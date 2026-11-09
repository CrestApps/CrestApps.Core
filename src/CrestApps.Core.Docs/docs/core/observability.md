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
application that does not collect telemetry pays nothing for it.

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
| `AICompletionService` | The `gen_ai.*` spans and metrics of the AI clients (`CrestAppsTelemetry.AIClientSourceName`). |
| `Microsoft.AspNetCore.SignalR.Server` | The SignalR hub invocations a chat turn starts from. |
| `Experimental.ModelContextProtocol` | The MCP SDK's client and server operations, such as `mcp.client.operation.duration`. |
| `A2A`, `A2A.AspNetCore` | The A2A SDK's client and server requests, such as `a2a.client.request.duration`. |
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
| `EnableOpenTelemetry` | Off | Adds the OpenTelemetry generative AI (`gen_ai.*`) spans and metrics to the chat, embedding, image, speech and realtime clients: one span per provider round trip, an `execute_tool` span per tool call, and the `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` metrics. The Azure OpenAI chat client calls the Azure SDK directly rather than through Microsoft.Extensions.AI, so it does not emit them yet; its turns, tool calls and usage are still covered by the `crestapps.*` telemetry. |
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
└── crestapps.orchestrate               the orchestrator produces the reply
    ├── crestapps.orchestrate.plan      when the default orchestrator plans its tools
    ├── chat {model}                    gen_ai: one per provider round trip
    └── execute_tool {tool}             gen_ai: one per tool call
        └── crestapps.tool.invoke       every tool, with tool.name and tool.source
            └── crestapps.retrieval.search
```

A sub-agent's reply is its own `crestapps.orchestrate` span under the tool call that ran it. Other entry points have
their own root span: `crestapps.realtime.session` covers a whole voice conversation, `crestapps.ingest.run` a file
source or crawler run, `crestapps.ingest.process` one uploaded document, `crestapps.mcp.connect` each MCP connection
and handshake, and `crestapps.background.cycle` one pass of a periodic service.

Work handed to a background queue, such as indexing a data source or a memory, runs in a
`crestapps.background.item` span that starts a new trace with a link back to the request that queued it, rather than
staying a child of a request that ended long ago.

Ids that help find a trace, such as `crestapps.session.id`, `crestapps.profile.id` and `crestapps.data_source.id`,
are set on spans only.

## Metrics

All instruments are on the `CrestApps.Core` meter. They are defined in `CrestAppsTelemetry` and `AITelemetry`, and
their names are a public contract. Durations are in seconds, with bucket boundaries advised for seconds so a
collector does not fall back to millisecond buckets. Every `outcome` tag is one of `ok`, `error`, `cancelled` or
`blocked`, plus `partial` and `empty` where noted.

### AI pipeline

| Instrument | Type | Tags | What it measures |
| --- | --- | --- | --- |
| `crestapps.core.ai.orchestration.duration` | Histogram (s) | `orchestrator`, `outcome` | A turn, from the user's message to the last streamed update, for every orchestrator. |
| `crestapps.core.ai.orchestration.time_to_first_token` | Histogram (s) | `orchestrator` | From the start of a turn to its first streamed text. |
| `crestapps.core.ai.completion.time_to_first_token` | Histogram (s) | `client` | From the start of a streamed completion to its first text, per provider. |
| `crestapps.core.ai.completion.errors` | Counter | `client`, `error.type` | Completions that failed, including the ones logged and turned into an empty answer. |
| `crestapps.core.ai.planning.duration` | Histogram (s) | `outcome` | The planning call; `error` means the turn fell back to every tool. |
| `crestapps.core.ai.tools.scoped_count` | Histogram | | The tools handed to the model after scoping. |
| `crestapps.core.ai.tool.duration` | Histogram (s) | `tool.source`, `outcome` | One tool invocation. `tool.source` is `local`, `system`, `mcp`, `a2a`, `agent` or `unknown`. |
| `crestapps.core.ai.resilience.retries` | Counter | `operation`, `reason`, `attempt` | Requests retried after the provider rate limited them, by client kind (`Chat`, `Embedding`, `Image`, `SpeechToText`, `TextToSpeech`). `attempt` stops at 5. |
| `crestapps.core.ai.handler.errors` | Counter | `handler` | Handler failures that were logged and skipped. |
| `crestapps.core.ai.security.prompt_checks` | Counter | `decision`, `direction` | Prompt security checks: `allowed`, `flagged` or `blocked`, on the `input` or the `output`. |
| `crestapps.core.ai.security.evaluation.duration` | Histogram (s) | `direction` | One prompt security check. |
| `crestapps.core.ai.guardrail.duration` | Histogram (s) | `direction`, `outcome` | One guardrail evaluation; `error` means the text was let through unchecked. |
| `crestapps.core.ai.ratelimit.rejections` | Counter | `limiter` | Chat requests the `chat` or `session_start` rate limiter refused. |
| `crestapps.core.ai.retrieval.duration` | Histogram (s) | `mode`, `outcome` | A data source search; `outcome` adds `partial` and `empty`. |
| `crestapps.core.ai.retrieval.results` | Histogram | `mode` | The results a search handed to the model. |
| `crestapps.core.ai.retrieval.fallbacks` | Counter | `reason` | Searches that fell back: `content_type_filter` or `chunk`. |
| `crestapps.core.ai.embedding.fallbacks` | Counter | | Document chunks stored without an embedding because generating it failed. |
| `crestapps.core.ai.ingestion.duration` | Histogram (s) | `outcome` | Reading, chunking and embedding one uploaded document. |
| `crestapps.core.ai.ingestion.chunks` | Histogram | | The chunks one document produced. |
| `crestapps.core.ai.ingestion.run.duration` | Histogram (s) | `outcome` | One file source or crawler ingestion run; `outcome` adds `partial`. |
| `crestapps.core.ai.ingestion.run.items` | Counter | `result` | Items a run `indexed`, `skipped`, `deleted` or `failed`. |
| `crestapps.core.ai.webcrawler.crawl.duration` | Histogram (s) | `outcome` | Re-indexing one web crawler. |
| `crestapps.core.chat.turn.duration` | Histogram (s) | `outcome` | One chat turn on the hub, from the message to the end of the reply. |
| `crestapps.core.mcp.client.connect.duration` | Histogram (s) | `transport`, `outcome` | Connecting to an MCP server and completing its handshake. |
| `crestapps.core.realtime.session.duration` | Histogram (s) | `end_reason` | A realtime voice session. |
| `crestapps.core.realtime.sessions.active` | UpDownCounter | | The voice sessions in progress. |
| `crestapps.core.realtime.grounding.duration` | Histogram (s) | | Grounding a spoken turn in documents, data sources and memory. |
| `crestapps.core.realtime.first_audio_latency` | Histogram (s) | | From the provider committing the user's utterance to the first audio of the reply. |
| `crestapps.core.realtime.interruptions` | Counter | | Times a user talked over the assistant's spoken reply. |

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
| `crestapps.core.index.operation.duration` | Histogram (s) | `provider`, `operation`, `outcome` | A search index `search` or `write`; a failed write that the provider logged and returned `false` for counts as `error`. |
| `crestapps.core.store.commit.duration` | Histogram (s) | `store`, `outcome` | Committing a unit of work to the `YesSql` or `EntityCore` store. |
| `crestapps.core.queue.depth` | UpDownCounter | `queue` | Items waiting in an in-memory queue. |
| `crestapps.core.queue.wait.duration` | Histogram (s) | `queue` | How long an item waited before a worker picked it up. |
| `crestapps.core.background.item.duration` | Histogram (s) | `service`, `work_item_type`, `outcome` | One queued work item. |
| `crestapps.core.background.cycle.duration` | Histogram (s) | `service`, `outcome` | One pass of a periodic service; a service whose passes stop reporting `ok` needs attention. |

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
| 1000s | A2A | `A2ALog` |
| 2000s | AI pipeline | 2001 completion failed, 2002 planning failed, 2003 handler skipped, 2004 guardrail check failed, 2101 and 2102 security audit events |
| 4000s | Document processing | 4001 a document could not be read, 4002 embeddings failed |
| 5000s | Retrieval | 5001 hierarchical retrieval fell back to chunks |
| 9000s | Sample hosts | the Blazor and MVC samples' own messages |

A chat turn's logs carry `SessionId` and `ProfileId`, and a chat interaction's `InteractionId`, as scope properties.
With `IncludeScopes` on, the OpenTelemetry log exporter sends them with every line, next to the trace id.

## Related

- [AI Usage Metering](./ai-usage-metering.md): the stored usage records the usage metrics are emitted from.
- [Realtime Voice](./realtime-voice.md): the voice sessions behind the `crestapps.core.realtime.*` metrics.
