using System.Diagnostics;
using System.Diagnostics.Metrics;
using CrestApps.Core.AI;
using CrestApps.Core.Templates;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class CrestAppsTelemetryTests
{
    // The instrument names are a public contract: dashboards and alerts are built on them.
    private static readonly string[] _expectedInstrumentNames =
    [
        "crestapps.core.index.operation.duration",
        "crestapps.core.index.documents",
        "crestapps.core.store.commit.duration",
        "crestapps.core.queue.depth",
        "crestapps.core.queue.wait.duration",
        "crestapps.core.background.item.duration",
        "crestapps.core.background.cycle.duration",
        "crestapps.core.background.last_success",
        "crestapps.core.background.task.faults",
        "crestapps.core.ai.orchestration.duration",
        "crestapps.core.ai.orchestration.time_to_first_token",
        "crestapps.core.ai.completion.time_to_first_token",
        "crestapps.core.ai.completion.errors",
        "crestapps.core.ai.planning.duration",
        "crestapps.core.ai.context_build.duration",
        "crestapps.core.ai.tools.scoped_count",
        "crestapps.core.ai.tool.duration",
        "crestapps.core.ai.tool.denied",
        "crestapps.core.ai.resilience.retries",
        "crestapps.core.ai.resilience.stream_failures",
        "crestapps.core.ai.ratelimit.tracked_keys",
        "crestapps.core.ai.client_cache.size",
        "crestapps.core.ai.handler.errors",
        "crestapps.core.ai.security.prompt_checks",
        "crestapps.core.ai.security.evaluation.duration",
        "crestapps.core.ai.guardrail.duration",
        "crestapps.core.ai.ratelimit.rejections",
        "crestapps.core.ai.retrieval.duration",
        "crestapps.core.ai.retrieval.results",
        "crestapps.core.ai.retrieval.fallbacks",
        "crestapps.core.ai.embedding.fallbacks",
        "crestapps.core.ai.rag.preemptive.duration",
        "crestapps.core.ai.rag.query_extraction.duration",
        "crestapps.core.ai.memory.search.duration",
        "crestapps.core.ai.memory.saves",
        "crestapps.core.ai.ingestion.duration",
        "crestapps.core.ai.ingestion.chunks",
        "crestapps.core.ai.ingestion.reader.duration",
        "crestapps.core.ai.ingestion.reader.fallbacks",
        "crestapps.core.ai.ingestion.figures",
        "crestapps.core.ai.ingestion.run.duration",
        "crestapps.core.ai.ingestion.run.items",
        "crestapps.core.ai.ingestion.connector.duration",
        "crestapps.core.ai.ingestion.connector.bytes",
        "crestapps.core.ai.documents.uploads.rejected",
        "crestapps.core.ai.documents.tabular.prepare.duration",
        "crestapps.core.chat.turn.duration",
        "crestapps.core.chat.messages",
        "crestapps.core.chat.response.duration",
        "crestapps.core.chat.sessions.closed",
        "crestapps.core.chat.post_processing.duration",
        "crestapps.core.realtime.session.duration",
        "crestapps.core.realtime.sessions.active",
        "crestapps.core.realtime.grounding.duration",
        "crestapps.core.realtime.first_audio_latency",
        "crestapps.core.realtime.interruptions",
        "crestapps.core.realtime.provider.errors",
        "crestapps.core.realtime.acknowledgements",
        "crestapps.core.realtime.watchdog.fires",
        "crestapps.core.realtime.cascaded.leg.latency",
        "crestapps.core.realtime.speech.stalls",
        "crestapps.core.realtime.speech.disconnects",
        "crestapps.core.realtime.webrtc.peers.active",
        "crestapps.core.realtime.webrtc.connection_states",
        "crestapps.core.realtime.webrtc.codec.errors",
        "crestapps.core.realtime.webrtc.stall_frames",
        "crestapps.core.mcp.client.connect.duration",
        "crestapps.core.mcp.clients.active",
        "crestapps.core.mcp.tool_resolution.duration",
        "crestapps.core.a2a.client.duration",
        "crestapps.core.a2a.agent_card.fetch.duration",
        "crestapps.core.a2a.agent_card.lookups",
        "crestapps.core.ai.webcrawler.crawl.duration",
        "crestapps.core.ai.webcrawler.pages",
        "crestapps.core.ai.webcrawler.sitemap.errors",
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

    [Fact]
    public void RecordIndexOperation_CountsTheDocumentsOfASuccessfulOperationOnly()
    {
        using var collector = new TelemetryCollector();
        var provider = "test-provider-" + Guid.NewGuid().ToString("N");

        CrestAppsTelemetry.RecordIndexOperation(provider, "delete", Stopwatch.GetTimestamp(), succeeded: true, documentCount: 3);
        CrestAppsTelemetry.RecordIndexOperation(provider, "write", Stopwatch.GetTimestamp(), succeeded: false, documentCount: 5);

        var measurement = Assert.Single(collector.GetMeasurements("crestapps.core.index.documents", CrestAppsTelemetry.ProviderTag, provider));

        Assert.Equal(3d, measurement.Value);
        Assert.Equal("delete", measurement.Tags[CrestAppsTelemetry.OperationTag]);
    }

    [Fact]
    public void RecordBackgroundCycle_RecordsTheLastSuccessOnlyForACycleThatSucceeded()
    {
        using var collector = new TelemetryCollector();
        var succeeding = "test-service-" + Guid.NewGuid().ToString("N");
        var failing = "test-service-" + Guid.NewGuid().ToString("N");

        CrestAppsTelemetry.RecordBackgroundCycle(succeeding, Stopwatch.GetTimestamp(), CrestAppsTelemetry.OutcomeOk);
        CrestAppsTelemetry.RecordBackgroundCycle(failing, Stopwatch.GetTimestamp(), CrestAppsTelemetry.OutcomeError);

        Assert.Single(collector.GetMeasurements("crestapps.core.background.cycle.duration", CrestAppsTelemetry.ServiceTag, succeeding));
        Assert.Single(collector.GetMeasurements("crestapps.core.background.cycle.duration", CrestAppsTelemetry.ServiceTag, failing));

        var lastSuccess = Assert.Single(collector.GetMeasurements("crestapps.core.background.last_success", CrestAppsTelemetry.ServiceTag, succeeding));

        Assert.InRange(lastSuccess.Value, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(), DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());
        Assert.Empty(collector.GetMeasurements("crestapps.core.background.last_success", CrestAppsTelemetry.ServiceTag, failing));
    }

    [Fact]
    public void TemplateRenderDuration_IsPublishedInSecondsOnTheTemplatesMeter()
    {
        _ = TemplateTelemetry.RenderDuration;

        Instrument published = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == TemplateTelemetry.MeterName && instrument.Name == "crestapps.core.templates.render.duration")
                {
                    published = instrument;
                }
            },
        };

        listener.Start();

        var histogram = Assert.IsType<Histogram<double>>(published);

        Assert.Equal("s", histogram.Unit);
        Assert.NotNull(histogram.Advice?.HistogramBucketBoundaries);
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
