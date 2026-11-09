using System.Diagnostics;
using System.Text;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Memory;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class SpanTelemetryTests
{
    [Fact]
    public async Task ReadingADocument_StartsAReadSpanAndTimesTheReader()
    {
        using var collector = new TelemetryCollector();
        var pipeline = new DefaultAIDocumentIngestionPipeline(new SingleReaderResolver(new SpanTestReader()), []);
        var fileName = "doc-" + Guid.NewGuid().ToString("N") + ".txt";

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("the content"));

        await pipeline.IngestAsync(content, fileName, "text/plain", context: null, TestContext.Current.CancellationToken);

        var span = Assert.Single(collector.Activities, activity => activity.OperationName == "crestapps.ingest.read" && Equals(activity.GetTagItem(AITelemetry.ReaderTag), nameof(SpanTestReader)));

        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
        Assert.Contains(collector.GetMeasurements("crestapps.core.ai.ingestion.reader.duration", AITelemetry.ReaderTag, nameof(SpanTestReader)), measurement => Equals(measurement.Tags[CrestAppsTelemetry.OutcomeTag], CrestAppsTelemetry.OutcomeOk));
    }

    [Fact]
    public async Task AQueuedMemoryWriteThatFails_RunsInABackgroundItemSpanLinkedToTheRequestThatQueuedIt()
    {
        using var collector = new TelemetryCollector();
        var memoryId = "memory-" + Guid.NewGuid().ToString("N");

        AIMemoryIndexingWorkItem workItem;

        using (var request = CrestAppsTelemetry.ActivitySource.StartActivity("test.request"))
        {
            workItem = new AIMemoryIndexingWorkItem(null, memoryId);
        }

        // No memory indexing service is registered, so the work item fails and the failure is recorded.
        var services = new ServiceCollection().BuildServiceProvider();
        var worker = new AIMemoryIndexingBackgroundService(
            new AIMemoryIndexingQueue(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AIMemoryIndexingBackgroundService>.Instance);

        await worker.ProcessAsync(workItem, TestContext.Current.CancellationToken);

        var span = Assert.Single(collector.Activities, activity => activity.OperationName == "crestapps.background.item" && activity.Links.Any(link => link.Context == workItem.QueuedContext));

        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("delete", span.GetTagItem(CrestAppsTelemetry.WorkItemTypeTag));
        Assert.NotEqual(workItem.QueuedContext.TraceId, span.TraceId);
    }

    private sealed class SingleReaderResolver : IIngestionDocumentReaderResolver
    {
        private readonly IngestionDocumentReader _reader;

        public SingleReaderResolver(IngestionDocumentReader reader)
        {
            _reader = reader;
        }

        public IngestionDocumentReader Resolve(string fileName, string mediaType, Stream content = null)
        {
            return _reader;
        }
    }

    private sealed class SpanTestReader : IngestionDocumentReader
    {
        public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IngestionDocument(identifier));
        }
    }
}
