---
sidebar_label: AI Usage Metering
sidebar_position: 17
title: AI Usage Metering
description: Meter every AI request made through the AI client factory, whatever the provider, and report usage per model, per category, and per purpose.
---

# AI Usage Metering

> Know what every request to every model used, so usage can be priced, reported per feature, and split between the parts of an application that caused it.

Every client returned by `IAIClientFactory` meters its own requests. That covers chat clients, embedding
generators, image generators, speech-to-text and text-to-speech clients, and realtime clients. Metering does
not depend on the provider: it wraps the client the provider returned and reads the provider-agnostic
`UsageDetails` that Microsoft.Extensions.AI responses carry. A custom `IAIClientProvider` is metered as soon as
its clients are created through the factory.

Metering is on when `GeneralAIOptions.EnableAIUsageTracking` is `true`. Each metered request becomes one
`AICompletionUsageRecord`, handed to every registered `IAICompletionUsageObserver`. The default
`IAICompletionUsageService` is one of them, and stores the record through `IAICompletionUsageStore`.

## One record per request

A record is written for each request sent to the provider, not for each user turn. One user message can cause
several:

- a planning call and a search-query rewrite on the utility deployment
- one call per step of a tool-calling loop, including the steps that only asked for tool calls
- vision calls that describe an image or a document figure
- one embedding call per batch while indexing or retrieving
- one record per completed response, and one per completed transcription, in a realtime session

Each of these is metered where it reaches the provider, below any middleware the caller adds. A tool-calling
loop added with `UseFunctionInvocation()` therefore produces one record per round trip, and a response served
from a cache produces none, because nothing was billed.

## What is recorded

Each kind of request is billed in its own units, so a record keeps all of them. `OperationType` says which
kind it was, using the values in `AIUsageOperationTypes`.

| Property | Meaning |
| --- | --- |
| `OperationType` | `Chat`, `Embedding`, `Image`, `SpeechToText`, `TextToSpeech`, `Realtime`, or `RealtimeTranscription` |
| `ClientName`, `ConnectionName`, `DeploymentName`, `ModelName` | Where the request went. `ModelName` is the model the provider reported, or the deployment when it reported none |
| `InputTokenCount`, `OutputTokenCount`, `TotalTokenCount` | Tokens in and out |
| `CachedInputTokenCount` | Input tokens served from the provider's prompt cache, usually billed at a discount |
| `ReasoningTokenCount` | Output tokens a reasoning model spent before answering |
| `InputAudioTokenCount`, `OutputAudioTokenCount` | Audio tokens, billed at a different rate from text tokens |
| `AudioDurationMs` | Audio transcribed, for models billed by duration |
| `CharacterCount` | Characters synthesized, for models billed by character |
| `ImageCount` | Images generated, for models billed per image |
| `ContextType` | The category of feature that made the request |
| `Purpose` | Why the request was made within that category |

A unit a provider does not report stays at zero. When a provider measures a non-token unit better than the
metering client can, it reports it in `UsageDetails.AdditionalCounts` under a key from
`AIUsageAdditionalCounts`, and that value is used instead.

:::note[How audio and characters are measured]
Speech-to-text duration is taken from where the last recognized speech ended, measured from the start of the
audio (`SpeechToTextResponse.EndTime`). Trailing silence after the last words is not counted, so a provider that
bills every second it receives will bill slightly more. Text-to-speech characters are the length of the text
sent to be spoken.
:::

## Categories and purposes

Two labels say what a request was for, so usage can be reported per model and per feature.

- **`ContextType`** is the category of feature. Requests made for a chat session are `AIChatSession`, and those
  made for a chat interaction are `ChatInteraction`. Any other feature can name its own category.
- **`Purpose`** is why the request was made within that category. The framework labels its own requests with
  the values in `AIUsagePurposes`:

| Purpose | Requests |
| --- | --- |
| `Conversation` | The assistant's reply |
| `Planning` | Planning which tools a request needs |
| `SearchQueryGeneration` | Rewriting a message into a search query |
| `Retrieval` | Embedding a query to search documents, data sources, memory, or MCP capabilities |
| `Indexing` | Embedding content for a search index |
| `DataExtraction` | Extracting structured data from a conversation |
| `PostSessionProcessing` | Tasks that run after a conversation closes |
| `ImageAnalysis` | Describing an image or a document figure |
| `DocumentAnalysis` | A document tool reading a document: the PDF agent's summaries, answers, extraction and classification |
| `ImageGeneration`, `ChartGeneration` | Generating an image or a chart |
| `MetadataExtraction` | Extracting metadata from an ingested publication |
| `Transcription`, `SpeechSynthesis` | Speech-to-text and text-to-speech in chat |
| `VoiceConversation` | A realtime voice session |

### Labeling your own requests

**When your code creates the client**, add `UseUsageLabels` to its pipeline, the same way as
`UseDefaultResilience`. Every request the client makes is labeled:

```csharp
var client = await clientFactory.CreateChatClientAsync(deployment, builder => builder
    .UseDefaultResilience()
    .UseUsageLabels(contextType: "Leads", purpose: "LeadScoring"));
```

`UseUsageLabels` is available on the chat, embedding, image, speech-to-text, text-to-speech and realtime
builders. `CreateRealtimeClientAsync` takes no pipeline, so build on the client it returns:

```csharp
var realtimeClient = (await clientFactory.CreateRealtimeClientAsync(deployment))
    .AsBuilder()
    .UseUsageLabels(contextType: "Support", purpose: AIUsagePurposes.VoiceConversation)
    .Build();
```

`UseUsageLabels` only names the requests. Metering happens whether or not it is used, below every middleware,
so a tool-calling loop is still recorded once per round trip wherever `UseUsageLabels` sits in the pipeline.
The labels travel on each request and are removed before it reaches the provider. They hold for streamed
responses read from an `async` iterator, where a scope begun inside the iterator would not survive.

**When your code calls a service that creates the client for you**, such as `IAICompletionService` or an
orchestrator, wrap the call in an `AIUsageScope`. Every metered request inside it, of any kind, is labeled:

```csharp
using var usageScope = AIUsageScope.Begin(contextType: "Leads", purpose: "LeadScoring");

var response = await completionService.CompleteAsync(deployment, messages, context);
```

Scopes nest, and a label left `null` is inherited from the enclosing scope. A realtime session takes its labels
from the scope in effect when the session is created. A single request can also carry its own labels in its
options' `AdditionalProperties`, or in an `AICompletionContext`'s `AdditionalProperties`, under
`AICompletionContextKeys.UsageContextType` and `AICompletionContextKeys.UsagePurpose`.

The labels are resolved in this order:

1. a value set on the request
2. the client's `UseUsageLabels`
3. the current `AIUsageScope`
4. a default purpose set on the request under `AICompletionContextKeys.DefaultUsagePurpose`, which the framework
   uses for general requests such as a conversation reply so that it never overrides a purpose you chose
5. the category of the chat session or interaction the request belongs to

### Clients created outside the factory

A client your code builds itself, without `IAIClientFactory`, is not metered until you wrap it. Wrap the
provider's client before adding any middleware:

```csharp
var chatClient = AIUsageMetering.Meter(providerClient, "MyProvider", connectionName: null, deploymentName: "model-id", serviceProvider);
```

:::note[Scopes and async iterators]
`AIUsageScope` flows with the async execution context. Begin it in the method that makes the request, or in one
that awaits it. A scope begun inside an `async` iterator does not reliably survive across `yield return`, so a
streaming method should label the client with `UseUsageLabels`, or put the labels on the request, instead.
:::

## Reports

`AIUsageReport.Build` turns records into rows, with every billable unit summed. Every grouping keeps the
provider, the model, and the kind of request apart, because each combination is priced on its own:

```csharp
var records = await usageService.GetAsync(startUtc, endUtc);

var perModel = AIUsageReport.Build(records, AIUsageReportGrouping.Model);
var perModelAndPurpose = AIUsageReport.Build(records, AIUsageReportGrouping.ModelAndPurpose);
var perModelAndCategory = AIUsageReport.Build(records, AIUsageReportGrouping.ModelAndCategory);
```

Each `AIUsageReportRow` reports `RequestCount` alongside the token, audio, character and image totals.

### Turning usage into cost

Providers bill each unit on its own meter: input, cached input, output, and audio tokens per model, audio
duration, characters, images. Multiplying a row by list prices gives an estimate. To match what was actually
paid, including discounts and credits, take the cost of each meter from the provider's bill for a period and
divide it in proportion to the units each group used on that meter in the same period. A single blended price
per model misprices groups whose mix of input and output tokens differs from the average.

## Providers

Metering reads the usage each provider client reports. These provider-specific behaviors fill in what a
provider would otherwise drop:

- **Azure OpenAI chat** through `AzureOpenAICompletionClient` records each round trip of its tool-calling loop
  separately, and keeps the cached, reasoning, and audio token counts the service returns.
- **Azure OpenAI realtime** reads the usage on `response.done` and on completed input transcriptions, including
  transcription models that bill by audio duration.
- **Azure AI Speech** reports where recognized speech starts and ends, which gives speech-to-text its duration.
