---
sidebar_label: Deployment Capabilities
sidebar_position: 16
title: AI Deployment Capabilities
description: Metadata-driven deployment features and parameters that describe what an AI deployment supports, which options its consumers may configure, and how a host should shape its chat UI around them.
---

# AI Deployment Capabilities

> A registry of **deployment features** and **deployment parameters** that lets deployments declare what
> their model supports, so editors render only the relevant options, the runtime only sends supported
> values, and the **chat UI adapts its input to the selected model**.

## Why this exists

Different models expose different knobs. A reasoning model accepts a reasoning effort level, a small
chat model does not, and a speech-to-speech model has no text box at all. Without metadata, every new
knob turns into hardcoded, provider-specific UI and provider-specific request-building code.

The capability system replaces that with two extensible concepts:

| Concept | Shape | Example |
| --- | --- | --- |
| **Feature** | A binary capability. The deployment either supports it or it does not. | `toolCalling`, `reasoning`, `realtime` |
| **Parameter** | A configurable option carrying metadata: kind, allowed values, range, and a default. | `reasoningEffort` |

Definitions are registered once at startup. An **AI Deployment** then declares which of those
definitions its model actually exposes, optionally narrowing the allowed values. AI Profiles, AI
Profile Templates, and Chat Interactions store the selected values. At request time the framework
binds the selected values into the outgoing request.

:::info
Features are **not** the same as `AIDeploymentPurpose`. `Purpose` (`Chat`, `Utility`, `Embedding`,
`Image`, …) drives *routing* — which deployment is picked for a given job. Features describe
*capabilities within* a deployment and deliberately avoid duplicating routing concerns. This is why
realtime (speech-to-speech) is a **feature on a `Chat` deployment**, not a separate purpose: a realtime
model is still a chat model — it just speaks instead of typing.
:::

## Quick Start

```csharp
builder.Services.AddCoreAIDeploymentCapabilities();
```

:::info
You rarely need to call this directly — `AddCoreAIServices()` chains it automatically.
:::

## Built-in definitions

### Features

| Name | Constant | Default on new deployments | Description |
| --- | --- | --- | --- |
| `textGeneration` | `AIDeploymentFeatureNames.TextGeneration` | ✅ | The model can hold a text conversation. Clear this **only** for a speech-to-speech-only model that cannot handle text. |
| `toolCalling` | `AIDeploymentFeatureNames.ToolCalling` | ✅ | The model can call tools and functions supplied with the request. |
| `structuredOutputs` | `AIDeploymentFeatureNames.StructuredOutputs` | | The model can return responses that follow a supplied JSON schema. |
| `streaming` | `AIDeploymentFeatureNames.Streaming` | ✅ | The model can stream response updates as they are produced. |
| `reasoning` | `AIDeploymentFeatureNames.Reasoning` | | The model performs internal reasoning before producing an answer. |
| `imageInput` | `AIDeploymentFeatureNames.ImageInput` | | The model can understand image inputs (vision). |
| `imageOutput` | `AIDeploymentFeatureNames.ImageOutput` | | The model can generate images. |
| `audioInput` | `AIDeploymentFeatureNames.AudioInput` | | The model accepts audio input. |
| `audioOutput` | `AIDeploymentFeatureNames.AudioOutput` | | The model produces audio output. |
| `videoInput` | `AIDeploymentFeatureNames.VideoInput` | | The model can understand video inputs. |
| `videoOutput` | `AIDeploymentFeatureNames.VideoOutput` | | The model can generate video. |
| `realtime` | `AIDeploymentFeatureNames.Realtime` | | The model supports real-time, bidirectional **speech-to-speech** sessions. |

These represent **trained capabilities** the underlying model was built with. Provider-hosted tools
(such as a web-search tool the provider runs on your behalf, or a computer-use tool) are *not* modeled
as features because almost any tool-calling model can be handed such a tool — they are ordinary tools,
not a trained trait. Set `AIDeploymentFeatureDescriptor.EnabledByDefault` when registering a feature to
pre-select it on newly created deployments; existing deployments are never changed by this flag.

:::tip Opt-out vs opt-in features
`textGeneration` is **opt-out**: a deployment is assumed to support it unless it declares metadata that
omits it. Most models are text models, so this keeps every existing deployment working without change.
`realtime` is **opt-in**: only a deployment that explicitly lists it is treated as a realtime model. The
two together let a single flag flip a chat surface between text and voice — see
[Building a capability-aware chat UI](#building-a-capability-aware-chat-ui).
:::

### Parameters

| Name | Constant | Kind | Allowed values | Default |
| --- | --- | --- | --- | --- |
| `reasoningEffort` | `AIDeploymentParameterNames.ReasoningEffort` | `Choice` | `None` (shown as *Minimal*), `Low`, `Medium`, `High`, `ExtraHigh` | `Medium` |

`reasoningEffort` maps onto `Microsoft.Extensions.AI.ChatOptions.Reasoning.Effort`, so it is
provider-agnostic and ships in the core AI package rather than in a provider module. It also declares
`RequiredFeature = AIDeploymentFeatureNames.Reasoning`, which links the parameter to the `reasoning`
trained feature (see [Linking a parameter to a feature](#linking-a-parameter-to-a-feature)).

## Declaring what a deployment supports

Capability metadata is stored on the deployment through the
[extensible entity](./extensible-entity.md) `Properties` bag using `AIDeploymentMetadata`:

```csharp
deployment.Put(new AIDeploymentMetadata
{
    Features =
    [
        AIDeploymentFeatureNames.ToolCalling,
        AIDeploymentFeatureNames.Reasoning,
    ],
    Parameters = new Dictionary<string, AIDeploymentParameter>(StringComparer.OrdinalIgnoreCase)
    {
        [AIDeploymentParameterNames.ReasoningEffort] = new AIDeploymentParameter
        {
            AllowedValues = ["Low", "Medium", "High"],
            DefaultValue = "Medium",
        },
    },
});
```

A **speech-to-speech-only** model declares `realtime` and omits `textGeneration`, which is what tells
every chat surface to run this deployment as audio-only:

```csharp
deployment.Put(new AIDeploymentMetadata
{
    // No textGeneration: this model cannot handle a text turn.
    Features = [ AIDeploymentFeatureNames.Realtime ],
});
```

Because `AIDeploymentCatalogHandler` deep-merges `Properties`, the same metadata can be supplied from
configuration or a recipe with no extra code. Note the property key is the type name,
`AIDeploymentMetadata`:

```json
{
  "CrestApps": {
    "AI": {
      "Deployments": [
        {
          "Name": "gpt-5-chat",
          "ModelName": "gpt-5",
          "ConnectionName": "openai",
          "Properties": {
            "AIDeploymentMetadata": {
              "Features": [ "textGeneration", "toolCalling", "reasoning", "streaming" ],
              "Parameters": {
                "reasoningEffort": {
                  "AllowedValues": [ "Low", "Medium", "High" ],
                  "DefaultValue": "Medium"
                }
              }
            }
          }
        },
        {
          "Name": "gpt-realtime",
          "ModelName": "gpt-realtime",
          "ConnectionName": "openai",
          "Properties": {
            "AIDeploymentMetadata": {
              "Features": [ "realtime" ]
            }
          }
        }
      ]
    }
  }
}
```

A deployment-level parameter entry may narrow or override the registered definition:

| Property | Effect |
| --- | --- |
| `AllowedValues` | Restricts a `Choice` parameter to a subset of the registered options. |
| `DefaultValue` | Overrides the registered default. Ignored when the value is not valid for the parameter. |
| `Minimum`, `Maximum`, `Step` | Overrides the numeric bounds for `Number` and `Integer` parameters. |

## Resolving capabilities

`IAIDeploymentCapabilityService` merges the registered definitions with the deployment metadata and
returns only what the deployment exposes:

```csharp
public sealed class MyService
{
    private readonly IAIDeploymentCapabilityService _capabilityService;

    public MyService(IAIDeploymentCapabilityService capabilityService)
    {
        _capabilityService = capabilityService;
    }

    public async Task<bool> SupportsReasoningAsync(string deploymentName)
    {
        var capabilities = await _capabilityService.GetCapabilitiesAsync(deploymentName);

        return capabilities.SupportsFeature(AIDeploymentFeatureNames.Reasoning);
    }
}
```

| Member | Description |
| --- | --- |
| `GetRegisteredFeatures()` | Every registered feature descriptor, ordered. |
| `GetRegisteredParameters()` | Every registered parameter descriptor, ordered. |
| `GetCapabilities(AIDeployment)` | Resolves capabilities from an already loaded deployment. |
| `GetCapabilitiesAsync(string, CancellationToken)` | Loads the deployment by name and resolves its capabilities. |
| `SupportsFeatureOrUnconstrained(AIDeployment, string)` | Opt-out check: a deployment with **no** capability metadata counts as supporting the feature. Use for `textGeneration`. |
| `GetDeploymentsWithFeatureAsync(string, CancellationToken)` | Every deployment whose model declares the feature. |
| `ResolveDeploymentWithFeatureAsync(string, string?, CancellationToken)` | Returns the named deployment **only if** it declares the feature; when no name is given, the first deployment that declares it. Returns `null` when none qualifies. |

The returned `AIDeploymentCapabilities` exposes `Features`, `Parameters`, `SupportsFeature`,
`SupportsParameter`, and `GetParameter`. Descriptors are cloned, so the registered definitions are
never mutated by a deployment override.

:::note Opt-in vs opt-out at the call site
Use `GetCapabilities(deployment).SupportsFeature("realtime")` for opt-in features — a deployment must
explicitly declare `realtime`. Use `SupportsFeatureOrUnconstrained(deployment, "textGeneration")` for
opt-out features — a deployment with no metadata is treated as text-capable, so legacy deployments keep
working.
:::

## Storing selected values

Consumers store their selections with `AIDeploymentParametersMetadata`, again through the extensible
entity `Properties` bag:

```csharp
profile.Put(new AIDeploymentParametersMetadata
{
    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [AIDeploymentParameterNames.ReasoningEffort] = "High",
    },
});
```

This works on `AIProfile`, `AIProfileTemplate`, and `ChatInteraction`. Profile and chat interaction
context builder handlers copy the stored values onto `AICompletionContext.ModelParameters` before the
request is built.

### Markdown profile templates

Markdown-authored profile templates can set values through the `ModelParameters` front-matter key.
Pairs are `name=value`, separated by `;` or by a new line:

```markdown
---
Name: Deep research
ModelParameters: reasoningEffort=High
---

You are a meticulous research assistant.
```

## Runtime binding

`ModelParametersAICompletionServiceHandler` runs as an `IAICompletionServiceHandler` and is the single
enforcement point:

1. It iterates only the parameters the **deployment** exposes. A value stored for an unsupported
   parameter is ignored and never leaves the process.
2. When the stored value is missing or invalid for the resolved descriptor, the deployment default is
   used and a warning is logged.
3. If an `IAIDeploymentParameterBinder` is registered for the parameter, the binder shapes the request.
4. Otherwise the value is written to `ChatOptions.AdditionalProperties` so providers that read raw
   properties still receive it.

`ReasoningEffortModelParameterBinder` implements step 3 for `reasoningEffort` by setting
`ChatOptions.Reasoning.Effort`.

### Feature enforcement

`ModelFeaturesAICompletionServiceHandler` enforces the **features** a deployment declares. It runs
after the tool-adding handlers so it can strip options that depend on an unsupported trained
capability:

- When the deployment does not declare `toolCalling`, any `ChatOptions.Tools` and `ChatOptions.ToolMode`
  are cleared before the request leaves the process.
- When the deployment does not declare `structuredOutputs`, a JSON `ChatOptions.ResponseFormat` is
  removed.
- When the deployment does not declare `reasoning`, `ChatOptions.Reasoning` is removed. When it does
  declare `reasoning`, the requested effort is validated against the effective `reasoningEffort`
  parameter and coerced to the deployment default (or removed when the parameter is not exposed).

The same feature logic also runs inside a `CapabilityEnforcingChatClient` that `IAIClientFactory` wraps
around every chat client it creates, as the terminal layer immediately above the provider-facing
client. This guarantees enforcement even when a caller resolves an `IChatClient` from the factory and
calls it directly, outside the completion pipeline.

Because `streaming` is a method choice rather than a `ChatOptions` field, it is enforced at the call
site: when a deployment does not declare `streaming`, a streaming request is transparently completed as
a single non-streaming response and replayed as one streaming update. This applies both to the
client-factory path (`CapabilityEnforcingChatClient`) and to `AzureOpenAICompletionClient`, which
streams through the Azure SDK directly.

Enforcement is **opt-in**: it only applies to deployments that declare capability metadata. A
deployment with no `AIDeploymentMetadata` is treated as unconstrained, so existing configurations keep
working unchanged. Combined with the parameter handler above, this guarantees that neither unsupported
parameters (for example `reasoningEffort`) nor unsupported options (tools, structured output,
reasoning) are sent to a model that was not trained for them.

:::note
Azure OpenAI builds `OpenAI.Chat.ChatCompletionOptions` directly instead of going through
`Microsoft.Extensions.AI.ChatOptions`. `AzureOpenAICompletionClient` therefore translates the resolved
reasoning effort onto `ChatCompletionOptions.ReasoningEffortLevel` as well, so both request paths
behave the same.
:::

## Building a capability-aware chat UI

This is the pattern the built-in hosts follow, and the one your own chat surface should follow. The
guiding rule is simple:

> **The chat input is chosen from the effective deployment's capabilities — not from a global setting.**
> A deployment that declares `realtime` renders as **audio-only**; every other chat deployment renders a
> text box.

### The two features that drive input modality

| Feature | Meaning for the UI |
| --- | --- |
| `textGeneration` (opt-out, default on) | The model can take a typed turn. Render the text box, send button, and — when the site has the matching default deployments — the speech-to-text and STT/TTS conversation controls. |
| `realtime` (opt-in) | The model is speech-to-speech. Render **only** a *Start speaking* control that opens a realtime voice session, and **hide the text box** — such a model cannot process a text turn, so sending text would fail. |

A model that declares `realtime` and omits `textGeneration` is voice-only. A model that declares both is
unusual but valid — treat it as realtime-capable for the voice control while still allowing text.

### Effective chat mode

`ChatMode` has four values: `TextInput`, `AudioInput`, `Conversation`, and `Realtime`. Resolve the
effective mode on the server from the deployment the surface will actually use, letting a realtime model
win:

```csharp
// The selected deployment takes precedence: a realtime model forces audio-only, because it
// cannot handle a text turn.
var selectedIsRealtime = !string.IsNullOrWhiteSpace(deploymentName)
    && await _capabilityService.ResolveDeploymentWithFeatureAsync(
        AIDeploymentFeatureNames.Realtime, deploymentName) is not null;

var effectiveChatMode = selectedIsRealtime
    ? ChatMode.Realtime
    : /* fall back to your STT / conversation / text logic */ ChatMode.TextInput;
```

Where the deployment comes from depends on the surface:

| Surface | Text deployment | Realtime deployment |
| --- | --- | --- |
| **AI Profile / AI Chat widget** | `AIProfile.ChatDeploymentName` | `AIProfile.RealtimeDeploymentName` (falls back to the site default) |
| **Chat Interaction** | `ChatInteraction.ChatDeploymentName` | The **same** selected `ChatDeploymentName` when it declares `realtime`; otherwise the site default |
| **Site default (fallback)** | — | `DefaultAIDeploymentSettings.DefaultRealtimeDeploymentName` |

Because a Chat Interaction has a single deployment picker, selecting a realtime model there *is* the
realtime deployment. The AI Profile keeps text and voice as separate fields so a profile can use a text
model for typed turns and a realtime model for voice.

### Switching the input live

When your surface lets the operator change the deployment without a reload (the Chat Interaction picker
does), pass the set of realtime-capable deployment names to the client and flip the input on change:

```csharp
// Server: expose which deployments are realtime-capable.
var realtimeDeployments = await _capabilityService.GetDeploymentsWithFeatureAsync(
    AIDeploymentFeatureNames.Realtime);
model.RealtimeCapableDeploymentNames = realtimeDeployments
    .Select(d => d.Name)
    .Where(name => !string.IsNullOrWhiteSpace(name))
    .ToArray();
```

```javascript
// Client: audio-only when the selected deployment is realtime, text otherwise.
function applyRealtimeMode(enable) {
    chatInput.hidden = enable;
    sendButton.hidden = enable;
    realtimeButton.hidden = !enable;  // the "Start speaking" control
}

deploymentSelect.addEventListener('change', function () {
    var name = deploymentSelect.value.toLowerCase();
    applyRealtimeMode(realtimeCapableDeployments.indexOf(name) !== -1);
});
```

### Realtime transport

Realtime runs over the chat SignalR hub, not the request/response completion pipeline. The client and
server exchange raw **PCM16, 24 kHz, mono** audio as base64 frames:

- **Send** — capture the microphone, downmix to 16-bit PCM, and stream base64 frames through a
  `signalR.Subject` to the hub's `StartRealtimeConversation` method (its first argument is the
  session/interaction identifier, followed by the audio stream, the voice, and the language).
- **Receive** — assistant audio arrives as `ReceiveAudioChunk(id, base64, "audio/pcm")`; schedule those
  frames for immediate playback through the Web Audio API. (STT/TTS *conversation* audio arrives on the
  same callback but tagged `audio/mp3` / `audio/wav` and is collected and played on completion — branch
  on the content type.) User and assistant **transcripts** arrive through the same conversation
  callbacks that STT/TTS conversation mode already uses, so realtime renders in the same message list.

For complete client implementations of this contract, see the `CrestApps.Core.Mvc.Web`
`Areas/AIChat` and `Areas/ChatInteractions` chat views (and the shared `ai-chat.js`), which capture the
microphone, stream PCM16 frames, and play back the assistant audio.

### Guardrails you get for free

Even if a UI lets a bad combination through, the framework refuses to fail silently:

- **Save-time validation** — the AI Profile, AI Profile Template, and Chat Interaction editors reject a
  realtime-only model (one that does not support `textGeneration`) in a **text** chat slot, and reject a
  non-realtime model in a **realtime** slot, using `SupportsFeatureOrUnconstrained` and
  `ResolveDeploymentWithFeatureAsync` respectively.
- **Runtime message** — if a text turn still reaches a realtime-only model, the chat surface returns a
  clear explanation ("the selected chat deployment may not support text conversation…") as the assistant
  response instead of an empty or failed turn.

## Registering your own definitions

Any module can contribute definitions during startup.

```csharp
services.AddAIDeploymentFeature(
    "webSearch",
    new LocalizedString("webSearch", "Web search"),
    feature =>
    {
        feature.Description = new LocalizedString("webSearch", "The provider runs a hosted web-search tool for this model.");
        feature.Order = 200;
    });

services.AddAIDeploymentParameter(
    "verbosity",
    new LocalizedString("verbosity", "Verbosity"),
    parameter =>
    {
        parameter.Kind = AIDeploymentParameterKind.Choice;
        parameter.DefaultValue = "medium";
        parameter.AllowedValues =
        [
            new AIDeploymentParameterOption { Value = "low", DisplayName = new LocalizedString("low", "Low") },
            new AIDeploymentParameterOption { Value = "medium", DisplayName = new LocalizedString("medium", "Medium") },
            new AIDeploymentParameterOption { Value = "high", DisplayName = new LocalizedString("high", "High") },
        ];
    });
```

Registering the same name again updates the existing descriptor instead of adding a duplicate, so a
provider module can refine a definition contributed by another module.

### Linking a parameter to a feature

A parameter can declare that it only applies when the model exposes a specific trained feature by
setting `RequiredFeature` to the feature name. The built-in `reasoningEffort` parameter uses this to
depend on the `reasoning` feature:

```csharp
services.AddAIDeploymentParameter(
    AIDeploymentParameterNames.ReasoningEffort,
    new LocalizedString(AIDeploymentParameterNames.ReasoningEffort, "Reasoning effort"),
    parameter =>
    {
        parameter.Kind = AIDeploymentParameterKind.Choice;
        parameter.RequiredFeature = AIDeploymentFeatureNames.Reasoning;
        // allowed values, default, etc.
    });
```

When `RequiredFeature` is set, the deployment editor only shows the parameter while the matching
feature checkbox is enabled, and clearing the feature also clears the dependent parameter so a
contradictory combination (for example a `reasoningEffort` value on a model that is not a reasoning
model) can never be saved. The relationship is also enforced in the framework:
`IAIDeploymentCapabilityService.GetCapabilities` excludes a parameter whose `RequiredFeature` is not
among the deployment's declared features, so `ModelParametersAICompletionServiceHandler` never applies
it — regardless of how the metadata was authored.

### Parameter kinds

| Kind | Editor | Notes |
| --- | --- | --- |
| `Choice` | Drop-down | Requires `AllowedValues`. |
| `Number` | Numeric input | Honors `Minimum`, `Maximum`, and `Step`. |
| `Integer` | Numeric input | Honors `Minimum`, `Maximum`, and `Step`. |
| `Boolean` | Drop-down of `true` / `false` | |
| `Text` | Free-text input | |

### Custom binders

Implement `IAIDeploymentParameterBinder` when a parameter needs to shape the request beyond
`AdditionalProperties`:

```csharp
public sealed class VerbosityModelParameterBinder : IAIDeploymentParameterBinder
{
    public string ParameterName => "verbosity";

    public ValueTask BindAsync(AIDeploymentParameterBindingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ChatOptions.AdditionalProperties ??= [];
        context.ChatOptions.AdditionalProperties["verbosity"] = context.Value;

        return ValueTask.CompletedTask;
    }
}
```

```csharp
services.AddScoped<IAIDeploymentParameterBinder, VerbosityModelParameterBinder>();
```

The binding context exposes the resolved `Descriptor`, the selected `Value`, the `ChatOptions` being
built, the `CompletionContext`, and the `Deployment`.

## Sample host editors

Both sample hosts render the metadata rather than hardcoding options.

- **AI Deployment editor** — lists every registered feature as a checkbox under a **Trained features**
  heading (features flagged `EnabledByDefault` are pre-checked on new deployments). A parameter that
  declares a `RequiredFeature` is rendered inline beneath its feature checkbox and is only shown while
  that feature is enabled; the **Model parameters** heading only appears for parameters that are not
  linked to a feature. Each parameter offers a *supported* toggle, an allowed-values selector, a default
  value, and numeric bounds where applicable.
- **AI Profile, AI Profile Template, and Chat Interaction editors** — render only the parameters the
  selected deployment supports, restricted to that deployment's allowed values, and show the selected
  deployment's declared trained capabilities as read-only badges so operators can see what the model
  supports. They also validate the chosen deployment against the slot's required capability at save time
  (see [Guardrails](#guardrails-you-get-for-free)).
- **AI Chat and Chat Interaction chat views** — switch the input between a text box and an audio-only
  *Start speaking* control based on the effective deployment's `realtime` capability, as described in
  [Building a capability-aware chat UI](#building-a-capability-aware-chat-ui).

In `CrestApps.Core.Mvc.Web` the server renders every registered parameter inside hidden, disabled
wrappers together with a deployment-to-capability JSON map; a small script shows, enables, and filters
the fields when the deployment selection changes. Disabled inputs are not posted, so an unsupported
value can never be submitted. Both the `CrestApps.Core.Mvc.Web` and `CrestApps.Core.Blazor.Web`
deployment editors render the allowed-values selectors with the
[`@crestapps/bootstrap-select`](https://github.com/CrestApps/bootstrap-select) picker for a searchable
multi-select experience, so the two hosts present the same editing UI. In `CrestApps.Core.Blazor.Web`
the picker is wrapped in an isolated `BootstrapMultiSelect` component that initializes the plugin once
through a small JS interop module and reports selection changes back to Blazor, and the components prune
values that the newly selected deployment does not support.

:::note
The GitHub Copilot and Claude orchestrators keep their own effort settings
(`CopilotReasoningEffort`, `ClaudeEffortLevel`). Those are orchestrator/session-level options rather
than deployment-level model parameters and are intentionally left unchanged.
:::

## Related

- [AI Core](./ai-core.md)
- [AI Profiles](./ai-profiles.md)
- [AI Templates](./ai-templates.md)
- [Chat](./chat.md)
- [SignalR](./signalr.md)
- [Extensible Entity](./extensible-entity.md)
