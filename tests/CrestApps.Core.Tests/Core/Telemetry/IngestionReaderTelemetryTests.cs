using System.Text;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class IngestionReaderTelemetryTests
{
    [Fact]
    public async Task APreferredReaderThatFails_IsCountedAsAFallback()
    {
        using var collector = new TelemetryCollector();

        await ReadAsync(new UnavailableServiceReader(new InvalidOperationException("The service is down.")));

        var measurement = Assert.Single(collector.GetMeasurements("crestapps.core.ai.ingestion.reader.fallbacks", AITelemetry.ReaderTag, nameof(UnavailableServiceReader)));

        Assert.Equal("read", measurement.Tags[CrestAppsTelemetry.OperationTag]);
    }

    [Fact]
    public async Task APreferredReaderThatDoesNotHandleTheFile_IsNotCountedAsAFallback()
    {
        using var collector = new TelemetryCollector();

        await ReadAsync(new UnavailableServiceReader(new NotSupportedException("Not configured.")));

        Assert.Empty(collector.GetMeasurements("crestapps.core.ai.ingestion.reader.fallbacks", AITelemetry.ReaderTag, nameof(UnavailableServiceReader)));
    }

    private static async Task ReadAsync(IngestionDocumentReader preferred)
    {
        var reader = new FallbackIngestionDocumentReader(preferred, new PlainTextReader(), NullLogger<FallbackIngestionDocumentReader>.Instance);

        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("the content"));

        await reader.ReadAsync(source, "doc.pdf", "application/pdf", TestContext.Current.CancellationToken);
    }

    private sealed class UnavailableServiceReader : IngestionDocumentReader
    {
        private readonly Exception _exception;

        public UnavailableServiceReader(Exception exception)
        {
            _exception = exception;
        }

        public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }

    private sealed class PlainTextReader : IngestionDocumentReader
    {
        public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IngestionDocument(identifier));
        }
    }
}
