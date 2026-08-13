---
sidebar_label: Claude
sidebar_position: 17
title: Claude Orchestrator
description: Configure Claude as a first-class CrestApps.Core orchestrator using the official Anthropic C# SDK and the MVC sample host.
---

# Claude Orchestrator

> A Claude-powered `IOrchestrator` implementation built on the official `Anthropic` NuGet package. It runs entirely through managed SDK dependencies, so hosts do not need to install any external executable or CLI.

## Quick start

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddClaudeOrchestrator()
    )
);
```

Or register the lower-level service directly:

```csharp
builder.Services.AddCoreAIClaudeOrchestrator();
```

Resolve it by name:

```csharp
public sealed class MyController(IOrchestratorResolver resolver)
{
    public async IAsyncEnumerable<string> StreamAsync(OrchestrationContext context)
    {
        var orchestrator = resolver.Resolve("anthropic");

        await foreach (var update in orchestrator.ExecuteStreamingAsync(context))
        {
            yield return update.Text;
        }
    }
}
```

## What it adds

The `CrestApps.Core.AI.Claude` package mirrors the Copilot integration points that matter for host applications:

- registers a named orchestrator (`"anthropic"`) with the title **Claude**
- adds an orchestration-context handler that reads `ClaudeSessionMetadata`
- adds a chat-interaction settings handler so SignalR/MVC chat settings persist Claude model overrides
- exposes `ClaudeClientService` for live model discovery through the Anthropic Models API
- keeps all dependencies inside normal NuGet restore and application publish flows

Unlike Copilot, Claude does **not** use GitHub OAuth, a credential store contract, or build-transitive CLI download targets.

## Configuration

### `ClaudeOptions`

```csharp
public sealed class ClaudeOptions
{
    public string ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string DefaultModel { get; set; }
}
```

### `appsettings.json`

```json
{
  "ClaudeOptions": {
    "ApiKey": "sk-ant-...",
    "BaseUrl": "https://api.anthropic.com",
    "DefaultModel": "claude-sonnet-4-6"
  }
}
```

The orchestrator requires an API key. A default model is optional when profiles, templates, or chat interactions provide their own Claude model override.

Per-profile, per-template, and per-chat overrides are stored in `ClaudeSessionMetadata`.

## Services registered by `AddCoreAIClaudeOrchestrator()`

| Service | Implementation | Lifetime | Purpose |
| --- | --- | --- | --- |
| `IOrchestrator` | `ClaudeOrchestrator` (name: `"anthropic"`) | Scoped | Streams Claude responses through the shared orchestration pipeline |
| `ClaudeClientService` | `ClaudeClientService` | Scoped | Creates configured SDK clients and lists available models |
| `IChatInteractionSettingsHandler` | `ClaudeChatInteractionSettingsHandler` | Scoped | Persists Anthropic-specific chat settings |
| `IOrchestrationContextBuilderHandler` | `ClaudeOrchestrationContextHandler` | Scoped | Pushes Anthropic metadata into orchestration context |

## MVC sample host

`CrestApps.Core.Mvc.Web` registers the Claude orchestrator by default:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddClaudeOrchestrator()
        .AddCopilotOrchestrator()
    )
);
```

The MVC sample also wires Claude into:

1. **Admin → Settings** for Claude authentication mode, API key, base URL, and the optional default model
2. **AI Profiles** for profile-level model overrides
3. **AI Templates** for profile-source template overrides
4. **Chat Interactions** for interactive Claude model selection

When the API key is configured, the MVC editors load available Claude models from the Anthropic Models API. If model discovery is unavailable, the same editors fall back to free-form model entry.
