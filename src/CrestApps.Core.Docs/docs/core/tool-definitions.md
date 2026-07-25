---
sidebar_label: Tool Definitions
sidebar_position: 9
title: Parameterized Tool Definitions
description: Let users configure reusable tool definitions with their own endpoints, credentials, and settings that the AI model invokes on demand.
---

# Parameterized Tool Definitions

> Author a tool **source** once in code, then let users create multiple configured **definitions** of it. The user supplies the parameters (endpoint, authentication, headers, …) up front; the AI model only decides *when* to invoke each definition.

## Quick Start

You author a **source** (a reusable blueprint) in code, and users create configured **definitions** of it from the UI. Author your own source by deriving from `AIToolSource` and registering it with the generic `AddAIToolSource<TSource>()`:

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    // Register your own source (blueprint). Users create definitions of it via the UI.
    .AddAIToolSource<MyToolSource>();
```

The framework also ships **one** built-in source — the [HTTP API Request tool](#built-in-http-api-request-tool) — as a ready-made example you can register with a single call:

```csharp
// A built-in source; equivalent to registering your own "call any HTTP API" blueprint.
builder.Services.AddApiRequestToolSource();
```

Either way, once a source is registered, users create definitions in the management UI, give each a clear **description**, and attach one or more definitions to an AI profile. Each definition appears to the model as a distinct callable function.

## Problem & Solution

A [custom AI tool](tools) exposes a function whose arguments are always supplied by the model. That is perfect for stateless helpers (calculator, weather), but it does not fit tools that must be *configured before use*, such as:

- calling a specific external HTTP API with a fixed endpoint, auth, and headers;
- talking to an internal service that requires a secret the model must never see;
- the same capability pointed at several different targets (staging vs. production, two vendors, …).

**Tool definitions** solve this by splitting the concern in two:

| Concept | Authored by | Responsibility |
|---------|-------------|----------------|
| **Source** (`AIToolSource`) | Developer (code) | Describes *how* the tool works and how to build it from stored settings. |
| **Definition** (`AIToolDefinition`) | End user (UI) | Supplies *the parameters* (endpoint, credentials, headers) and a natural-language description. |

The AI model still decides *when* to call, but it calls the user-configured definition, using the user's predefined settings. Because every definition carries its own user-written description, the model can distinguish multiple definitions built from the same source.

`AIToolDefinition` is a sealed [`SourceCatalogEntry`](extensible-entity): its `Source` property records which `AIToolSource` produced it, and the management UI adapts to that source. This mirrors the framework's other source-aware catalogs (AI deployments, AI data sources).

## How It Works

```
AIToolSource  ──►  AIToolDefinition (user settings)  ──►  AITool  ──►  ChatOptions.Tools
   (code)              (catalog entry)                  (per definition)    (model)
```

1. A developer registers a **source** with `AddAIToolSource<TSource>()`.
2. A user creates one or more **definitions** from that source and stores settings on the definition.
3. The user attaches definitions to an AI profile (via `AIProfileToolDefinitionMetadata`).
4. During completion, `ToolDefinitionRegistryProvider` materializes each referenced definition into a distinct `AITool` whose function name and description are unique per definition.
5. The resulting `AITool` flows into `ChatOptions.Tools` through `Microsoft.Extensions.AI`, so **every client (OpenAI, Azure OpenAI, …) works with no client-specific code**.

Distinct per-definition function names are produced by `AIToolDefinitionNaming.GetFunctionName`, so definitions never collide even when they share a source.

## Authoring a Source

Authoring your own source is the core extension point — the built-in HTTP tool below is authored exactly this way. Derive from `AIToolSource`, override the metadata (`Name`, `DisplayName`, `Description`, `Category`), and implement `CreateTool`. `CreateTool` reads the definition's stored settings and returns an `AITool` bound to them.

```csharp
using CrestApps.Core;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;

public sealed class HttpApiRequestToolSource : AIToolSource
{
    public override string Name => "http-api-request";

    public override LocalizedString DisplayName => new("HTTP API Request", "HTTP API Request");

    public override LocalizedString Description => new(
        "HTTP API Request Description",
        "Call an external HTTP API with a preconfigured endpoint, method, authentication, and headers.");

    public override string Category => "Integrations";

    public override AITool CreateTool(AIToolSourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Read the settings the user stored on the definition.
        var settings = context.Definition.TryGet<HttpApiRequestToolSettings>(out var stored)
            ? stored
            : new HttpApiRequestToolSettings();

        // FunctionName and Description are unique per definition so the model can tell them apart.
        return new HttpApiRequestToolFunction(context.FunctionName, context.Description, settings);
    }
}
```

The returned tool is an ordinary `Microsoft.Extensions.AI.AIFunction`. Read the settings the user captured, resolve any services you need from `AIFunctionArguments.Services`, and only accept the open arguments you allow the model to supply. Because a source's display metadata lives directly on the class, no separate options, entry, or builder types are required.

### Persisting settings on the definition

`AIToolDefinition` extends the framework's [extensible entity](extensible-entity), so a source persists its own strongly typed settings model in the definition's properties:

```csharp
// When saving (UI/controller):
definition.Put(new HttpApiRequestToolSettings { BaseUrl = "https://api.example.com", ... });

// When building the tool (source):
definition.TryGet<HttpApiRequestToolSettings>(out var settings);
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
builder.Services.AddAIToolSource<HttpApiRequestToolSource>();
```

`AddAIToolSource<TSource>` calls `AddCoreAIToolDefinitions()` for you and registers the source as an enumerable `AIToolSource` singleton. The source's own overridden `Name`, `DisplayName`, `Description`, and `Category` provide everything the UI and registry need — there is no separate options object or fluent builder.

| Override | Use |
|---|---|
| `Name` | Unique key stored as each definition's `Source`. |
| `DisplayName` | Friendly name shown when choosing a source. |
| `Description` | Explains what the source does. |
| `Category` | UI grouping. |

## Built-In HTTP API Request Tool

The framework ships a ready-to-use source that calls arbitrary HTTP APIs. Register it with a single call:

```csharp
builder.Services.AddApiRequestToolSource();
```

This registers the `http-api-request` source plus its named `HttpClient`. Each definition captures:

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

## Creating Definitions (as a User)

In the sample hosts, open **AI Tool Definitions**, then:

1. Choose a **source** (for example, *HTTP API Request*).
2. Enter a **display name** and a **description**. The description is the primary signal the model uses to tell definitions apart, so make it specific — e.g. *"Looks up order status from the Orders API."*
3. Fill in the source-specific settings (endpoint, auth, headers, …).
4. Save.

Repeat to add **multiple definitions from the same source** — each with different settings and its own description. For example, one definition calls the Orders API and another calls the Weather API; both use the same `http-api-request` source but appear to the model as two separate functions.

## Attaching Definitions to a Profile

Definitions only reach the model when a profile references them. The sample hosts add a checkbox section on the AI profile Create/Edit pages; the selected definition IDs are stored via `AIProfileToolDefinitionMetadata`:

```csharp
profile.Alter<AIProfileToolDefinitionMetadata>(metadata =>
{
    metadata.DefinitionIds = selectedDefinitionIds;
});
```

At completion time, `AIToolDefinitionCompletionContextBuilderHandler` copies those IDs onto `AICompletionContext.ToolDefinitionIds`, and the registry provider surfaces each as a distinct tool.

## Persistence

The `AIToolDefinition` catalog is registered automatically with your store provider when you register the AI stores:

- **YesSql** — `AddCoreAIServicesStoresYesSql()` registers the catalog and the `AIToolDefinitionIndex`. Create the index table during startup with `CreateAIToolDefinitionIndexSchemaAsync()`.
- **Entity Framework Core** — `AddCoreAIServicesStoresEntityCore()` registers the source-document catalog.

No extra wiring is required beyond registering the store suite; only the source (`AddApiRequestToolSource()` or your own) and the management UI are app-specific.

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

To verify that multiple definitions surface distinctly, build a service provider with the source registered plus an `ISourceCatalog<AIToolDefinition>` returning two definitions, then assert `ToolDefinitionRegistryProvider.GetToolsAsync` returns two entries with distinct `Name` and `Description`.

:::tip
The sample projects (`CrestApps.Core.Mvc.Web` and `CrestApps.Core.Blazor.Web`) register `AddApiRequestToolSource()` and include the full management UI. Run the Aspire host and add two HTTP API definitions to see them appear to the model as separate functions.
:::

## Related

- [Custom AI Tools](tools) — tools whose arguments are always supplied by the model.
- [Extensible Entity](extensible-entity) — how definition settings are persisted.
- The Orchard Core CMS integration builds on the same abstractions; see the downstream product docs at [orchardcore.crestapps.com](https://orchardcore.crestapps.com).
