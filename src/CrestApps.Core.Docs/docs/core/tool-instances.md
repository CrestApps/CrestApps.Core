---
sidebar_label: Tool Instances
sidebar_position: 9
title: Parameterized Tool Instances
description: Let users configure reusable tool instances with their own endpoints, credentials, and settings that the AI model invokes on demand.
---

# Parameterized Tool Instances

> Author a tool **source** once in code, then let users create multiple configured **instances** of it from the UI. Each instance supplies the parameters (endpoint, authentication, headers, …) and a natural-language description up front; the AI model only decides *when* to invoke each instance.

## Quick Start

A developer authors a **source** (a reusable blueprint) in code by implementing `IAIToolInstanceSource`, and registers it under a unique name with `AddAIToolInstanceSource<TSource>()`:

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    // Register your own source (blueprint) under a unique name.
    // Users create instances of it via the UI.
    .AddAIToolInstanceSource<MyToolInstanceSource>("my-source", options =>
    {
        options.DisplayName = new LocalizedString("my-source", "My Source");
        options.Description = new LocalizedString("my-source", "What this source does.");
        options.Category = "Integrations";
    });
```

The framework also ships a built-in source — the [HTTP API Request tool](#built-in-http-api-request-tool) — as a ready-made example. Register its named `HttpClient` and the source:

```csharp
builder.Services.AddHttpClient(HttpApiRequestToolConstants.HttpClientName);
builder.Services.AddAIToolInstanceSource<HttpApiRequestToolInstanceSource>(
    HttpApiRequestToolConstants.SourceName,
    options =>
    {
        options.DisplayName = new LocalizedString(HttpApiRequestToolConstants.SourceName, "HTTP API Request");
        options.Description = new LocalizedString(HttpApiRequestToolConstants.SourceName, "Calls an external HTTP API using preconfigured settings (endpoint, authentication, headers).");
        options.Category = "Integrations";
    });
```

Either way, once a source is registered, users create instances in the management UI, give each a unique **name** and a clear **description**, and attach one or more instances to an AI profile. Each instance appears to the model as a distinct callable function.

## Problem & Solution

A [custom AI tool](tools) exposes a function whose arguments are always supplied by the model. That is perfect for stateless helpers (calculator, weather), but it does not fit tools that must be *configured before use*, such as:

- calling a specific external HTTP API with a fixed endpoint, auth, and headers;
- talking to an internal service that requires a secret the model must never see;
- the same capability pointed at several different targets (staging vs. production, two vendors, …).

**Tool instances** solve this by splitting the concern in two:

| Concept | Authored by | Responsibility |
|---------|-------------|----------------|
| **Source** (`IAIToolInstanceSource`) | Developer (code) | Describes *how* the tool works and how to build it from stored settings. |
| **Instance** (`AIToolInstance`) | End user (UI) | Supplies *the parameters* (endpoint, credentials, headers), a unique name, and a natural-language description. |

The AI model still decides *when* to call, but it calls the user-configured instance, using the user's predefined settings. Because every instance carries its own unique name and user-written description, the model can distinguish multiple instances built from the same source.

`AIToolInstance` is a sealed [`SourceCatalogEntry`](extensible-entity): its `Source` property records which source produced it, and the management UI adapts to that source. This mirrors the framework's other source-aware catalogs (AI connections, AI deployments, AI data sources).

## How It Works

```
IAIToolInstanceSource  ──►  AIToolInstance (user settings)  ──►  AITool  ──►  ChatOptions.Tools
       (code)                    (catalog entry)                (per instance)     (model)
```

1. A developer registers a **source** with `AddAIToolInstanceSource<TSource>(name, configure)`.
2. A user creates one or more **instances** from that source, each with a unique name, a description, and its own stored settings.
3. The user attaches instances to an AI profile (via `AIProfileToolInstanceMetadata`).
4. During completion, `ToolInstanceRegistryProvider` materializes each referenced instance into a distinct `AITool` whose function name and description are unique per instance.
5. The resulting `AITool` flows into `ChatOptions.Tools` through `Microsoft.Extensions.AI`, so **every client (OpenAI, Azure OpenAI, …) works with no client-specific code**.

Distinct per-instance function names are produced by `AIToolInstance.GetFunctionName()`, which sanitizes the instance's unique `Name`, so instances never collide even when they share a source.

## Authoring a Source

Authoring your own source is the core extension point — the built-in HTTP tool below is authored exactly this way. Implement `IAIToolInstanceSource` and its single `CreateTool` method. `CreateTool` reads the instance's stored settings and returns an `AITool` bound to them, using the supplied `FunctionName` and `Description` so the instance surfaces distinctly.

```csharp
using CrestApps.Core;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

public sealed class HttpApiRequestToolInstanceSource : IAIToolInstanceSource
{
    public AITool CreateTool(AIToolInstanceSourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Read the settings the user stored on the instance.
        var settings = context.Instance.TryGet<HttpApiRequestToolSettings>(out var stored)
            ? stored
            : new HttpApiRequestToolSettings();

        // FunctionName and Description are unique per instance so the model can tell them apart.
        return new HttpApiRequestToolFunction(context.FunctionName, context.Description, settings);
    }
}
```

The returned tool is an ordinary `Microsoft.Extensions.AI.AIFunction`. Read the settings the user captured, resolve any services you need from `AIFunctionArguments.Services`, and only accept the open arguments you allow the model to supply. The source's *display* metadata (display name, description, category) is provided at registration time — not on the class — so no separate options or builder types are required.

### Persisting settings on the instance

`AIToolInstance` extends the framework's [extensible entity](extensible-entity), so a source persists its own strongly typed settings model in the instance's properties:

```csharp
// When saving (UI/controller):
instance.Put(new HttpApiRequestToolSettings { BaseUrl = "https://api.example.com", ... });

// When building the tool (source):
instance.TryGet<HttpApiRequestToolSettings>(out var settings);
```

### Protecting secrets

Never store credentials in plain text. Protect them with ASP.NET Core Data Protection when saving and unprotect them at invocation time:

```csharp
var protector = provider.CreateProtector("MySource.Secrets");
var stored = protector.Protect(userSuppliedSecret);      // when saving
var secret = protector.Unprotect(stored);                // inside the tool
```

The built-in HTTP tool follows this pattern: on edit, a blank secret reuses the previously stored value, and `Unprotect` tolerates config-seeded plain values.

## Registering a Source

```csharp
builder.Services.AddAIToolInstanceSource<HttpApiRequestToolInstanceSource>(
    HttpApiRequestToolConstants.SourceName,
    options =>
    {
        options.DisplayName = new LocalizedString(HttpApiRequestToolConstants.SourceName, "HTTP API Request");
        options.Description = new LocalizedString(HttpApiRequestToolConstants.SourceName, "Calls an external HTTP API.");
        options.Category = "Integrations";
    });
```

`AddAIToolInstanceSource<TSource>(name, configure)` does three things:

- calls `AddCoreAIToolInstances()` for you (registers the catalog handler, completion-context builder handler, and the default registry provider);
- registers the source as a **keyed** scoped `IAIToolInstanceSource`, keyed by `name` (the value stored as each instance's `Source`);
- records the source's display metadata in `AIOptions.ToolInstanceSources[name]` via the `configure` delegate.

| Configure property | Use |
|---|---|
| `DisplayName` | Friendly name shown when choosing a source (`LocalizedString`). |
| `Description` | Explains what the source does (`LocalizedString`). |
| `Category` | UI grouping. |

If `DisplayName` is left empty it defaults to the registered `name`.

## Built-In HTTP API Request Tool

The framework ships a ready-to-use source that calls arbitrary HTTP APIs. Register its named `HttpClient` and the source:

```csharp
builder.Services.AddHttpClient(HttpApiRequestToolConstants.HttpClientName);
builder.Services.AddAIToolInstanceSource<HttpApiRequestToolInstanceSource>(
    HttpApiRequestToolConstants.SourceName, /* configure */);
```

Each instance captures:

| Setting | Purpose |
|---|---|
| `BaseUrl` | The endpoint the request targets. |
| `HttpMethod` | `GET`, `POST`, `PUT`, `PATCH`, or `DELETE`. |
| `AuthenticationType` | `None`, `ApiKey`, `Bearer`, or `Basic`. |
| `ApiKey` / `ApiKeyHeaderName` | API-key auth (header defaults to `X-Api-Key`). |
| `BearerToken` | Bearer token (`Authorization: Bearer …`). |
| `BasicUsername` / `BasicPassword` | HTTP basic auth. |
| `DefaultHeaders` | Static headers always added. |
| `AllowModelProvidedPath` / `…Query` / `…Body` | Which open arguments the model may supply. |
| `TimeoutSeconds` | Optional per-request timeout. |

Credentials (`ApiKey`, `BearerToken`, `BasicPassword`) are data-protected at rest with the `HttpApiRequestToolConstants.DataProtectionPurpose` purpose.

The tool exposes only the open arguments you enable (`path`, `query`, `body`) and returns a JSON envelope:

```json
{
  "success": true,
  "statusCode": 200,
  "reasonPhrase": "OK",
  "contentType": "application/json",
  "truncated": false,
  "body": "…"
}
```

## Creating Instances (as a User)

In the sample hosts, open **AI Tool Instances**, then:

1. Choose a **source** (for example, *HTTP API Request*).
2. Enter a unique **technical name** and a **description**. The name becomes the function name exposed to the model, and the description is the primary signal the model uses to tell instances apart, so make it specific — e.g. *"Looks up order status from the Orders API."*
3. Fill in the source-specific settings (endpoint, auth, headers, …).
4. Save.

Repeat to add **multiple instances from the same source** — each with a different name, settings, and description. For example, one instance calls the Orders API and another calls the Weather API; both use the same `http-api-request` source but appear to the model as two separate functions.

## Attaching Instances to a Profile

Instances only reach the model when a profile references them. The sample hosts add a checkbox section on the AI profile Create/Edit pages; the selected instance IDs are stored via `AIProfileToolInstanceMetadata`:

```csharp
profile.Alter<AIProfileToolInstanceMetadata>(metadata =>
{
    metadata.InstanceIds = selectedInstanceIds;
});
```

At completion time, `AIToolInstanceCompletionContextBuilderHandler` copies those IDs onto `AICompletionContext.ToolInstanceIds`, and the registry provider surfaces each as a distinct tool.

## Custom Tool Registry Providers

The default `ToolInstanceRegistryProvider` surfaces every referenced instance to the model unconditionally. Real applications often need extra logic — most commonly a **permission check** so an instance is only exposed to users who are allowed to use it.

Tools reach the model through the aggregated `IToolRegistryProvider` abstraction. Any number of providers can be registered; the registry concatenates the tools returned by each. To add gating, register your own `IToolRegistryProvider`:

```csharp
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;

public sealed class PermissionAwareToolRegistryProvider : IToolRegistryProvider
{
    private readonly ISourceCatalog<AIToolInstance> _catalog;
    private readonly IAuthorizationService _authorization;
    // ... resolve the current user, sources, etc.

    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var ids = context?.ToolInstanceIds;

        if (ids is null || ids.Length == 0)
        {
            return [];
        }

        var instances = await _catalog.GetAsync(ids, cancellationToken);
        var entries = new List<ToolRegistryEntry>();

        foreach (var instance in instances)
        {
            // Only expose instances the current user is permitted to use.
            if (!await IsAuthorizedAsync(instance, cancellationToken))
            {
                continue;
            }

            entries.Add(/* build a ToolRegistryEntry from the instance's source */);
        }

        return entries;
    }
}
```

Register it and, if you want it to replace the built-in behavior, remove the default provider:

```csharp
// Add your provider alongside the default one:
builder.Services.AddScoped<IToolRegistryProvider, PermissionAwareToolRegistryProvider>();

// Or replace the default provider entirely:
builder.Services.RemoveAll<IToolRegistryProvider>();
builder.Services.AddScoped<IToolRegistryProvider, PermissionAwareToolRegistryProvider>();
```

This is exactly how downstream products layer their own authorization on top of the same abstractions — for example, the Orchard Core CMS integration ships a `LocalToolRegistryProvider` that checks per-instance permissions before exposing each tool.

## Persistence

The `AIToolInstance` catalog is registered automatically with your store provider when you register the AI stores:

- **YesSql** — `AddCoreAIServicesStoresYesSql()` registers the catalog and the `AIToolInstanceIndex`. Create the index table during startup with `CreateAIToolInstanceIndexSchemaAsync()`.
- **Entity Framework Core** — `AddCoreAIServicesStoresEntityCore()` registers the source-document catalog.

No extra wiring is required beyond registering the store suite; only the source (`AddAIToolInstanceSource<TSource>(...)`) and the management UI are app-specific.

## Testing

Because a source produces an ordinary `AIFunction`, you can test it in isolation. Supply services (such as `IHttpClientFactory`) through `AIFunctionArguments.Services`:

```csharp
var settings = new HttpApiRequestToolSettings
{
    BaseUrl = "https://api.example.com/v1",
    HttpMethod = "POST",
    AuthenticationType = HttpApiRequestAuthenticationType.Bearer,
    BearerToken = "secret-token",
    AllowModelProvidedPath = true,
};

var function = new HttpApiRequestToolFunction("weather", "Gets the weather.", settings);

var arguments = new AIFunctionArguments
{
    ["path"] = "forecast",
    ["query"] = new Dictionary<string, object> { ["city"] = "Seattle" },
    Services = serviceProvider, // provides a stubbed IHttpClientFactory
};

var result = await function.InvokeAsync(arguments);
```

To verify that multiple instances surface distinctly, build a service provider with the source registered as a keyed `IAIToolInstanceSource` plus an `ISourceCatalog<AIToolInstance>` returning two instances, then assert `ToolInstanceRegistryProvider.GetToolsAsync` returns two entries with distinct `Name` and `Description`.

:::tip
The sample projects (`CrestApps.Core.Mvc.Web` and `CrestApps.Core.Blazor.Web`) register the `http-api-request` source and include the full management UI. Run the Aspire host and add two HTTP API instances to see them appear to the model as separate functions.
:::

## Related

- [Custom AI Tools](tools) — tools whose arguments are always supplied by the model.
- [Extensible Entity](extensible-entity) — how instance settings are persisted.
- The Orchard Core CMS integration builds on the same abstractions; see the downstream product docs at [orchardcore.crestapps.com](https://orchardcore.crestapps.com).
