using System.Diagnostics.Metrics;

namespace CrestApps.Core.AI;

/// <summary>
/// The instruments of the AI pipeline: orchestration, completions, tools, prompt security, retrieval,
/// ingestion, chat, realtime voice, MCP and usage. They are created on <see cref="CrestAppsTelemetry.Meter"/>, so
/// subscribing to <see cref="CrestAppsTelemetry.SourceName"/> collects them, and they record nothing until a
/// listener does.
/// </summary>
/// <remarks>
/// The instrument names are a public contract that dashboards and alerts depend on. The full catalogue, with
/// the tags each instrument carries, is in the observability documentation.
/// </remarks>
public static class AITelemetry
{
    /// <summary>
    /// The tag that records which orchestrator ran a turn, such as <c>default</c>, <c>claude</c> or <c>copilot</c>.
    /// </summary>
    public const string OrchestratorTag = "orchestrator";

    /// <summary>
    /// The tag that records the AI client, which is the provider name of a deployment such as <c>OpenAI</c>.
    /// </summary>
    public const string ClientTag = "client";

    /// <summary>
    /// The tag that records where a tool comes from: <c>local</c>, <c>system</c>, <c>mcp</c>, <c>a2a</c>,
    /// <c>instance</c> or <c>agent</c>.
    /// </summary>
    public const string ToolSourceTag = "tool.source";

    /// <summary>
    /// The span attribute that records the name of an invoked tool. It is never put on a metric, because remote
    /// tool names are unbounded.
    /// </summary>
    public const string ToolNameTag = "tool.name";

    /// <summary>
    /// The span attribute that records the chat session id. Ids are unbounded, so they never go on a metric.
    /// </summary>
    public const string SessionIdAttribute = "crestapps.session.id";

    /// <summary>
    /// The span attribute that records the AI profile id. Ids are unbounded, so they never go on a metric.
    /// </summary>
    public const string ProfileIdAttribute = "crestapps.profile.id";

    /// <summary>
    /// The span attribute that records the AI data source id. Ids are unbounded, so they never go on a metric.
    /// </summary>
    public const string DataSourceIdAttribute = "crestapps.data_source.id";

    /// <summary>
    /// The tag that records why something happened, such as why a request was retried or a search fell back.
    /// </summary>
    public const string ReasonTag = "reason";

    /// <summary>
    /// The tag that records the attempt number of a retry, capped so it stays bounded.
    /// </summary>
    public const string AttemptTag = "attempt";

    /// <summary>
    /// The tag that records the type name of a handler that failed.
    /// </summary>
    public const string HandlerTag = "handler";

    /// <summary>
    /// The tag that records the decision of a prompt security check: <c>allowed</c>, <c>flagged</c> or <c>blocked</c>.
    /// </summary>
    public const string DecisionTag = "decision";

    /// <summary>
    /// The tag that records whether text was checked on its way in (<c>input</c>) or out (<c>output</c>).
    /// </summary>
    public const string DirectionTag = "direction";

    /// <summary>
    /// The <see cref="DirectionTag"/> value of text on its way to the model.
    /// </summary>
    public const string InputDirection = "input";

    /// <summary>
    /// The <see cref="DirectionTag"/> value of text on its way back from the model.
    /// </summary>
    public const string OutputDirection = "output";

    /// <summary>
    /// The tag that records the rate limiter that rejected a request.
    /// </summary>
    public const string LimiterTag = "limiter";

    /// <summary>
    /// The tag that records the retrieval mode of a data source search: <c>chunk</c> or <c>hierarchical</c>.
    /// </summary>
    public const string ModeTag = "mode";

    /// <summary>
    /// The tag that records how one item of an ingestion run ended.
    /// </summary>
    public const string ResultTag = "result";

    /// <summary>
    /// The tag that records why a realtime voice session ended.
    /// </summary>
    public const string EndReasonTag = "end_reason";

    /// <summary>
    /// The tag that records the transport of an MCP connection, such as <c>sse</c> or <c>stdio</c>.
    /// </summary>
    public const string TransportTag = "transport";

    /// <summary>
    /// The tag that records the kind of tokens counted: <c>input</c>, <c>output</c>, <c>cached</c>,
    /// <c>reasoning</c> or <c>audio</c>.
    /// </summary>
    public const string TokenTypeTag = "token.type";

    /// <summary>
    /// The tag that records why the framework made an AI request, from <see cref="Completions.AIUsagePurposes"/>.
    /// </summary>
    public const string PurposeTag = "purpose";

    /// <summary>
    /// The tag that records the deployment that served an AI request.
    /// </summary>
    public const string DeploymentTag = "deployment";

    /// <summary>
    /// The tag that records whether an AI response was streamed.
    /// </summary>
    public const string StreamingTag = "streaming";

    /// <summary>
    /// The duration of an orchestrated turn, from the user's message to the last streamed update, tagged with
    /// <see cref="OrchestratorTag"/> and <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> OrchestrationDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.orchestration.duration",
        "The duration of an orchestrated turn, from the user's message to the last streamed update.");

    /// <summary>
    /// The time from the start of an orchestrated turn to its first streamed text, tagged with <see cref="OrchestratorTag"/>.
    /// </summary>
    public static readonly Histogram<double> OrchestrationTimeToFirstToken = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.orchestration.time_to_first_token",
        "The time from the start of an orchestrated turn to its first streamed text.");

    /// <summary>
    /// The time from the start of a streamed completion to its first text, tagged with <see cref="ClientTag"/>.
    /// </summary>
    public static readonly Histogram<double> CompletionTimeToFirstToken = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.completion.time_to_first_token",
        "The time from the start of a streamed completion to its first text.");

    /// <summary>
    /// The number of completions that failed, including those whose failure is logged and swallowed, tagged with
    /// <see cref="ClientTag"/> and <see cref="CrestAppsTelemetry.ErrorTypeTag"/>.
    /// </summary>
    public static readonly Counter<long> CompletionErrors = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.completion.errors",
        "{error}",
        "The number of completions that failed, including those whose failure is logged and swallowed.");

    /// <summary>
    /// The duration of the planning call that picks the tools for a turn, tagged with
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>. An <see cref="CrestAppsTelemetry.OutcomeError"/> means the turn
    /// fell back to injecting every tool.
    /// </summary>
    public static readonly Histogram<double> PlanningDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.planning.duration",
        "The duration of the planning call that picks the tools for a turn.");

    /// <summary>
    /// The number of tools handed to the model for a turn after scoping.
    /// </summary>
    public static readonly Histogram<int> ScopedTools = CrestAppsTelemetry.Meter.CreateHistogram<int>(
        "crestapps.core.ai.tools.scoped_count",
        "{tool}",
        "The number of tools handed to the model for a turn after scoping.");

    /// <summary>
    /// The duration of one tool invocation, tagged with <see cref="ToolSourceTag"/> and <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> ToolDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.tool.duration",
        "The duration of one tool invocation.");

    /// <summary>
    /// The number of AI requests retried after the provider rate limited them, tagged with
    /// <see cref="CrestAppsTelemetry.OperationTag"/>, <see cref="ReasonTag"/> and <see cref="AttemptTag"/>.
    /// </summary>
    public static readonly Counter<long> ResilienceRetries = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.resilience.retries",
        "{retry}",
        "The number of AI requests retried after the provider rate limited them.");

    /// <summary>
    /// The number of failures of a completion or context handler that were logged and skipped, tagged with
    /// <see cref="HandlerTag"/>.
    /// </summary>
    public static readonly Counter<long> HandlerErrors = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.handler.errors",
        "{error}",
        "The number of failures of a completion or context handler that were logged and skipped.");

    /// <summary>
    /// The number of prompt security checks, tagged with <see cref="DecisionTag"/> and <see cref="DirectionTag"/>.
    /// </summary>
    public static readonly Counter<long> PromptChecks = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.security.prompt_checks",
        "{check}",
        "The number of prompt security checks.");

    /// <summary>
    /// The duration of a prompt security check, tagged with <see cref="DirectionTag"/>.
    /// </summary>
    public static readonly Histogram<double> PromptEvaluationDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.security.evaluation.duration",
        "The duration of a prompt security check.");

    /// <summary>
    /// The duration of a guardrail evaluation, tagged with <see cref="DirectionTag"/> and
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>. A failed evaluation lets the text through.
    /// </summary>
    public static readonly Histogram<double> GuardrailDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.guardrail.duration",
        "The duration of a guardrail evaluation.");

    /// <summary>
    /// The number of chat requests refused by a rate limiter, tagged with <see cref="LimiterTag"/>.
    /// </summary>
    public static readonly Counter<long> RateLimitRejections = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.ratelimit.rejections",
        "{request}",
        "The number of chat requests refused by a rate limiter.");

    /// <summary>
    /// The duration of a data source search, tagged with <see cref="ModeTag"/> and
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>: <c>ok</c>, <c>partial</c>, <c>empty</c> or <c>error</c>.
    /// </summary>
    public static readonly Histogram<double> RetrievalDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.retrieval.duration",
        "The duration of a data source search.");

    /// <summary>
    /// The number of results a data source search handed to the model, tagged with <see cref="ModeTag"/>.
    /// </summary>
    public static readonly Histogram<int> RetrievalResults = CrestAppsTelemetry.Meter.CreateHistogram<int>(
        "crestapps.core.ai.retrieval.results",
        "{result}",
        "The number of results a data source search handed to the model.");

    /// <summary>
    /// The number of data source searches that fell back to a weaker form, tagged with <see cref="ReasonTag"/>.
    /// </summary>
    public static readonly Counter<long> RetrievalFallbacks = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.retrieval.fallbacks",
        "{fallback}",
        "The number of data source searches that fell back to a weaker form.");

    /// <summary>
    /// The number of document chunks stored without an embedding because generating it failed.
    /// </summary>
    public static readonly Counter<long> EmbeddingFallbacks = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.embedding.fallbacks",
        "{chunk}",
        "The number of document chunks stored without an embedding because generating it failed.");

    /// <summary>
    /// The duration of reading, chunking and embedding one uploaded document, tagged with
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> IngestionDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.ingestion.duration",
        "The duration of reading, chunking and embedding one uploaded document.");

    /// <summary>
    /// The number of chunks one ingested document produced.
    /// </summary>
    public static readonly Histogram<int> IngestionChunks = CrestAppsTelemetry.Meter.CreateHistogram<int>(
        "crestapps.core.ai.ingestion.chunks",
        "{chunk}",
        "The number of chunks one ingested document produced.");

    /// <summary>
    /// The duration of one file source ingestion run, tagged with <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> IngestionRunDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.ingestion.run.duration",
        "The duration of one file source ingestion run.",
        CrestAppsTelemetry.LongDurationBucketBoundaries);

    /// <summary>
    /// The number of items an ingestion run processed, tagged with <see cref="ResultTag"/>.
    /// </summary>
    public static readonly Counter<long> IngestionRunItems = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.ingestion.run.items",
        "{item}",
        "The number of items an ingestion run processed.");

    /// <summary>
    /// The duration of one chat turn on the hub, from receiving the message to the end of the reply, tagged
    /// with <see cref="CrestAppsTelemetry.OutcomeTag"/>: <c>ok</c>, <c>blocked</c>, <c>error</c> or <c>cancelled</c>.
    /// </summary>
    public static readonly Histogram<double> ChatTurnDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.chat.turn.duration",
        "The duration of one chat turn, from receiving the message to the end of the reply.");

    /// <summary>
    /// The duration of a realtime voice session, tagged with <see cref="EndReasonTag"/>.
    /// </summary>
    public static readonly Histogram<double> RealtimeSessionDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.realtime.session.duration",
        "The duration of a realtime voice session.",
        CrestAppsTelemetry.LongDurationBucketBoundaries);

    /// <summary>
    /// The number of realtime voice sessions in progress.
    /// </summary>
    public static readonly UpDownCounter<long> RealtimeActiveSessions = CrestAppsTelemetry.Meter.CreateUpDownCounter<long>(
        "crestapps.core.realtime.sessions.active",
        "{session}",
        "The number of realtime voice sessions in progress.");

    /// <summary>
    /// The time spent grounding a realtime turn in documents, data sources and memory before the reply starts.
    /// </summary>
    public static readonly Histogram<double> RealtimeGroundingDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.realtime.grounding.duration",
        "The time spent grounding a realtime turn before the reply starts.");

    /// <summary>
    /// The time from the provider committing the user's utterance, once they stop speaking, to the first audio of
    /// the reply. It is measured on the server, so it excludes the browser's network and playback buffer.
    /// </summary>
    public static readonly Histogram<double> RealtimeFirstAudioLatency = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.realtime.first_audio_latency",
        "The time from the end of the user's utterance to the first audio of the reply.");

    /// <summary>
    /// The number of times a user interrupted the assistant's spoken reply.
    /// </summary>
    public static readonly Counter<long> RealtimeInterruptions = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.interruptions",
        "{interruption}",
        "The number of times a user interrupted the assistant's spoken reply.");

    /// <summary>
    /// The duration of connecting to an MCP server and completing its handshake, tagged with
    /// <see cref="TransportTag"/> and <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> McpClientConnectDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.mcp.client.connect.duration",
        "The duration of connecting to an MCP server and completing its handshake.");

    /// <summary>
    /// The duration of crawling one web data source, tagged with <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> WebCrawlDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.webcrawler.crawl.duration",
        "The duration of crawling one web data source.",
        CrestAppsTelemetry.LongDurationBucketBoundaries);

    /// <summary>
    /// The number of metered AI requests, tagged with <see cref="CrestAppsTelemetry.OperationTag"/>,
    /// <see cref="ClientTag"/>, <see cref="DeploymentTag"/>, <see cref="PurposeTag"/> and <see cref="StreamingTag"/>.
    /// </summary>
    public static readonly Counter<long> UsageRequests = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.usage.requests",
        "{request}",
        "The number of metered AI requests.");

    /// <summary>
    /// The number of tokens metered AI requests used, tagged like <see cref="UsageRequests"/> plus <see cref="TokenTypeTag"/>.
    /// </summary>
    public static readonly Counter<long> UsageTokens = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.usage.tokens",
        "{token}",
        "The number of tokens metered AI requests used.");

    /// <summary>
    /// The response latency of metered AI requests, tagged like <see cref="UsageRequests"/>.
    /// </summary>
    public static readonly Histogram<double> UsageDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.usage.duration",
        "The response latency of metered AI requests.");
}
