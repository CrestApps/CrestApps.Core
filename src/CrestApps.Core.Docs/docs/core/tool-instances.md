---
sidebar_label: Tool Instances
sidebar_position: 9
title: Parameterized Tool Instances
description: Let users configure reusable tool instances with their own endpoints, credentials, and settings that the AI model invokes on demand.
---

# Parameterized Tool Instances

> Define a tool once in code, then let users create multiple configured **instances** of it. The user supplies the parameters (endpoint, authentication, headers, …) up front; the AI model only decides *when* to invoke each instance.

## Quick Start

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    // Registers the built-in "call any HTTP API" definition.
    .AddApiRequestToolInstance();
```

Once registered, users create instances in the management UI, give each a clear **description**, and attach one or more instances to an AI profile. Each instance appears to the model as a distinct callable function.

## Problem & Solution

A [custom AI tool](tools) exposes a function whose arguments are always supplied by the model. That is perfect for stateless helpers (calculator, weather), but it does not fit tools that must be *configured before use*, such as:

- calling a specific external HTTP API with a fixed endpoint, auth, and headers;
- talking to an internal service that requires a secret the model must never see;
- the same capability pointed at several different targets (staging vs. production, two vendors, …).

**Tool instances** solve this by splitting the concern in two:

| Concept | Authored by | Responsibility |
|---------|-------------|----------------|
| **Definition** (`IAIToolInstanceDefinition`) | Developer (code) | Describes *how* the tool works and how to build it from stored settings. |
| **Instance** (`AIToolInstance`) | End user (UI) | Supplies *the parameters* (endpoint, credentials, headers) and a natural-language description. |

The AI model still decides *when* to call, but it calls the user-configured instance, using the user's predefined settings. Because every instance carries its own user-written description, the model can distinguish multiple instances of the same definition.

## How It Works

```
IAIToolInstanceDefinition  ──►  AIToolInstance (user settings)  ──►  AITool  ──►  ChatOptions.Tools
        (code)                        (catalog entry)              (per instance)     (model)
```

1. A developer registers a **definition** with `AddAIToolInstanceDefinition<TDefinition>(name)`.
2. A user creates one or more **instances** of that definition and stores settings in the instance.
3. The user attaches instances to an AI profile (via `AIProfileToolInstanceMetadata`).
4. During completion, `ToolInstanceRegistryProvider` materializes each referenced instance into a distinct `AITool` whose function name and description are unique per instance.
5. The resulting `AITool` flows into `ChatOptions.Tools` through `Microsoft.Extensions.AI`, so **every client (OpenAI, Azure OpenAI, …) works with no client-specific code**.

Distinct per-instance function names are produced by `AIToolInstanceNaming.GetFunctionName`, so instances never collide even when they share a definition.

## Defining a Tool

Implement `IAIToolInstanceDefinition`. Its `CreateTool` reads the instance's stored settings and returns an `AITool` bound to them.

```csharp
using CrestApps.Core;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

public sealed class HttpApiRequestToolDefinition : IAIToolInstanceDefinition
{
    public string Name => "http-api-request";

    public AITool CreateTool(AIToolInstanceToolContext context)
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

The returned tool is an ordinary `Microsoft.Extensions.AI.AIFunction`. Read the settings the user captured, resolve any services you need from `AIFunctionArguments.Services`, and only accept the open arguments you allow the model to supply.

### Persisting settings on the instance

`AIToolInstance` extends the framework's [extensible entity](extensible-entity), so a definition persists its own strongly typed settings model in the instance's properties:

```csharp
// When saving (UI/controller):
instance.Put(new HttpApiRequestToolSettings { BaseUrl = "https://api.example.com", ... });

// When building the tool (definition):
instance.TryGet<HttpApiRequestToolSettings>(out var settings);
```

### Protecting secrets

Never store credentials in plain text. Protect them with ASP.NET Core Data Protection when saving and unprotect them at invocation time:

```csharp
var protector = provider.CreateProtector("MyDefinition.Secrets");
var stored = protector.Protect(userSuppliedSecret);      // when saving
var secret = protector.Unprotect(stored);                // inside the tool
```

The built-in HTTP tool follows this pattern: on edit, a blank secret reuses the previously stored value, and `Unprotect` tolerates config-seeded plain values.

## Registering a Definition

```csharp
using Microsoft.Extensions.Localization;

builder.Services
    .AddAIToolInstanceDefinition<HttpApiRequestToolDefinition>("http-api-request")
        .WithDisplayName(new LocalizedString("HTTP API Request", "HTTP API Request"))
        .WithDescription(new LocalizedString(
            "HTTP API Request Description",
            "Call an external HTTP API with a preconfigured endpoint, method, authentication, and headers."))
        .WithCategory("Integrations");
```

`AddAIToolInstanceDefinition` calls `AddCoreAIToolInstances()` for you, registers the definition as a keyed service (keyed by name), and records its display metadata in `AIToolInstanceDefinitionOptions`.

| Builder method | Use |
|---|---|
| `.WithDisplayName(...)` | Friendly name shown when choosing a definition |
| `.WithDescription(...)` | Explains what the definition does |
| `.WithCategory(...)` | UI grouping |

## Built-In HTTP API Request Tool

The framework ships a ready-to-use definition that calls arbitrary HTTP APIs. Register it with a single call:

```csharp
builder.Services.AddApiRequestToolInstance();
```

This registers the `http-api-request` definition plus its named `HttpClient`. Each instance captures:

| Setting | Purpose |
|---|---|
| `BaseUrl` | The endpoint the request targets. |
| `HttpMethod` | `GET`, `POST`, `PUT`, `PATCH`, or `DELETE`. |
| `AuthenticationType` | `None`, `ApiKey`, `Bearer`, or `Basic`. |
| `ApiKey` / `ApiKeyHeaderName` | API-key auth (header defaults to `X-Api-Key`). |
| `BearerToken` | Bearer auth (`Authorization: Bearer …`). |
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

1. Choose a **definition** (for example, *HTTP API Request*).
2. Enter a **display name** and a **description**. The description is the primary signal the model uses to tell instances apart, so make it specific — e.g. *"Looks up order status from the Orders API."*
3. Fill in the definition-specific settings (endpoint, auth, headers, …).
4. Save.

Repeat to add **multiple instances of the same definition** — each with different settings and its own description. For example, one instance calls the Orders API and another calls the Weather API; both use the same `http-api-request` definition but appear to the model as two separate functions.

## Attaching Instances to a Profile

Instances only reach the model when a profile references them. The sample hosts add a checkbox section on the AI profile Create/Edit pages; the selected instance IDs are stored via `AIProfileToolInstanceMetadata`:

```csharp
profile.Alter<AIProfileToolInstanceMetadata>(metadata =>
{
    metadata.InstanceIds = selectedInstanceIds;
});
```

At completion time, `AIToolInstanceCompletionContextBuilderHandler` copies those IDs onto `AICompletionContext.ToolInstanceIds`, and the registry provider surfaces each as a distinct tool.

## Persistence

The `AIToolInstance` catalog is registered automatically with your store provider when you register the AI stores:

- **YesSql** — `AddCoreAIServicesStoresYesSql()` registers the catalog and the `AIToolInstanceIndex`. Create the index table during startup with `CreateAIToolInstanceIndexSchemaAsync()`.
- **Entity Framework Core** — `AddCoreAIServicesStoresEntityCore()` registers the source-document catalog.

No extra wiring is required beyond registering the store suite; only the definition (`AddApiRequestToolInstance()` or your own) and the management UI are app-specific.

## Testing

Because a definition produces an ordinary `AIFunction`, you can test it in isolation. Supply services (such as `IHttpClientFactory`) through `AIFunctionArguments.Services`:

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

To verify that multiple instances surface distinctly, build a service provider with the definition registered plus an `ISourceCatalog<AIToolInstance>` returning two instances, then assert `ToolInstanceRegistryProvider.GetToolsAsync` returns two entries with distinct `Name` and `Description`.

:::tip
The sample projects (`CrestApps.Core.Mvc.Web` and `CrestApps.Core.Blazor.Web`) register `AddApiRequestToolInstance()` and include the full management UI. Run the Aspire host and add two HTTP API instances to see them appear to the model as separate functions.
:::

## Related

- [Custom AI Tools](tools) — tools whose arguments are always supplied by the model.
- [Extensible Entity](extensible-entity) — how instance settings are persisted.
- The Orchard Core CMS integration builds on the same abstractions; see the downstream product docs at [orchardcore.crestapps.com](https://orchardcore.crestapps.com).
