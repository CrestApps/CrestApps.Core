using System.Diagnostics;
using System.Diagnostics.Metrics;
using CrestApps.Core.AI;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class CrestAppsTelemetryTests
{
    // The instrument names are a public contract: dashboards and alerts are built on them.
    private static readonly string[] _expectedInstrumentNames =
    [
        "crestapps.core.index.operation.duration",
        "crestapps.core.store.commit.duration",
        "crestapps.core.queue.depth",
        "crestapps.core.queue.wait.duration",
        "crestapps.core.background.item.duration",
        "crestapps.core.background.cycle.duration",
        "crestapps.core.ai.orchestration.duration",
        "crestapps.core.ai.orchestration.time_to_first_token",
        "crestapps.core.ai.completion.time_to_first_token",
        "crestapps.core.ai.completion.errors",
        "crestapps.core.ai.planning.duration",
        "crestapps.core.ai.tools.scoped_count",
        "crestapps.core.ai.tool.duration",
        "crestapps.core.ai.resilience.retries",
        "crestapps.core.ai.handler.errors",
        "crestapps.core.ai.security.prompt_checks",
        "crestapps.core.ai.security.evaluation.duration",
        "crestapps.core.ai.guardrail.duration",
        "crestapps.core.ai.ratelimit.rejections",
        "crestapps.core.ai.retrieval.duration",
        "crestapps.core.ai.retrieval.results",
        "crestapps.core.ai.retrieval.fallbacks",
        "crestapps.core.ai.embedding.fallbacks",
        "crestapps.core.ai.ingestion.duration",
        "crestapps.core.ai.ingestion.chunks",
        "crestapps.core.ai.ingestion.run.duration",
        "crestapps.core.ai.ingestion.run.items",
        "crestapps.core.chat.turn.duration",
        "crestapps.core.realtime.session.duration",
        "crestapps.core.realtime.sessions.active",
        "crestapps.core.realtime.grounding.duration",
        "crestapps.core.realtime.first_audio_latency",
        "crestapps.core.realtime.interruptions",
        "crestapps.core.mcp.client.connect.duration",
        "crestapps.core.ai.webcrawler.crawl.duration",
        "crestapps.core.ai.usage.requests",
        "crestapps.core.ai.usage.tokens",
        "crestapps.core.ai.usage.duration",
    ];

    [Fact]
    public void Instruments_ArePublishedUnderTheirDocumentedNamesOnTheCrestAppsMeter()
    {
        var published = GetPublishedInstruments();

        foreach (var name in _expectedInstrumentNames)
        {
            Assert.True(published.ContainsKey(name), $"The instrument '{name}' was not published on the '{CrestAppsTelemetry.SourceName}' meter.");
        }
    }

    [Fact]
    public void DurationInstruments_RecordSecondsWithAdvisedBuckets()
    {
        var durations = GetPublishedInstruments().Values
            .Where(instrument => instrument.Name.EndsWith(".duration", StringComparison.Ordinal) || instrument.Name.EndsWith("time_to_first_token", StringComparison.Ordinal) || instrument.Name.EndsWith("latency", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(durations);

        foreach (var instrument in durations)
        {
            var histogram = Assert.IsType<Histogram<double>>(instrument);

            Assert.Equal("s", histogram.Unit);
            Assert.NotNull(histogram.Advice?.HistogramBucketBoundaries);
        }
    }

    [Fact]
    public void StartLinkedActivity_WhenTheWorkWasQueuedInATrace_StartsANewTraceLinkedToIt()
    {
        using var collector = new TelemetryCollector();
        using var producer = CrestAppsTelemetry.ActivitySource.StartActivity("test.producer");

        Assert.NotNull(producer);

        var queuedContext = producer.Context;

        producer.Stop();

        using var consumer = CrestAppsTelemetry.StartLinkedActivity("test.consumer", queuedContext);

        Assert.NotNull(consumer);
        Assert.NotEqual(queuedContext.TraceId, consumer.TraceId);
        Assert.Equal(queuedContext, Assert.Single(consumer.Links).Context);
    }

    [Fact]
    public void StartLinkedActivity_WhenNothingWasCurrentWhenQueued_StartsWithoutALink()
    {
        using var collector = new TelemetryCollector();
        using var consumer = CrestAppsTelemetry.StartLinkedActivity("test.consumer", default);

        Assert.NotNull(consumer);
        Assert.Empty(consumer.Links);
    }

    [Fact]
    public void GetOutcome_DistinguishesCancellationFromFailure()
    {
        Assert.Equal(CrestAppsTelemetry.OutcomeCancelled, CrestAppsTelemetry.GetOutcome(new OperationCanceledException()));
        Assert.Equal(CrestAppsTelemetry.OutcomeCancelled, CrestAppsTelemetry.GetOutcome(new TaskCanceledException()));
        Assert.Equal(CrestAppsTelemetry.OutcomeError, CrestAppsTelemetry.GetOutcome(new InvalidOperationException()));
    }

    [Fact]
    public void RecordIndexOperation_TagsProviderOperationAndOutcome()
    {
        using var collector = new TelemetryCollector();
        var provider = "test-provider-" + Guid.NewGuid().ToString("N");

        CrestAppsTelemetry.RecordIndexOperation(provider, "write", Stopwatch.GetTimestamp(), succeeded: false);

        var measurement = Assert.Single(collector.GetMeasurements("crestapps.core.index.operation.duration", CrestAppsTelemetry.ProviderTag, provider));

        Assert.Equal("write", measurement.Tags[CrestAppsTelemetry.OperationTag]);
        Assert.Equal(CrestAppsTelemetry.OutcomeError, measurement.Tags[CrestAppsTelemetry.OutcomeTag]);
    }

    private static Dictionary<string, Instrument> GetPublishedInstruments()
    {
        // Touching the catalogues creates their instruments, which the listener is then told about on Start.
        _ = CrestAppsTelemetry.IndexOperationDuration;
        _ = AITelemetry.OrchestrationDuration;

        var published = new Dictionary<string, Instrument>(StringComparer.Ordinal);

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == CrestAppsTelemetry.SourceName)
                {
                    published[instrument.Name] = instrument;
                }
            },
        };

        listener.Start();

        return published;
    }
}
