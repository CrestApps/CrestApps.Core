---
sidebar_label: Model Capabilities
sidebar_position: 16
title: AI Model Capabilities
description: Metadata-driven model features and model parameters that describe what an AI deployment supports and which options its consumers may configure.
---

# AI Model Capabilities

> A registry of **model features** and **model parameters** that lets deployments declare what their
> model supports, so editors render only the relevant options and the runtime only sends supported values.

## Why this exists

Different models expose different knobs. A reasoning model accepts a reasoning effort level, a small
chat model does not. Without metadata, every new knob turns into hardcoded, provider-specific UI and
provider-specific request-building code.

The capability system replaces that with two extensible concepts:

| Concept | Shape | Example |
| --- | --- | --- |
| **Model feature** | A binary capability. The deployment either supports it or it does not. | `toolCalling`, `reasoning`, `streaming` |
| **Model parameter** | A configurable option carrying metadata: kind, allowed values, range, and a default. | `reasoningEffort` |

Definitions are registered once at startup. An **AI Deployment** then declares which of those
definitions its model actually exposes, optionally narrowing the allowed values. AI Profiles, AI
Profile Templates, and Chat Interactions store the selected values. At request time the framework
binds the selected values into the outgoing request.

:::info
Model features are **not** the same as `AIDeploymentPurpose`. `Purpose` (`Chat`, `Utility`,
`Embedding`, `Image`, …) drives *routing* — which deployment is picked for a given job. Features
describe *capabilities within* a deployment and deliberately avoid duplicating routing concerns.
:::

## Quick Start

```csharp
builder.Services.AddCoreAIModelCapabilities();
```

:::info
You rarely need to call this directly — `AddCoreAIServices()` chains it automatically.
:::

## Built-in definitions

### Features

| Name | Constant | Default on new deployments | Description |
| --- | --- | --- | --- |
| `toolCalling` | `AIModelFeatureNames.ToolCalling` | ✅ | The model can call tools and functions supplied with the request. |
| `structuredOutputs` | `AIModelFeatureNames.StructuredOutputs` | | The model can return responses that follow a supplied JSON schema. |
| `streaming` | `AIModelFeatureNames.Streaming` | ✅ | The model can stream response updates as they are produced. |
| `reasoning` | `AIModelFeatureNames.Reasoning` | | The model performs internal reasoning before producing an answer. |
| `imageInput` | `AIModelFeatureNames.ImageInput` | | The model can understand image inputs (vision). |
| `imageOutput` | `AIModelFeatureNames.ImageOutput` | | The model can generate images. |
| `audioInput` | `AIModelFeatureNames.AudioInput` | | The model accepts audio input. |
| `audioOutput` | `AIModelFeatureNames.AudioOutput` | | The model produces audio output. |
| `videoInput` | `AIModelFeatureNames.VideoInput` | | The model can understand video inputs. |
| `videoOutput` | `AIModelFeatureNames.VideoOutput` | | The model can generate video. |

These represent **trained capabilities** the underlying model was built with. Provider-hosted tools
(such as a web-search tool the provider runs on your behalf, or a computer-use tool) are *not* modeled
as features because almost any tool-calling model can be handed such a tool — they are ordinary tools,
not a trained
trait. Set `AIModelFeatureDescriptor.EnabledByDefault` when registering a feature to pre-select it on
newly created deployments; existing deployments are never changed by this flag.

### Parameters

| Name | Constant | Kind | Allowed values | Default |
| --- | --- | --- | --- | --- |
| `reasoningEffort` | `AIModelParameterNames.ReasoningEffort` | `Choice` | `None` (shown as *Minimal*), `Low`, `Medium`, `High`, `ExtraHigh` | `Medium` |

`reasoningEffort` maps onto `Microsoft.Extensions.AI.ChatOptions.Reasoning.Effort`, so it is
provider-agnostic and ships in the core AI package rather than in a provider module. It also declares
`RequiredFeature = AIModelFeatureNames.Reasoning`, which links the parameter to the `reasoning` trained
feature (see [Linking a parameter to a feature](#linking-a-parameter-to-a-feature)).

## Declaring what a deployment supports

Capability metadata is stored on the deployment through the
[extensible entity](./extensible-entity.md) `Properties` bag using `AIDeploymentModelMetadata`:

```csharp
deployment.Put(new AIDeploymentModelMetadata
{
    Features =
    [
        AIModelFeatureNames.ToolCalling,
        AIModelFeatureNames.Reasoning,
    ],
    Parameters = new Dictionary<string, AIDeploymentModelParameter>(StringComparer.OrdinalIgnoreCase)
    {
        [AIModelParameterNames.ReasoningEffort] = new AIDeploymentModelParameter
        {
            AllowedValues = ["Low", "Medium", "High"],
            DefaultValue = "Medium",
        },
    },
});
```

Because `AIDeploymentCatalogHandler` deep-merges `Properties`, the same metadata can be supplied from
configuration or a recipe with no extra code:

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
            "AIDeploymentModelMetadata": {
              "Features": [ "toolCalling", "reasoning", "streaming" ],
              "Parameters": {
                "reasoningEffort": {
                  "AllowedValues": [ "Low", "Medium", "High" ],
                  "DefaultValue": "Medium"
                }
              }
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

`IAIModelCapabilityService` merges the registered definitions with the deployment metadata and returns
only what the deployment exposes:

```csharp
public sealed class MyService
{
    private readonly IAIModelCapabilityService _capabilityService;

    public MyService(IAIModelCapabilityService capabilityService)
    {
        _capabilityService = capabilityService;
    }

    public async Task<bool> SupportsReasoningAsync(string deploymentName)
    {
        var capabilities = await _capabilityService.GetCapabilitiesAsync(deploymentName);

        return capabilities.SupportsFeature(AIModelFeatureNames.Reasoning);
    }
}
```

| Member | Description |
| --- | --- |
| `GetRegisteredFeatures()` | Every registered feature descriptor, ordered. |
| `GetRegisteredParameters()` | Every registered parameter descriptor, ordered. |
| `GetCapabilities(AIDeployment)` | Resolves capabilities from an already loaded deployment. |
| `GetCapabilitiesAsync(string, CancellationToken)` | Loads the deployment by name and resolves its capabilities. |

The returned `AIDeploymentCapabilities` exposes `Features`, `Parameters`, `SupportsFeature`,
`SupportsParameter`, and `GetParameter`. Descriptors are cloned, so the registered definitions are
never mutated by a deployment override.

## Storing selected values

Consumers store their selections with `AIModelParametersMetadata`, again through the extensible
entity `Properties` bag:

```csharp
profile.Put(new AIModelParametersMetadata
{
    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [AIModelParameterNames.ReasoningEffort] = "High",
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
3. If an `IAIModelParameterBinder` is registered for the parameter, the binder shapes the request.
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

Enforcement is **opt-in**: it only applies to deployments that declare capability metadata. A
deployment with no `AIDeploymentModelMetadata` is treated as unconstrained, so existing configurations
keep working unchanged. Combined with the parameter handler above, this guarantees that neither
unsupported parameters (for example `reasoningEffort`) nor unsupported options (tools, structured
output) are sent to a model that was not trained for them.

:::note
Azure OpenAI builds `OpenAI.Chat.ChatCompletionOptions` directly instead of going through
`Microsoft.Extensions.AI.ChatOptions`. `AzureOpenAICompletionClient` therefore translates the resolved
reasoning effort onto `ChatCompletionOptions.ReasoningEffortLevel` as well, so both request paths
behave the same.
:::

## Registering your own definitions

Any module can contribute definitions during startup.

```csharp
services.AddAIModelFeature(
    "webSearch",
    new LocalizedString("webSearch", "Web search"),
    feature =>
    {
        feature.Description = new LocalizedString("webSearch", "The provider runs a hosted web-search tool for this model.");
        feature.Order = 200;
    });

services.AddAIModelParameter(
    "verbosity",
    new LocalizedString("verbosity", "Verbosity"),
    parameter =>
    {
        parameter.Kind = AIModelParameterKind.Choice;
        parameter.DefaultValue = "medium";
        parameter.AllowedValues =
        [
            new AIModelParameterOption { Value = "low", DisplayName = new LocalizedString("low", "Low") },
            new AIModelParameterOption { Value = "medium", DisplayName = new LocalizedString("medium", "Medium") },
            new AIModelParameterOption { Value = "high", DisplayName = new LocalizedString("high", "High") },
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
services.AddAIModelParameter(
    AIModelParameterNames.ReasoningEffort,
    new LocalizedString(AIModelParameterNames.ReasoningEffort, "Reasoning effort"),
    parameter =>
    {
        parameter.Kind = AIModelParameterKind.Choice;
        parameter.RequiredFeature = AIModelFeatureNames.Reasoning;
        // allowed values, default, etc.
    });
```

When `RequiredFeature` is set, the deployment editor only shows the parameter while the matching
feature checkbox is enabled, and clearing the feature also clears the dependent parameter so a
contradictory combination (for example a `reasoningEffort` value on a model that is not a reasoning
model) can never be saved. The relationship is also enforced in the framework:
`IAIModelCapabilityService.GetCapabilities` excludes a parameter whose `RequiredFeature` is not among
the deployment's declared features, so `ModelParametersAICompletionServiceHandler` never applies it —
regardless of how the metadata was authored.

### Parameter kinds

| Kind | Editor | Notes |
| --- | --- | --- |
| `Choice` | Drop-down | Requires `AllowedValues`. |
| `Number` | Numeric input | Honors `Minimum`, `Maximum`, and `Step`. |
| `Integer` | Numeric input | Honors `Minimum`, `Maximum`, and `Step`. |
| `Boolean` | Drop-down of `true` / `false` | |
| `Text` | Free-text input | |

### Custom binders

Implement `IAIModelParameterBinder` when a parameter needs to shape the request beyond
`AdditionalProperties`:

```csharp
public sealed class VerbosityModelParameterBinder : IAIModelParameterBinder
{
    public string ParameterName => "verbosity";

    public ValueTask BindAsync(AIModelParameterBindingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ChatOptions.AdditionalProperties ??= [];
        context.ChatOptions.AdditionalProperties["verbosity"] = context.Value;

        return ValueTask.CompletedTask;
    }
}
```

```csharp
services.AddScoped<IAIModelParameterBinder, VerbosityModelParameterBinder>();
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
  supports.

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
- [Extensible Entity](./extensible-entity.md)
