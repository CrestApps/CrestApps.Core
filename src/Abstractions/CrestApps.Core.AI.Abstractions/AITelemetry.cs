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
    /// The span attribute that records how deep a sub-agent call is nested, where 1 is an agent the user's
    /// orchestrator called.
    /// </summary>
    public const string AgentDepthAttribute = "crestapps.agent.depth";

    /// <summary>
    /// The span attribute that records the MCP connection a proxied tool call went to.
    /// </summary>
    public const string McpConnectionAttribute = "crestapps.mcp.connection";

    /// <summary>
    /// The tag that records how the tools of a turn were chosen: <c>all</c>, <c>relevance</c> or <c>planned</c>.
    /// </summary>
    public const string ToolsModeTag = "tools.mode";

    /// <summary>
    /// The tag that records which stage of a build or pipeline was measured.
    /// </summary>
    public const string PhaseTag = "phase";

    /// <summary>
    /// The tag that records the type of the cached clients.
    /// </summary>
    public const string CacheTag = "cache";

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
    /// The tag that records the kind of rule that flagged or blocked a prompt, such as <c>max-length</c> or
    /// <c>guardrail</c>. The topic a guardrail model names is left off, because the model can name anything.
    /// </summary>
    public const string RuleCategoryTag = "rule_category";

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
    /// The tag that records which limits applied to a rate-limited caller: <c>authenticated</c> or <c>anonymous</c>.
    /// </summary>
    public const string TierTag = "tier";

    /// <summary>
    /// The tag that records how a search was answered: <c>chunk</c> or <c>hierarchical</c> for a data source search,
    /// <c>index</c> or <c>store</c> for a memory search.
    /// </summary>
    public const string ModeTag = "mode";

    /// <summary>
    /// The tag that records how one item of an ingestion run ended.
    /// </summary>
    public const string ResultTag = "result";

    /// <summary>
    /// The tag that records the document reader that read a file, by its type name.
    /// </summary>
    public const string ReaderTag = "reader";

    /// <summary>
    /// The tag that records the response handler that answered a chat message.
    /// </summary>
    public const string ResponseHandlerTag = "response_handler";

    /// <summary>
    /// The tag that records why a realtime voice session ended.
    /// </summary>
    public const string EndReasonTag = "end_reason";

    /// <summary>
    /// The tag that records the leg of a cascaded realtime session: <c>stt</c>, <c>llm</c> or <c>tts</c>.
    /// </summary>
    public const string LegTag = "leg";

    /// <summary>
    /// The tag that records the state a WebRTC peer connection moved to, such as <c>connected</c> or <c>failed</c>.
    /// </summary>
    public const string StateTag = "state";

    /// <summary>
    /// The tag that records the transport of an MCP connection, such as <c>sse</c> or <c>stdio</c>.
    /// </summary>
    public const string TransportTag = "transport";

    /// <summary>
    /// The tag that records the ingestion connector that listed or read a file, such as <c>ftp</c>, <c>sftp</c> or
    /// <c>filesystem</c>.
    /// </summary>
    public const string ConnectorTag = "connector";

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
    /// The duration of building an orchestration or completion context, including the preemptive retrieval its
    /// handlers run, tagged with <see cref="PhaseTag"/>: <c>orchestration</c> or <c>completion</c>.
    /// </summary>
    public static readonly Histogram<double> ContextBuildDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.context_build.duration",
        "The duration of building an orchestration or completion context.");

    /// <summary>
    /// The number of tools handed to the model for a turn after scoping, tagged with <see cref="ToolsModeTag"/>.
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
    /// The number of tools left out of a request because the caller may not use them, tagged with <see cref="ReasonTag"/>.
    /// </summary>
    public static readonly Counter<long> ToolDenials = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.tool.denied",
        "{tool}",
        "The number of tools left out of a request because the caller may not use them.");

    /// <summary>
    /// The number of AI requests retried, tagged with <see cref="CrestAppsTelemetry.OperationTag"/> for the
    /// framework's resilience pipelines or <see cref="ClientTag"/> for the Azure OpenAI SDK's own retry policy, and
    /// with <see cref="ReasonTag"/> and <see cref="AttemptTag"/>.
    /// </summary>
    public static readonly Counter<long> ResilienceRetries = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.resilience.retries",
        "{retry}",
        "The number of AI requests retried after the provider rate limited them.");

    /// <summary>
    /// The number of streamed responses that failed after their first update, tagged with
    /// <see cref="CrestAppsTelemetry.OperationTag"/>. A stream is only retried until its first update arrives, so
    /// these reach the caller.
    /// </summary>
    public static readonly Counter<long> ResilienceStreamFailures = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.resilience.stream_failures",
        "{failure}",
        "The number of streamed responses that failed after their first update.");

    /// <summary>
    /// The number of keys a rate limiter is tracking, tagged with <see cref="LimiterTag"/>. A count that only grows
    /// shows keys that are never released.
    /// </summary>
    public static readonly Gauge<int> RateLimitTrackedKeys = CrestAppsTelemetry.Meter.CreateGauge<int>(
        "crestapps.core.ai.ratelimit.tracked_keys",
        "{key}",
        "The number of keys a rate limiter is tracking.");

    /// <summary>
    /// The number of provider clients held in a bounded client cache, tagged with <see cref="CacheTag"/>.
    /// </summary>
    public static readonly Gauge<int> ClientCacheSize = CrestAppsTelemetry.Meter.CreateGauge<int>(
        "crestapps.core.ai.client_cache.size",
        "{client}",
        "The number of provider clients held in a bounded client cache.");

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
    /// The duration of one preemptive RAG handler, which searches before the model is called, tagged with
    /// <see cref="HandlerTag"/> and <see cref="CrestAppsTelemetry.OutcomeTag"/>. A handler that fails is skipped, so
    /// its failures show here and on <see cref="HandlerErrors"/> only.
    /// </summary>
    public static readonly Histogram<double> RagPreemptiveDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.rag.preemptive.duration",
        "The duration of one preemptive RAG handler.");

    /// <summary>
    /// The duration of asking the utility model for the search queries of a turn, tagged with
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>. A failure falls back to searching for the message as typed.
    /// </summary>
    public static readonly Histogram<double> RagQueryExtractionDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.rag.query_extraction.duration",
        "The duration of asking the utility model for the search queries of a turn.");

    /// <summary>
    /// The duration of a user memory search, tagged with <see cref="ModeTag"/>: <c>index</c> when the vector index
    /// answered, <c>store</c> when the search fell back to the memory store.
    /// </summary>
    public static readonly Histogram<double> MemorySearchDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.memory.search.duration",
        "The duration of a user memory search.");

    /// <summary>
    /// The number of memories the model asked to save, tagged with <see cref="ResultTag"/>: <c>created</c>,
    /// <c>updated</c>, or <c>rejected</c> by the memory safety checks.
    /// </summary>
    public static readonly Counter<long> MemorySaves = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.memory.saves",
        "{memory}",
        "The number of memories the model asked to save.");

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
    /// The duration of a document reader turning a file into text and structure, tagged with <see cref="ReaderTag"/>
    /// and <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> IngestionReaderDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.ingestion.reader.duration",
        "The duration of a document reader turning a file into text and structure.");

    /// <summary>
    /// The number of times a document reader fell back to a simpler way of reading, tagged with
    /// <see cref="ReaderTag"/> and <see cref="CrestAppsTelemetry.OperationTag"/>, the step that failed, such as
    /// <c>layout</c> or <c>tables</c>. The document is still read, with less structure.
    /// </summary>
    public static readonly Counter<long> IngestionReaderFallbacks = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.ingestion.reader.fallbacks",
        "{fallback}",
        "The number of times a document reader fell back to a simpler way of reading.");

    /// <summary>
    /// The number of figures ingestion left waiting for a description, and what the description backfill did with
    /// them, tagged with <see cref="ResultTag"/>: <c>pending</c>, <c>described</c>, <c>failed</c> or <c>retry</c>.
    /// The backlog is the pending count less the described and failed ones.
    /// </summary>
    public static readonly Counter<long> IngestionFigures = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.ingestion.figures",
        "{figure}",
        "The number of figures left waiting for a description, and what the description backfill did with them.");

    /// <summary>
    /// The number of uploaded chat documents refused before they were read, tagged with <see cref="ReasonTag"/>:
    /// <c>size</c>, <c>scan</c>, <c>type</c> or <c>disabled</c>.
    /// </summary>
    public static readonly Counter<long> UploadRejections = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.documents.uploads.rejected",
        "{file}",
        "The number of uploaded chat documents refused before they were read.");

    /// <summary>
    /// The duration of loading a conversation's tabular files into the workspace the model queries, tagged with
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> TabularPreparationDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.documents.tabular.prepare.duration",
        "The duration of loading a conversation's tabular files into the workspace the model queries.");

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
    /// The number of chat messages handed to a response handler, tagged with <see cref="ResponseHandlerTag"/> and
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>. A message stopped before it reaches a handler, such as a blocked
    /// prompt, shows on <see cref="ChatTurnDuration"/> instead.
    /// </summary>
    public static readonly Counter<long> ChatMessages = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.chat.messages",
        "{message}",
        "The number of chat messages handed to a response handler.");

    /// <summary>
    /// The time a chat reply took to stream, from the first update requested to the last one, tagged with
    /// <see cref="ResponseHandlerTag"/>. It is the latency stored on each assistant message.
    /// </summary>
    public static readonly Histogram<double> ChatResponseDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.chat.response.duration",
        "The time a chat reply took to stream, from the first update requested to the last one.");

    /// <summary>
    /// The number of chat sessions the close cycle ended, tagged with <see cref="ReasonTag"/>: <c>closed</c> or
    /// <c>abandoned</c>.
    /// </summary>
    public static readonly Counter<long> ChatSessionsClosed = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.chat.sessions.closed",
        "{session}",
        "The number of chat sessions the close cycle ended.");

    /// <summary>
    /// The duration of the AI work that runs after a chat message or once a session closes, tagged with
    /// <see cref="CrestAppsTelemetry.OperationTag"/> (<c>data_extraction</c> or <c>post_close</c>) and
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>. Both swallow their failures, so a failed attempt only shows here
    /// and in the logs; a post-close attempt that leaves work for a retry counts as an error.
    /// </summary>
    public static readonly Histogram<double> ChatPostProcessingDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.chat.post_processing.duration",
        "The duration of the AI work that runs after a chat message or once a session closes.",
        CrestAppsTelemetry.LongDurationBucketBoundaries);

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
    /// The number of errors a realtime provider reported during a session, tagged with <see cref="ReasonTag"/>:
    /// <c>error</c>, <c>response</c> (a reply that ended in an error) or <c>transcription</c>. The benign turn-taking
    /// races a provider reports are left out.
    /// </summary>
    public static readonly Counter<long> RealtimeProviderErrors = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.provider.errors",
        "{error}",
        "The number of errors a realtime provider reported during a session.");

    /// <summary>
    /// The number of spoken acknowledgements a grounded realtime session used to cover a slow knowledge search,
    /// tagged with <see cref="ResultTag"/>: <c>spoken</c> or <c>failed</c>.
    /// </summary>
    public static readonly Counter<long> RealtimeAcknowledgements = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.acknowledgements",
        "{acknowledgement}",
        "The number of spoken acknowledgements a grounded realtime session used to cover a slow knowledge search.");

    /// <summary>
    /// The number of committed realtime turns that produced no transcript in time, so the watchdog asked for an
    /// answer anyway. Each one is a turn the provider swallowed.
    /// </summary>
    public static readonly Counter<long> RealtimeWatchdogFires = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.watchdog.fires",
        "{turn}",
        "The number of committed realtime turns that produced no transcript in time.");

    /// <summary>
    /// How long one leg of a cascaded realtime session took to start producing, tagged with <see cref="LegTag"/>:
    /// <c>stt</c> from the last partial transcript to the final one, <c>llm</c> from the start of a turn to the chat
    /// leg's first update, and <c>tts</c> from asking for a fragment to its first audio.
    /// </summary>
    public static readonly Histogram<double> RealtimeCascadedLegLatency = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.realtime.cascaded.leg.latency",
        "How long one leg of a cascaded realtime session took to start producing.");

    /// <summary>
    /// The number of turns a speech-to-text provider never finished, so the session ended them itself, tagged with
    /// <see cref="CrestAppsTelemetry.ProviderTag"/>.
    /// </summary>
    public static readonly Counter<long> RealtimeSpeechStalls = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.speech.stalls",
        "{turn}",
        "The number of turns a speech-to-text provider never finished.");

    /// <summary>
    /// The number of speech provider streams that ended while their session was still in use, tagged with
    /// <see cref="CrestAppsTelemetry.ProviderTag"/>. The caller goes unheard until the session is replaced.
    /// </summary>
    public static readonly Counter<long> RealtimeSpeechDisconnects = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.speech.disconnects",
        "{disconnect}",
        "The number of speech provider streams that ended while their session was still in use.");

    /// <summary>
    /// The number of WebRTC peers open.
    /// </summary>
    public static readonly UpDownCounter<long> RealtimeWebRtcActivePeers = CrestAppsTelemetry.Meter.CreateUpDownCounter<long>(
        "crestapps.core.realtime.webrtc.peers.active",
        "{peer}",
        "The number of WebRTC peers open.");

    /// <summary>
    /// The number of WebRTC peer connection state changes, tagged with <see cref="StateTag"/>.
    /// </summary>
    public static readonly Counter<long> RealtimeWebRtcConnectionStates = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.webrtc.connection_states",
        "{change}",
        "The number of WebRTC peer connection state changes.");

    /// <summary>
    /// The number of audio frames the Opus codec could not encode or decode, tagged with
    /// <see cref="CrestAppsTelemetry.OperationTag"/>: <c>encode</c> or <c>decode</c>. Each one is a gap the caller
    /// hears.
    /// </summary>
    public static readonly Counter<long> RealtimeWebRtcCodecErrors = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.webrtc.codec.errors",
        "{frame}",
        "The number of audio frames the Opus codec could not encode or decode.");

    /// <summary>
    /// The number of 20 ms frames a WebRTC peer filled with silence in the middle of a reply, tagged with
    /// <see cref="ReasonTag"/>: <c>provider</c> when the provider's audio fell behind, <c>pacing</c> when the
    /// sending thread did. Counted when the peer closes.
    /// </summary>
    public static readonly Counter<long> RealtimeWebRtcStallFrames = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.realtime.webrtc.stall_frames",
        "{frame}",
        "The number of frames a WebRTC peer filled with silence in the middle of a reply.");

    /// <summary>
    /// The duration of connecting to an MCP server and completing its handshake, tagged with
    /// <see cref="TransportTag"/> and <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> McpClientConnectDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.mcp.client.connect.duration",
        "The duration of connecting to an MCP server and completing its handshake.");

    /// <summary>
    /// The number of MCP client sessions open, tagged with <see cref="TransportTag"/>. A session ends when its client
    /// is disposed or its transport closes, so a count that only grows means clients are not being released.
    /// </summary>
    public static readonly UpDownCounter<long> McpActiveClients = CrestAppsTelemetry.Meter.CreateUpDownCounter<long>(
        "crestapps.core.mcp.clients.active",
        "{client}",
        "The number of MCP client sessions open.");

    /// <summary>
    /// The duration of working out which MCP tools a request can use, tagged with
    /// <see cref="CrestAppsTelemetry.OperationTag"/> (<c>registry</c> when the tools of the profile's connections
    /// are listed, <c>capabilities</c> when they are matched to the prompt) and
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>. Both carry on without the tools that failed, so a failure only
    /// shows here and in the logs.
    /// </summary>
    public static readonly Histogram<double> McpToolResolutionDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.mcp.tool_resolution.duration",
        "The duration of working out which MCP tools a request can use.");

    /// <summary>
    /// The duration of sending a message to a remote A2A agent and waiting for its reply, tagged with
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> A2AClientDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.a2a.client.duration",
        "The duration of sending a message to a remote A2A agent and waiting for its reply.");

    /// <summary>
    /// The duration of fetching a remote A2A agent's card, tagged with <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// Cards are cached, so this only runs on a <see cref="A2AAgentCardLookups"/> miss.
    /// </summary>
    public static readonly Histogram<double> A2AAgentCardFetchDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.a2a.agent_card.fetch.duration",
        "The duration of fetching a remote A2A agent's card.");

    /// <summary>
    /// The number of A2A agent card lookups, tagged with <see cref="CacheTag"/>: <c>hit</c> or <c>miss</c>.
    /// </summary>
    public static readonly Counter<long> A2AAgentCardLookups = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.a2a.agent_card.lookups",
        "{lookup}",
        "The number of A2A agent card lookups.");

    /// <summary>
    /// The duration of crawling one web data source, tagged with <see cref="CrestAppsTelemetry.OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> WebCrawlDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.webcrawler.crawl.duration",
        "The duration of crawling one web data source.",
        CrestAppsTelemetry.LongDurationBucketBoundaries);

    /// <summary>
    /// The number of pages a web crawler fetched, tagged with <see cref="ResultTag"/>: <c>fetched</c>,
    /// <c>skipped</c> (not HTML, or nothing to index) or <c>failed</c>. The URL stays off the metric.
    /// </summary>
    public static readonly Counter<long> WebCrawlPages = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.webcrawler.pages",
        "{page}",
        "The number of pages a web crawler fetched.");

    /// <summary>
    /// The number of sitemaps and robots.txt files that could not be read during discovery, tagged with
    /// <see cref="ReasonTag"/>: <c>sitemap</c> or <c>robots</c>. Discovery carries on without them.
    /// </summary>
    public static readonly Counter<long> WebCrawlSitemapErrors = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.webcrawler.sitemap.errors",
        "{error}",
        "The number of sitemaps and robots.txt files that could not be read during discovery.");

    /// <summary>
    /// The duration of an ingestion connector listing a folder or opening a file, tagged with
    /// <see cref="ConnectorTag"/>, <see cref="CrestAppsTelemetry.OperationTag"/> (<c>list</c> or <c>read</c>) and
    /// <see cref="CrestAppsTelemetry.OutcomeTag"/>. A connector reports a failure as an empty result, so it only
    /// shows here and in the logs.
    /// </summary>
    public static readonly Histogram<double> IngestionConnectorDuration = CrestAppsTelemetry.CreateDurationHistogram(
        "crestapps.core.ai.ingestion.connector.duration",
        "The duration of an ingestion connector listing a folder or opening a file.");

    /// <summary>
    /// The number of bytes ingestion connectors read, tagged with <see cref="ConnectorTag"/>.
    /// </summary>
    public static readonly Counter<long> IngestionConnectorBytes = CrestAppsTelemetry.Meter.CreateCounter<long>(
        "crestapps.core.ai.ingestion.connector.bytes",
        "By",
        "The number of bytes ingestion connectors read.");

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
