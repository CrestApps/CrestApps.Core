---
sidebar_label: Observability
sidebar_position: 18
title: Observability with OpenTelemetry
description: Traces, metrics and structured logs for the AI pipeline, from the chat hub to the model, the tools, retrieval, ingestion and realtime voice.
---

# Observability with OpenTelemetry

> See what the AI pipeline is doing: how long a turn takes and where the time goes, which tools ran and which failed, how retrieval and ingestion behave, and which failures were logged and swallowed.

Every CrestApps.Core package emits through one `ActivitySource` and one `Meter`, both named `CrestApps.Core` and
exposed as `CrestAppsTelemetry.ActivitySource` and `CrestAppsTelemetry.Meter`. They use `System.Diagnostics` only, so
the framework packages take no OpenTelemetry dependency, and nothing is recorded until something listens: an
application that does not collect telemetry pays nothing for it. A package that stands on its own, such as the
templating library, reports under a `CrestApps.Core.*` meter of its own.

## Turning it on

Reference `CrestApps.Core.OpenTelemetry` and add it to the host's OpenTelemetry pipeline:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .AddCrestAppsOpenTelemetry()
    .UseOtlpExporter();

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});
```

`AddCrestAppsOpenTelemetry()` collects:

| Source or meter | What it is |
| --- | --- |
| `CrestApps.Core` | The framework's own spans and metrics, listed below. |
| `CrestApps.Core.*` | The packages that report under a meter of their own, such as `CrestApps.Core.Templates`. |
| `AICompletionService` | The `gen_ai.*` spans and metrics of the AI clients (`CrestAppsTelemetry.AIClientSourceName`). |
| `OpenAI.*` | The OpenAI SDK's own `gen_ai.*` spans and metrics, which the Azure OpenAI chat client reports through. |
| `Microsoft.AspNetCore.SignalR.Server` | The SignalR hub invocations a chat turn starts from. |
| `Microsoft.AspNetCore.Http.Connections` | The open SignalR connections (`signalr.server.active_connections`) and how long they last. |
| `Experimental.ModelContextProtocol` | The MCP SDK's client and server operations: `mcp.client.operation.duration`, `mcp.server.operation.duration` and the session durations. |
| `A2A`, `A2A.AspNetCore` | The A2A SDK's client requests and the requests an A2A host serves. |
| `Npgsql` | PostgreSQL commands and connections, when the pgvector store is used. |
| `Polly` | Retries, timeouts and open circuits of the resilience pipelines. |

It adds no exporter and no ASP.NET Core or HttpClient instrumentation, so it can be combined with whatever the host
already configures without collecting anything twice. A host that configures OpenTelemetry by hand can register the
same names with `AddSource` and `AddMeter` instead.

### The sample hosts and Aspire

The MVC and Blazor sample hosts call `AddSharedSampleOpenTelemetry()`, which wires the pipeline above when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set and does nothing otherwise. The Aspire AppHost sets it, with
`OTEL_SERVICE_NAME`, for every project it runs, so running the AppHost sends everything to the Aspire dashboard. To
use the dashboard without the AppHost, start it on its own and point a host at it:

```bash
docker run --rm -it -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Then set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` and `OTEL_SERVICE_NAME=MvcWeb` for the host, and open
`http://localhost:18888`. The sample hosts' NLog files also print the trace id of every line, so a log line can be
matched to its trace.

## What the AI options control

The framework's own `CrestApps.Core` spans and metrics are always emitted. Two AI options, set in configuration or in
the sample hosts' **Settings** page, control what the AI clients add:

| Option | Default | What it does |
| --- | --- | --- |
| `EnableOpenTelemetry` | Off | Adds the OpenTelemetry generative AI (`gen_ai.*`) spans and metrics to the chat, embedding, image, speech and realtime clients: one span per provider round trip, an `execute_tool` span per tool call, and the `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` metrics. The Azure OpenAI chat client calls the OpenAI SDK directly rather than through Microsoft.Extensions.AI; its `gen_ai.*` telemetry comes from the SDK itself once the host calls `AppContext.SetSwitch("OpenAI.Experimental.EnableOpenTelemetry", true)`, as the sample hosts do. |
| `EnableSensitiveData` | Off | Adds prompts, responses and tool arguments to those `gen_ai.*` spans. They can contain personal data. Only applies while `EnableOpenTelemetry` is on. |

The framework's own spans and metrics never carry prompt or response text, whatever these options say.

## Traces

One chat turn is one trace. The spans the framework adds, with the `gen_ai.*` spans in between when
`EnableOpenTelemetry` is on:

```text
crestapps.chat.turn                     the chat hub received a message
├── crestapps.orchestrate.context       the orchestration context is built
│   └── crestapps.rag.preemptive        one per preemptive retrieval handler
│       └── crestapps.index.search      one per index query
├── crestapps.guardrail.check           when the profile has guardrails
├── crestapps.orchestrate               the orchestrator produces the reply
│   ├── crestapps.orchestrate.plan      when the default orchestrator plans its tools
│   ├── chat {model}                    gen_ai: one per provider round trip
│   └── execute_tool {tool}             gen_ai: one per tool call
│       └── crestapps.tool.invoke       every tool, with tool.name and tool.source
│           └── crestapps.retrieval.search
└── crestapps.store.commit              the turn's writes are committed
```

A sub-agent's reply is its own `crestapps.orchestrate` span under the tool call that ran it, with its depth in
`crestapps.agent.depth`. A tool proxied to an MCP server carries the connection in `crestapps.mcp.connection`. Other
entry points have their own root span:

| Span | What it covers |
| --- | --- |
| `crestapps.realtime.session` | A whole voice conversation, with `crestapps.realtime.grounding` per grounded turn and `crestapps.realtime.turn` per reply of a cascaded session. |
| `crestapps.ingest.run` | A file source or crawler run. |
| `crestapps.ingest.process` | One uploaded document, with `crestapps.ingest.read` for the reader that read it. |
| `crestapps.mcp.connect` | Connecting to an MCP server and its handshake. |
| `crestapps.webcrawler.crawl` | Re-indexing one web crawler. |
| `crestapps.background.cycle` | One pass of a periodic service. |
| `crestapps.tool.invoke` | A tool an MCP or WebMCP client called, with `tool.source` set to `mcp_server` or `webmcp`. |

Work handed to a background queue, such as indexing a data source, a memory or an uploaded chat document, runs in a
`crestapps.background.item` span that starts a new trace with a link back to the request that queued it, rather than
staying a child of a request that ended long ago.

Ids that help find a trace, such as `crestapps.session.id`, `crestapps.profile.id` and `crestapps.data_source.id`,
are set on spans only.

## Metrics

The instruments are on the `CrestApps.Core` meter, defined in `CrestAppsTelemetry` and `AITelemetry`, and their names
are a public contract. Durations are in seconds, with bucket boundaries advised for seconds so a collector does not
fall back to millisecond buckets. Every `outcome` tag is one of `ok`, `error`, `cancelled` or `blocked`, plus
`partial` and `empty` where noted. A count of failures is the count of a duration whose `outcome` is `error`.

### Orchestration and completions

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.ai.orchestration.duration` | Histogram (s) | `orchestrator`, `outcome` | A turn, from the user's message to the last streamed update, for every orchestrator. |
| `crestapps.core.ai.orchestration.time_to_first_token` | Histogram (s) | `orchestrator` | From the start of a turn to its first streamed text. |
| `crestapps.core.ai.completion.time_to_first_token` | Histogram (s) | `client` | From the start of a streamed completion to its first text, per provider, including the Azure OpenAI client's own paths. |
| `crestapps.core.ai.completion.errors` | Counter | `client`, `error.type` | Completions that failed, including the ones logged and turned into an empty answer, and the Claude and Copilot orchestrators' failures. |
| `crestapps.core.ai.planning.duration` | Histogram (s) | `outcome` | The planning call; `error` means the turn fell back to every tool. |
| `crestapps.core.ai.context_build.duration` | Histogram (s) | `phase` | Building the context of a turn: `orchestration` or `completion`. |
| `crestapps.core.ai.tools.scoped_count` | Histogram | `tools.mode` | The tools handed to the model after scoping: `all`, `planned` or `relevance`. |
| `crestapps.core.ai.handler.errors` | Counter | `handler` | Handler failures that were logged and skipped. |

### Tools

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.ai.tool.duration` | Histogram (s) | `tool.source`, `outcome` | One tool invocation. `tool.source` is `local`, `system`, `mcp`, `a2a`, `agent`, `mcp_server`, `webmcp` or `unknown`. A tool that hands its failure back to the model as a result, such as an MCP or A2A proxy, still counts as `error`. |
| `crestapps.core.ai.tool.denied` | Counter | `reason` | Tools left out because the user may not use them. |

### Resilience, rate limits and caches

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.ai.resilience.retries` | Counter | `operation`, `client`, `reason`, `attempt` | Requests retried after a rate limit or a transient failure, by client kind (`Chat`, `Embedding`, `Image`, `SpeechToText`, `TextToSpeech`) and by the Azure OpenAI client's own retry policy. `attempt` stops at 5. |
| `crestapps.core.ai.resilience.stream_failures` | Counter | `operation` | Streams that failed after their first update, which are not retried. |
| `crestapps.core.ai.ratelimit.rejections` | Counter | `limiter` | Chat requests the `chat` or `session_start` rate limiter refused. |
| `crestapps.core.ai.ratelimit.tracked_keys` | Gauge | `limiter` | The callers a rate limiter is tracking. |
| `crestapps.core.ai.client_cache.size` | Gauge | `cache` | The AI clients a cache is holding. |

### Prompt security and guardrails

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.ai.security.prompt_checks` | Counter | `decision`, `direction`, `rule_category` | Prompt security checks: `allowed`, `flagged` or `blocked`, on the `input` or the `output`. `rule_category` is the rule that flagged or blocked the text, such as `max-length` or `guardrail`, without the topic a guardrail model named. |
| `crestapps.core.ai.security.evaluation.duration` | Histogram (s) | `direction` | One prompt security check. |
| `crestapps.core.ai.guardrail.duration` | Histogram (s) | `direction`, `outcome` | One guardrail evaluation; `error` means the text was let through unchecked. |

### Retrieval and memory

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.ai.retrieval.duration` | Histogram (s) | `mode`, `outcome` | A data source search; `outcome` adds `partial` and `empty`. |
| `crestapps.core.ai.retrieval.results` | Histogram | `mode` | The results a search handed to the model. |
| `crestapps.core.ai.retrieval.fallbacks` | Counter | `reason` | Searches that fell back: `content_type_filter` or `chunk`. |
| `crestapps.core.ai.rag.preemptive.duration` | Histogram (s) | `handler`, `outcome` | One preemptive retrieval handler, which searches before the model is called. |
| `crestapps.core.ai.rag.query_extraction.duration` | Histogram (s) | `outcome` | Asking the utility model for a turn's search queries; a failure falls back to the message as typed. |
| `crestapps.core.ai.embedding.fallbacks` | Counter | | Document chunks stored without an embedding because generating it failed. |
| `crestapps.core.ai.memory.search.duration` | Histogram (s) | `mode` | A user memory search: `index`, or `store` when it fell back to the memory store. |
| `crestapps.core.ai.memory.saves` | Counter | `result` | Memories the model asked to save: `created`, `updated` or `rejected` by the safety checks. |

### Documents and ingestion

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.ai.ingestion.duration` | Histogram (s) | `outcome` | Reading, chunking and embedding one uploaded document. Its count is the number of documents. |
| `crestapps.core.ai.ingestion.chunks` | Histogram | | The chunks one document produced. |
| `crestapps.core.ai.ingestion.reader.duration` | Histogram (s) | `reader`, `outcome` | A document reader turning a file into text and structure. |
| `crestapps.core.ai.ingestion.reader.fallbacks` | Counter | `reader`, `operation` | Reads that fell back to a simpler way: the preferred reader failing (`read`), or a PDF step such as `layout`, `segmentation`, `tables`, `vector_figures` or `charts` that was skipped. |
| `crestapps.core.ai.ingestion.figures` | Counter | `result` | Figures a run left `pending` a description, and what the backfill did with them: `described`, `failed` or `retry`. The backlog is the pending count less the described and failed ones. |
| `crestapps.core.ai.ingestion.run.duration` | Histogram (s) | `outcome` | One file source or crawler ingestion run; `outcome` adds `partial`. |
| `crestapps.core.ai.ingestion.run.items` | Counter | `result` | Items a run `indexed`, `skipped`, `deleted`, `failed`, or `discarded` when the run stopped part-way. |
| `crestapps.core.ai.ingestion.connector.duration` | Histogram (s) | `connector`, `operation`, `outcome` | An ingestion connector listing a folder (`list`) or opening a file (`read`), over FTP, SFTP or the file system. |
| `crestapps.core.ai.ingestion.connector.bytes` | Counter (By) | `connector` | The bytes the connectors read. |
| `crestapps.core.ai.documents.uploads.rejected` | Counter | `reason` | Uploaded chat documents refused before they were read: `size`, `scan`, `type` or `disabled`. |
| `crestapps.core.ai.documents.tabular.prepare.duration` | Histogram (s) | `outcome` | Loading a conversation's spreadsheets into the workspace the model queries. |

### Web crawlers

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.ai.webcrawler.crawl.duration` | Histogram (s) | `outcome` | Re-indexing one web crawler. |
| `crestapps.core.ai.webcrawler.pages` | Counter | `result` | Pages a crawler fetched: `fetched`, `skipped` (not HTML, or nothing to index) or `failed`. |
| `crestapps.core.ai.webcrawler.sitemap.errors` | Counter | `reason` | Sitemaps (`sitemap`) and robots.txt files (`robots`) that could not be read during discovery. |

### Chat

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.chat.turn.duration` | Histogram (s) | `outcome` | One chat turn on the hub, from the message to the end of the reply. |
| `crestapps.core.chat.messages` | Counter | `response_handler`, `outcome` | Messages handed to a response handler. A message stopped before it reaches one, such as a blocked prompt, shows on the turn duration only. |
| `crestapps.core.chat.response.duration` | Histogram (s) | `response_handler` | How long a reply took to stream: the latency stored on each assistant message. |
| `crestapps.core.chat.sessions.closed` | Counter | `reason` | Sessions the close cycle ended: `closed` or `abandoned`. |
| `crestapps.core.chat.post_processing.duration` | Histogram (s) | `operation`, `outcome` | The AI work after a message (`data_extraction`) or after a session closed (`post_close`). Both swallow their failures; a post-close attempt that leaves work for a retry counts as `error`. |

### Realtime voice

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.realtime.session.duration` | Histogram (s) | `end_reason` | A realtime voice session. |
| `crestapps.core.realtime.sessions.active` | UpDownCounter | | The voice sessions in progress. |
| `crestapps.core.realtime.grounding.duration` | Histogram (s) | | Grounding a spoken turn in documents, data sources and memory. |
| `crestapps.core.realtime.first_audio_latency` | Histogram (s) | | From the provider committing the user's utterance to the first audio of the reply. |
| `crestapps.core.realtime.interruptions` | Counter | | Times a user talked over the assistant's spoken reply. |
| `crestapps.core.realtime.provider.errors` | Counter | `reason` | Errors the provider reported: `error`, `response` or `transcription`. The benign turn-taking races are left out. |
| `crestapps.core.realtime.acknowledgements` | Counter | `result` | Spoken acknowledgements that covered a slow knowledge search: `spoken` or `failed`. |
| `crestapps.core.realtime.watchdog.fires` | Counter | | Committed turns that produced no transcript in time, so an answer was asked for anyway. |
| `crestapps.core.realtime.cascaded.leg.latency` | Histogram (s) | `leg` | How long a leg of a cascaded session took to start producing: `stt`, `llm` or `tts`. |
| `crestapps.core.realtime.speech.stalls` | Counter | `provider` | Turns a speech-to-text provider never finished, which the session ended itself. |
| `crestapps.core.realtime.speech.disconnects` | Counter | `provider` | Speech provider streams that ended while their session was in use. |
| `crestapps.core.realtime.webrtc.peers.active` | UpDownCounter | | The WebRTC peers open. |
| `crestapps.core.realtime.webrtc.connection_states` | Counter | `state` | WebRTC peer connection state changes, such as `connected` or `failed`. |
| `crestapps.core.realtime.webrtc.codec.errors` | Counter | `operation` | Audio frames the Opus codec could not `encode` or `decode`. |
| `crestapps.core.realtime.webrtc.stall_frames` | Counter | `reason` | Silent frames inside a reply, because the `provider` or the `pacing` thread fell behind. |

### MCP and A2A

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.mcp.client.connect.duration` | Histogram (s) | `transport`, `outcome` | Connecting to an MCP server and completing its handshake. |
| `crestapps.core.mcp.clients.active` | UpDownCounter | `transport` | The MCP client sessions open. A count that only grows means clients are not being released. |
| `crestapps.core.mcp.tool_resolution.duration` | Histogram (s) | `operation`, `outcome` | Working out a request's MCP tools: listing them (`registry`) or matching them to the prompt (`capabilities`). |
| `crestapps.core.a2a.client.duration` | Histogram (s) | `outcome` | Sending a message to a remote A2A agent and waiting for its reply. |
| `crestapps.core.a2a.agent_card.fetch.duration` | Histogram (s) | `outcome` | Fetching a remote agent's card. |
| `crestapps.core.a2a.agent_card.lookups` | Counter | `cache` | Agent card lookups: `hit` or `miss`. |

The requests an MCP server or an A2A host serves are measured by their SDKs: `mcp.server.operation.duration` and the
`A2A.AspNetCore` spans.

### Usage

When usage tracking is on (`AddCoreAIUsageTracking()`), every metered request is also emitted as metrics, for every
client kind: chat, embeddings, images, speech and realtime.

| Instrument | Type | Tags |
| --- | --- | --- |
| `crestapps.core.ai.usage.requests` | Counter | `operation`, `client`, `deployment`, `purpose`, `streaming` |
| `crestapps.core.ai.usage.tokens` | Counter | the same, plus `token.type`: `input`, `output`, `cached`, `reasoning` or `audio` |
| `crestapps.core.ai.usage.duration` | Histogram (s) | the same as requests |

The `gen_ai.client.token.usage` metric counts the same chat tokens when `EnableOpenTelemetry` is on. Chart one or the
other: the usage metrics cover every client kind and carry the purpose, the `gen_ai` metric follows the OpenTelemetry
conventions other tools understand.

### Framework

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.index.operation.duration` | Histogram (s) | `provider`, `operation`, `outcome` | A search index `search`, `write` or `delete`; a failed write that the provider logged and returned `false` for counts as `error`. |
| `crestapps.core.index.documents` | Counter | `provider`, `operation` | The documents written to or deleted from an index, when the operation succeeded. |
| `crestapps.core.store.commit.duration` | Histogram (s) | `store`, `outcome` | Committing a unit of work to the `YesSql` or `EntityCore` store. |
| `crestapps.core.queue.depth` | UpDownCounter | `queue` | Items waiting in an in-memory queue. |
| `crestapps.core.queue.wait.duration` | Histogram (s) | `queue` | How long an item waited before a worker picked it up. |
| `crestapps.core.background.item.duration` | Histogram (s) | `service`, `work_item_type`, `outcome` | One queued work item. |
| `crestapps.core.background.cycle.duration` | Histogram (s) | `service`, `outcome` | One pass of a periodic service. |
| `crestapps.core.background.last_success` | Gauge (s) | `service` | The Unix time a periodic service last finished a pass without an error. Alert when it falls too far behind the clock. |
| `crestapps.core.background.task.faults` | Counter | `service` | Fire-and-forget tasks that failed, which nothing else would report. |
| `crestapps.core.templates.render.duration` | Histogram (s) | `outcome` | Rendering a template, on the `CrestApps.Core.Templates` meter. |

A host or a custom package can record into the same instruments, so its own orchestrator or store shows up on the
same charts.

## What may go on a tag

A metric tag multiplies the number of series a collector stores, so the framework only tags with bounded values: the
provider or client, the operation, the purpose, the outcome, the direction, the orchestrator, and other small sets.
It never tags a metric with:

- session, user, interaction, connection, profile or item ids;
- URLs, hosts, file names, or index names;
- MCP or A2A tool names, which a remote server decides;
- prompt text, responses, or the fragment of a prompt a security rule matched.

Ids that help find a trace go on the span instead. Custom code that records into these instruments should follow the
same rule.

## Logs

The failures that the pipeline logs and then swallows, so a request can go on, have their own event ids, so they can
be counted and alerted on without matching text. Each also has a counter in the tables above.

| Range | Area | Examples |
| --- | --- | --- |
| 2000s | AI pipeline | 2001 completion failed, 2002 planning failed, 2003 handler skipped, 2004 guardrail check failed, 2101 and 2102 security audit events |
| 3000s | Chat | 3001 to 3006 a message's persistence, from the session it resolved to the commit |
| 4000s | Ingestion | 4001 a document could not be read, 4002 embeddings failed, 4101 to 4106 a PDF step skipped, 4201 to 4205 a figure description failed, 4206 a discarded fetch, 4207 a run finished |
| 5000s | Retrieval | 5001 hierarchical retrieval fell back to chunks |
| 7000s | MCP and A2A | 7001 to 7009 `A2ALog`, 7101 to 7105 a tool call proxied to an MCP server |
| 9000s | Sample hosts | the Blazor and MVC samples' own messages |

A chat turn's logs carry `SessionId` and `ProfileId`, and a chat interaction's `InteractionId`, as scope properties.
A realtime session's logs carry its `SessionId`, and a queued work item's carry its `WorkItemType` and the id of what
it works on. With `IncludeScopes` on, the OpenTelemetry log exporter sends them with every line, next to the trace id.
The logs that report a finished sync, crawl, run or close cycle include how long it took.

## Related

- [AI Usage Metering](./ai-usage-metering.md): the stored usage records the usage metrics are emitted from.
- [AI Resilience](./ai-resilience.md): the retries behind `crestapps.core.ai.resilience.retries`.
- [Realtime Voice](./realtime-voice.md): the voice sessions behind the `crestapps.core.realtime.*` metrics.
- [MVC Example](./mvc-example.md): the sample host that sends all of this to the Aspire dashboard.
