---
sidebar_label: Tool Instances
sidebar_position: 9
title: Parameterized Tool Instances
description: Let users configure reusable tool instances with their own endpoints, credentials, and settings that the AI model invokes on demand.
---

# Parameterized Tool Instances

> Author a tool **source** once in code, then let users create multiple configured **instances** of it from the UI. Each instance supplies the parameters (endpoint, authentication, headers, …) and a natural-language description up front; the AI model only decides *when* to invoke each instance.

## Quick Start

Enable the feature on the AI suite builder with `AddToolInstances(...)`, then register one or more **sources** (reusable blueprints) inside it. Each source is a class implementing `IAIToolInstanceSource`, registered under a unique name with `AddSource<TSource>()`:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddYesSqlStores()
        // Enable the tool instances feature and register your sources.
        .AddToolInstances(toolInstances => toolInstances
            // Register a persistence store for the instances users create (required).
            .AddYesSqlStores()
            .AddSource<MyToolInstanceSource>("my-source", options =>
            {
                options.DisplayName = new LocalizedString("my-source", "My Source");
                options.Description = new LocalizedString("my-source", "What this source does.");
                options.Category = new LocalizedString("Integrations", "Integrations");
            })
        )
    )
);
```

The framework also ships a built-in source — the [HTTP API Request tool](#built-in-http-api-request-tool) — as a ready-made example. Add it with the `AddHttpApiRequestSource()` convenience, which registers its named `HttpClient` and the source with sensible default display metadata:

```csharp
.AddToolInstances(toolInstances => toolInstances
    .AddYesSqlStores()
    .AddHttpApiRequestSource()
)
```

Either way, once a source is registered, users create instances in the management UI, give each a unique **name** and a clear **description**, and attach one or more instances to an AI profile or a chat interaction. Each instance appears to the model as a distinct callable function.

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

1. A developer registers the feature with `AddToolInstances(...)` and one or more **sources** with `AddSource<TSource>(name, configure)`.
2. A user creates one or more **instances** from that source, each with a unique name, a description, and its own stored settings. The name is the stable lookup key and is fixed once the instance is created — the management UI keeps it editable only on create.
3. The user attaches instances to an AI profile or a chat interaction (via `AIToolInstanceMetadata`).
4. During completion, `ToolInstanceRegistryProvider` materializes each referenced instance into a distinct `AITool` whose function name and description are unique per instance.
5. The resulting `AITool` flows into `ChatOptions.Tools` through `Microsoft.Extensions.AI`, so **every client (OpenAI, Azure OpenAI, …) works with no client-specific code**.

Distinct per-instance function names are produced by `AIToolInstance.GetFunctionName()`, which sanitizes the instance's unique `Name` to the characters chat-completion providers allow. When sanitizing or truncating to 64 characters would change the value, a short deterministic hash of the original name is appended so two distinct names can never collapse to the same function name.

## Authoring a Source

Authoring your own source is the core extension point — the built-in HTTP tool below is authored exactly this way. Implement `IAIToolInstanceSource` and its single `CreateTool` method. `CreateTool` receives the configured `AIToolInstance`, reads its stored settings, and returns an `AITool` bound to them. Derive the model-facing function name from `instance.GetFunctionName()` and the description from `instance.Description` so the instance surfaces distinctly:

```csharp
using CrestApps.Core;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

public sealed class HttpApiRequestToolInstanceSource : IAIToolInstanceSource
{
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // Read the settings the user stored on the instance.
        var settings = instance.TryGet<HttpApiRequestToolSettings>(out var stored)
            ? stored
            : new HttpApiRequestToolSettings();

        // The function name and description are unique per instance so the model can tell them apart.
        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? functionName
            : instance.Description;

        // Pass the instance so the tool can cache state (for example OAuth 2.0 tokens) on it.
        return new HttpApiRequestToolFunction(functionName, description, settings, instance);
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

Register sources through the `AddToolInstances(...)` feature builder on the AI suite. Call `AddSource<TSource>(name, configure)` for each source:

```csharp
.AddToolInstances(toolInstances => toolInstances
    .AddYesSqlStores()
    .AddSource<HttpApiRequestToolInstanceSource>(
        HttpApiRequestToolConstants.SourceName,
        options =>
        {
            options.DisplayName = new LocalizedString(HttpApiRequestToolConstants.SourceName, "HTTP API Request");
            options.Description = new LocalizedString(HttpApiRequestToolConstants.SourceName, "Calls an external HTTP API.");
            options.Category = new LocalizedString("Integrations", "Integrations");
        })
)
```

`AddToolInstances(configure, useDefaultRegistry = true)` does the following:

- registers the core services (the catalog handler and the completion-context builder handler);
- when `useDefaultRegistry` is `true` (the default), registers the built-in `ToolInstanceRegistryProvider`. Pass `useDefaultRegistry: false` to opt out and supply [your own registry provider](#custom-tool-registry-providers) instead;
- invokes the `configure` delegate so you can register sources and a persistence store on the builder.

The feature does **not** register a persistence store for you. Call `AddYesSqlStores()` or `AddEntityCoreStores()` on the tool-instances builder so the instances users create are saved.

Each `AddSource<TSource>(name, configure)`:

- registers the source as a **keyed** scoped `IAIToolInstanceSource`, keyed by `name` (the value stored as each instance's `Source`);
- records the source's display metadata in `AIOptions.ToolInstanceSources[name]` via the `configure` delegate.

| Configure property | Use |
|---|---|
| `DisplayName` | Friendly name shown when choosing a source (`LocalizedString`). |
| `Description` | Explains what the source does (`LocalizedString`). |
| `Category` | UI grouping (`LocalizedString`). |

If `DisplayName` is left empty it defaults to the registered `name`.

## Built-In HTTP API Request Tool

The framework ships a ready-to-use source that calls arbitrary HTTP APIs. Add it with the `AddHttpApiRequestSource()` convenience, which registers its named `HttpClient` and the source:

```csharp
.AddToolInstances(toolInstances => toolInstances
    .AddYesSqlStores()
    .AddHttpApiRequestSource()
)
```

`AddHttpApiRequestSource(configure)` applies default display metadata; pass an optional `configure` delegate to override the display name, description, or category.

Each instance captures:

| Setting | Purpose |
|---|---|
| `BaseUrl` | The endpoint the request targets. |
| `HttpMethod` | `GET`, `POST`, `PUT`, `PATCH`, or `DELETE`. |
| `AuthenticationType` | `None`, `ApiKey`, `Bearer`, `Basic`, or `OAuth2`. |
| `ApiKey` / `ApiKeyHeaderName` | API-key auth (header defaults to `X-Api-Key`). |
| `BearerToken` | Bearer auth (`Authorization: Bearer …`). |
| `Username` / `Password` | HTTP basic auth, or the resource-owner credentials for the OAuth 2.0 password grant. |
| `TokenEndpoint` / `ClientId` / `ClientSecret` / `Scope` | OAuth 2.0 settings used to obtain (and refresh) a token automatically. |
| `DefaultHeaders` | Static headers always added. |
| `AllowModelProvidedPath` / `…Query` / `…Body` | Which open arguments the model may supply. |
| `TimeoutSeconds` | Optional per-request timeout. |

Credentials (`ApiKey`, `BearerToken`, `Password`, `ClientSecret`) are data-protected at rest with the `HttpApiRequestToolConstants.DataProtectionPurpose` purpose.

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

### OAuth 2.0 token caching

When an instance uses `AuthenticationType = OAuth2`, the tool obtains an access token from the configured `TokenEndpoint` the first time it runs and **caches it on the instance** so subsequent calls do not re-authenticate:

1. Before each request the tool reads the cached `HttpApiRequestTokenState` from the instance (via `TryGet`). If a non-expired access token is present, it is reused.
2. Otherwise it requests a new token. If a refresh token was previously stored, it first tries `grant_type=refresh_token`. Otherwise, when a `Username` is configured it uses the resource-owner `grant_type=password` (sending `Username` / `Password`); when no username is configured it falls back to `grant_type=client_credentials` using `ClientId` / `ClientSecret` / `Scope`.
3. The returned access token, refresh token, token type, and expiry are data-protected and persisted back onto the instance with `Put(...)`, then saved through the catalog so the cache survives across requests and restarts.

The access and refresh tokens are protected at rest with the same `HttpApiRequestToolConstants.DataProtectionPurpose` purpose as the other credentials. Because the cache lives on the `AIToolInstance` itself, each instance maintains its own independent token, and no token is ever exposed to the model.

:::note
Token and credential protection depends on a registered `IDataProtectionProvider` (ASP.NET Core apps have one by default). If no provider is resolvable, the source degrades gracefully and stores the values unprotected, so make sure data protection is configured in production.
:::

## How Clients Invoke Instances

Instances are **provider-agnostic** — there is no OpenAI-, Azure OpenAI-, or Azure AI Inference-specific code anywhere in the flow:

1. `ToolInstanceRegistryProvider` turns each referenced instance into an ordinary `Microsoft.Extensions.AI.AIFunction` (via its source's `CreateTool`), with a unique per-instance `Name`, `Description`, and JSON schema of the open arguments.
2. Those functions are placed on `ChatOptions.Tools`.
3. Every supported client — OpenAI, Azure OpenAI, Azure AI Inference, Ollama, … — is a `Microsoft.Extensions.AI` `IChatClient`. The client serializes each `AIFunction`'s name, description, and schema into that provider's native "tools"/"functions" wire format.
4. When the model decides to call one, it returns a function-call naming the instance and supplying **only the open arguments** (for example `path`, `query`, `body`). The `FunctionInvokingChatClient` matches the name and calls `AIFunction.InvokeAsync(...)`.
5. The tool merges the model-supplied open arguments with the instance's **stored settings** (endpoint, authentication, headers) — which the model never sees — and performs the request.

This is why the same instance works identically across all clients: the model only ever sees names, descriptions, and open-argument schemas, while the fixed configuration and secrets stay on the instance.

In the sample hosts, open **AI Tool Instances**, then:

1. Choose a **source** (for example, *HTTP API Request*).
2. Enter a unique **technical name** and a **description**. The name becomes the function name exposed to the model, and the description is the primary signal the model uses to tell instances apart, so make it specific — e.g. *"Looks up order status from the Orders API."*
3. Fill in the source-specific settings (endpoint, auth, headers, …).
4. Save.

Repeat to add **multiple instances from the same source** — each with a different name, settings, and description. For example, one instance calls the Orders API and another calls the Weather API; both use the same `http-api-request` source but appear to the model as two separate functions.

## Attaching Instances to a Profile or Chat Interaction

Instances only reach the model when a profile or chat interaction references them. The sample hosts add a checkbox section on both the AI profile and chat interaction Create/Edit pages; the selected instance **names** are stored via `AIToolInstanceMetadata` — a generic metadata type usable by any resource, including both AI profiles and chat interactions:

```csharp
// The same call works for an AIProfile or a ChatInteraction.
resource.Alter<AIToolInstanceMetadata>(metadata =>
{
    metadata.ToolInstanceNames = selectedInstanceNames;
});
```

At completion time, `AIToolInstanceCompletionContextBuilderHandler` reads `AIToolInstanceMetadata` from the resource (any extensible entity) and copies those names onto `AICompletionContext.ToolInstanceNames`. The registry provider then looks each one up by name and surfaces it as a distinct tool. Because the handler works off the shared metadata rather than a specific resource type, the same instances are honored across every orchestrator.

## Custom Tool Registry Providers

The default `ToolInstanceRegistryProvider` surfaces every referenced instance to the model unconditionally. Real applications often need extra logic — most commonly a **permission check** so an instance is only exposed to users who are allowed to use it.

Tools reach the model through the aggregated `IToolRegistryProvider` abstraction. Any number of providers can be registered; the registry concatenates the tools returned by each.

For simple per-instance gating you do not have to write a provider from scratch. `ToolInstanceRegistryProvider` is `public` and exposes a `protected virtual ShouldIncludeInstanceAsync(instance, context, cancellationToken)` hook that runs for every referenced instance before it is surfaced. Subclass it and return `false` to hide an instance:

```csharp
public sealed class PermissionAwareToolInstanceRegistryProvider : ToolInstanceRegistryProvider
{
    private readonly IAuthorizationService _authorization;

    public PermissionAwareToolInstanceRegistryProvider(
        INamedCatalog<AIToolInstance> catalog,
        IServiceProvider services,
        IAuthorizationService authorization)
        : base(catalog, services)
    {
        _authorization = authorization;
    }

    protected override async ValueTask<bool> ShouldIncludeInstanceAsync(
        AIToolInstance instance,
        AICompletionContext context,
        CancellationToken cancellationToken)
        => await IsAuthorizedAsync(instance, cancellationToken);
}
```

Register your subclass in place of the default (see [opting out](#opting-out-of-the-default-provider) below).

If you need full control over how entries are built, implement `IToolRegistryProvider` directly instead:

```csharp
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;

public sealed class PermissionAwareToolRegistryProvider : IToolRegistryProvider
{
    private readonly INamedCatalog<AIToolInstance> _catalog;
    private readonly IAuthorizationService _authorization;
    // ... resolve the current user, sources, etc.

    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var names = context?.ToolInstanceNames;

        if (names is null || names.Length == 0)
        {
            return [];
        }

        var entries = new List<ToolRegistryEntry>();

        foreach (var name in names)
        {
            var instance = await _catalog.FindByNameAsync(name, cancellationToken);

            if (instance is null)
            {
                continue;
            }

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

### Opting Out of the Default Provider

Register your provider and **opt out of the default one** so the built-in provider does not also surface ungated tools. Do this by passing `useDefaultRegistry: false` to `AddToolInstances`, then registering your provider explicitly:

```csharp
// Enable the feature WITHOUT the default registry provider, and register your store and sources.
.AddToolInstances(toolInstances => toolInstances
    .AddYesSqlStores()
    .AddHttpApiRequestSource(),
    useDefaultRegistry: false)

// ...then register only your gated provider.
builder.Services.AddScoped<IToolRegistryProvider, PermissionAwareToolRegistryProvider>();
```

:::warning
Do **not** call `services.RemoveAll<IToolRegistryProvider>()` to swap the default provider. `IToolRegistryProvider` is an aggregated abstraction — other framework features register their own providers, and removing all of them strips out those tools too. Use the `useDefaultRegistry: false` opt-out instead, and if you also want the built-in behavior alongside yours, simply leave `useDefaultRegistry` at its default (`true`) and add your provider in addition.
:::

This is exactly how downstream products layer their own authorization on top of the same abstractions — for example, the Orchard Core CMS integration ships a `LocalToolRegistryProvider` that checks per-instance permissions before exposing each tool.

## Persistence

Tool instances are stored through the `AIToolInstance` catalog. Because the feature does not register a store on its own, register one on the **tool-instances** builder — not on the AI suite — so it matches the provider your app already uses:

```csharp
.AddToolInstances(toolInstances => toolInstances
    .AddYesSqlStores()          // or .AddEntityCoreStores()
    .AddHttpApiRequestSource()
)
```

- **YesSql** — `AddYesSqlStores()` registers the catalog and the `AIToolInstanceIndex`. Create the index table during startup with `CreateAIToolInstanceIndexSchemaAsync()`.
- **Entity Framework Core** — `AddEntityCoreStores()` registers the source-document catalog.

Only the store, the source (`AddSource<TSource>(...)`), and the management UI are app-specific.

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

To verify that multiple instances surface distinctly, build a service provider with the source registered as a keyed `IAIToolInstanceSource` plus an `INamedCatalog<AIToolInstance>` whose `FindByNameAsync` returns two instances, then assert `ToolInstanceRegistryProvider.GetToolsAsync` (with `context.ToolInstanceNames` set to the two names) returns two entries with distinct `Name` and `Description`.

:::tip
The sample projects (`CrestApps.Core.Mvc.Web` and `CrestApps.Core.Blazor.Web`) register the `http-api-request` source and include the full management UI. Run the Aspire host and add two HTTP API instances to see them appear to the model as separate functions.
:::

## Related

- [Custom AI Tools](tools) — tools whose arguments are always supplied by the model.
- [Extensible Entity](extensible-entity) — how instance settings are persisted.
- The Orchard Core CMS integration builds on the same abstractions; see the downstream product docs at [orchardcore.crestapps.com](https://orchardcore.crestapps.com).
