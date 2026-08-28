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

:::note
The examples above use YesSql, but the feature works with Entity Framework Core too — swap `AddYesSqlStores()` for `AddEntityCoreStores()`. See [Persistence](#persistence) for complete examples of both providers.
:::

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

Distinct per-instance function names are produced by `AIToolInstance.GetFunctionName()`, which sanitizes the instance's unique `Name` to the characters chat-completion providers allow and prefixes it with `AIToolInstanceExtensions.FunctionNamePrefix` (`tool_instance_`). The prefix guarantees a user-chosen instance name can never collide with a tool registered in code via `AddCoreAITool` — those are surfaced to the model under their bare registered name, so both kinds of tool coexist in the single function namespace the model sees without clashing. When sanitizing or truncating to 64 characters would change the value, a short deterministic hash of the original name is appended so two distinct names can never collapse to the same function name.

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

The returned tool is an ordinary `Microsoft.Extensions.AI.AIFunction`. Read the configuration the user captured, resolve any services you need from `AIFunctionArguments.Services`, and only accept the open arguments you allow the model to supply. The source's *display* metadata (display name, description, category) is provided at registration time — not on the class — so no separate options or builder types are required.

### Persisting metadata on the instance

`AIToolInstance` extends the framework's [extensible entity](extensible-entity), so a source persists its own strongly typed configuration as metadata on the instance:

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

## Parameters

By default the arguments a tool instance exposes are whatever its source hardcodes — for the HTTP source, an optional path, an untyped `query` bag, and an open-ended `body`. That leaves the model guessing which query keys the API actually accepts.

**Parameters** let the person configuring the instance declare exactly what the tool takes. Each parameter carries a name, a type, a description, and two decisions that make it work:

| Decision | Question it answers |
|---|---|
| **Fill** | Who supplies the value — the model, a value you pin, or the request context. |
| **Binding** | Where the resolved value goes — query string, path, header, body field. |

Both halves matter. The declaration alone would only tell the model what to send; the binding is what makes the value arrive.

### Parameter support is opt-in per source

The framework can always declare a parameter in the schema and resolve its value, but only the source can place that value into the call it makes. A source therefore advertises what it can honor at registration time:

```csharp
builder.AddSource<MyToolInstanceSource>("my-source", entry =>
{
    entry.DisplayName = new LocalizedString("my-source", "My Source");
    entry.Parameters = new AIToolInstanceParameterCapabilities
    {
        ReservedNames = ["query"],
        Bindings =
        [
            new AIToolParameterBindingOption("Query")
            {
                DisplayName = new LocalizedString("Query", "Query string parameter"),
            },
        ],
    };
});
```

Declaring capabilities is all a source has to do to get the management editor: the parameter UI in both sample hosts is driven entirely by what the source advertises, so a new source needs no view code of its own. See [Adding parameter support to your own source](#adding-parameter-support-to-your-own-source).

A source that declares no capabilities does not support parameters. The management UI hides the parameter editor for it, and saving an instance with parameters against it **fails validation**. That is deliberate: a parameter declared in the schema, filled by the model, and then ignored by the source would produce a call that silently omits the value while the model reports success — worse than not offering the feature at all.

### Fill modes

| Fill | In the schema? | Value comes from |
|---|---|---|
| `Model` | Yes | The AI model, falling back to the configured default when omitted. |
| `Fixed` | No | A value pinned by the user configuring the instance. |
| `Context` | No | An `IAIToolParameterContextResolver`, resolved per invocation. |

Only `Model` parameters are emitted into the function schema. Fixed and context values are never shown to the model — a model that can see a value will try to fill it, which would defeat the point of pinning or injecting it. A model-supplied argument that happens to share a fixed or context parameter's name is discarded, not merged.

Context parameters are what let a tool instance safely call a per-user API. Bind `userId` to `user.id` and every call carries the caller's real identifier, injected server-side, with no way for a prompt-injected model to substitute somebody else's:

| Key | Resolves to |
|---|---|
| `user.id` / `user.name` / `user.email` | The current `ClaimsPrincipal`. |
| `resource.id` | The chat interaction or profile driving the completion. |
| `now.utc` | The current UTC time, round-trip format. |

Register your own `IAIToolParameterContextResolver` to add keys, or to override a built-in one — resolvers are tried in registration order and the first to handle a key wins.

### Defaults, types, and validation

Values arrive as loosely typed JSON, so a declared type is enforced by coercion: a model that sends `"3"` for an integer is accepted, one that sends `"3.5"` is not. `AllowedValues` becomes the schema `enum` and is re-checked at invocation.

Defaults are applied **server-side by the binder**, not emitted as a JSON Schema `default` — providers do not reliably honor schema defaults and drop them entirely under strict mode. The default is mentioned in the property description so the model knows what omitting it means.

When a parameter cannot be resolved — a required value the model omitted, a value of the wrong type, a value outside the allowed set — the tool returns an error describing the problem instead of throwing, so the model can correct the call and retry.

### The built-in HTTP source

The HTTP source accepts four placements:

| Binding | Effect |
|---|---|
| `Query:<key>` | Appended to the URL as `?key=value`. |
| `Path` | Substitutes the matching `{token}` in the new `PathTemplate` setting. |
| `Body:<dotted.path>` | Written into the JSON body, creating intermediate objects as needed. |
| `Header:<name>` | Sent as a request header. **Model-filled values are refused.** |

A model-controlled header is a request-smuggling vector and is almost never intended, so the `Header` placement accepts only `Fixed` and `Context` fills — enforced in validation and disabled in the editor.

Path and query values are escaped before they reach the URL, so a model-supplied value can neither add path segments nor traverse upwards; the existing same-host check remains as the second line of defense. Header values containing a line break are dropped rather than allowed to split the request.

Binding a parameter to a placement **closes the corresponding free-form argument** — bind anything to `Query` and the untyped `query` bag disappears from the schema. A typed, described parameter is strictly better than an untyped bag, and closing the open arguments is what makes strict mode reachable: when every argument is a required scalar parameter, the function turns on provider strict schema validation automatically.

A worked example — `POST https://api.example.com/v1/orders/{orderId}` with a body field, a context-injected header, and an optional flag:

| Name | Type | Fill | Binding | Notes |
|---|---|---|---|---|
| `orderId` | String | Model | `Path` | Required; fills the `orders/{orderId}` template. |
| `status` | String | Model | `Body:order.status` | Required; allowed values `open, shipped`. |
| `notify` | Boolean | Model | `Query:notify` | Optional, defaults to `false`. |
| `actingUser` | String | Context | `Header:X-Acting-User` | Resolved from `user.id`. |

### Adding parameter support to your own source

Supporting parameters takes two steps, and **both are required**. Declaring alone gives users an editor whose values your tool ignores; consuming alone means the editor never appears.

#### 1. Declare what you can honor

Set `Parameters` on the registration entry. This one declaration drives the entire management experience — you write no UI code:

```csharp
.AddSource<MyToolInstanceSource>("my-source", entry =>
{
    entry.DisplayName = new LocalizedString("my-source", "My Source");

    entry.Parameters = new AIToolInstanceParameterCapabilities
    {
        // Argument names your tool builds itself. A parameter may not shadow one.
        ReservedNames = ["query"],
        Hint = new LocalizedString("my-source", "Define what this source accepts."),
        Bindings =
        [
            new AIToolParameterBindingOption("Argument")
            {
                DisplayName = new LocalizedString("Argument", "Call argument"),
                Hint = new LocalizedString("Argument", "Passed to the downstream call by name."),
            },
            new AIToolParameterBindingOption("Credential")
            {
                DisplayName = new LocalizedString("Credential", "Credential"),

                // The model must never be able to fill this one.
                AllowedFills = [AIToolParameterFill.Fixed, AIToolParameterFill.Context],
            },
        ],
    };
});
```

Everything the editor shows comes from this declaration:

| Property | Effect in the management UI |
|---|---|
| `Bindings` | The placements offered in the **Send as** dropdown. Declaring none disables parameter support entirely. |
| `ReservedNames` | Names rejected inline as you type, because your tool already uses them. |
| `Hint` | Explanatory text shown above the parameter list. |
| `AIToolParameterBindingOption.DisplayName` | The option label. |
| `AIToolParameterBindingOption.Hint` | Help text shown under the dropdown when the placement is selected. |
| `AIToolParameterBindingOption.AllowedFills` | Fill modes the placement accepts. Others are shown disabled, and rejected on save. |
| `AIToolParameterBindingOption.RequiresValue` | Forces a model-filled parameter here to be required or carry a default — for placements like a URL path token that have no sensible empty form. |
| `AIToolParameterBindingOption.SupportsTargetName` | Whether the placement takes a target name distinct from the parameter name. |

With that in place your source gets the full editor in both sample hosts — typed rows, fill modes, required and default values, allowed-value sets, secret handling, context keys, inline name validation, and save-time validation via `AIToolParameterValidator`. Nothing is per-source in the editors themselves.

:::note
The editors' **request preview** is the one part written for the built-in HTTP source: it recognises the placement names `Query`, `Path`, `Header`, and `Body`. A source using different placement names gets a fully working editor, but the preview panel will not illustrate its placements.
:::

#### 2. Consume them in your tool

Read the declared parameters in the constructor, merge them into your schema, then resolve them at invocation:

```csharp
public sealed class MyToolFunction : AIFunction
{
    private readonly IReadOnlyList<AIToolInstanceParameter> _parameters;
    private readonly JsonElement _jsonSchema;

    public MyToolFunction(AIToolInstance instance)
    {
        _parameters = AIToolParameterBinder.GetParameters(instance);

        // Pass your own base schema, or null when the tool has no arguments of its own.
        _jsonSchema = AIToolParameterSchemaBuilder.Merge(BuildBaseSchema(), _parameters);
    }

    public override JsonElement JsonSchema => _jsonSchema;

    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var resolution = AIToolParameterBinder.Resolve(_parameters, arguments, arguments.Services);

        if (!resolution.Succeeded)
        {
            // Reported to the model so it can correct the call, rather than thrown.
            return JsonSerializer.Serialize(new { success = false, error = string.Join(" ", resolution.Errors) });
        }

        foreach (var resolved in resolution.ForTarget("Argument"))
        {
            // resolved.Binding.Name is the target name; resolved.StringValue is the value,
            // and resolved.Value keeps the coerced type.
        }

        // ...
    }
}
```

`Merge` emits only the parameters the model is meant to fill, and `Resolve` applies precedence, type coercion, allowed-value checks, and defaults for you. Pass an `unprotect` delegate as the fourth argument to `Resolve` when your source stores secret fixed values under its own data-protection purpose.

Optionally call `AIToolParameterSchemaBuilder.IsStrictEligible(hasSourceArguments, parameters)` and surface the result as the `Strict` entry of `AdditionalProperties`, so a fully declared function opts into provider strict schema validation.

:::warning
Nothing forces a source that declares capabilities to actually call the binder. If you declare placements but skip step 2, the model will be shown parameters it fills and your tool will ignore them — the exact silent failure the opt-in design exists to prevent. A test that asserts your function's `JsonSchema` and then asserts the resolved values reach the call is the cheapest way to keep the two halves honest.
:::

### Storage

Parameters live in the instance's properties bag as `AIToolInstanceParametersMetadata`, so they travel with the instance like any other setting. An instance that declares none produces exactly the schema it did before parameters existed, so existing instances are unaffected.

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
| `PathTemplate` | Optional path appended to the base URL, with `{token}` placeholders filled by path-bound [parameters](#parameters). |
| `HttpMethod` | `GET`, `POST`, `PUT`, `PATCH`, or `DELETE`. |
| `AuthenticationType` | `None`, `ApiKey`, `Bearer`, `Basic`, or `OAuth2`. |
| `ApiKey` / `ApiKeyHeaderName` | API-key auth (header defaults to `X-Api-Key`). |
| `BearerToken` | Bearer auth (`Authorization: Bearer …`). |
| `Username` / `Password` | HTTP basic auth, or the resource-owner credentials for the OAuth 2.0 password grant. |
| `TokenEndpoint` / `ClientId` / `ClientSecret` / `Scope` | OAuth 2.0 settings used to obtain (and refresh) a token automatically. |
| `DefaultHeaders` | Static headers always added. |
| `AllowModelProvidedPath` / `…Query` / `…Body` | Which open arguments the model may supply. Binding a [parameter](#parameters) to a placement closes the matching open argument. |
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

## Creating Instances in the Sample Hosts

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

This is exactly how downstream products layer their own authorization on top of the same abstractions — for example, the Orchard Core CMS integration ships a `LocalToolRegistryProvider` that checks per-instance permissions before exposing each tool. Because `LocalToolRegistryProvider` builds its entries from the same `IAIToolInstanceSource`, instances it surfaces honor declared [parameters](#parameters) with no extra work; the CMS ships its own instance editor for authoring them.

## Persistence

Tool instances are stored through the `AIToolInstance` catalog. Because the feature does not register a store on its own, register one on the **tool-instances** builder — not on the AI suite — so it matches the provider your app already uses. Register the same provider you use for the rest of the AI suite.

### YesSql

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddYesSqlStores()
        .AddToolInstances(toolInstances => toolInstances
            .AddYesSqlStores()
            .AddHttpApiRequestSource()
        )
    )
);
```

`AddYesSqlStores()` registers the catalog and the `AIToolInstanceIndex`. Create the index table during startup with `CreateAIToolInstanceIndexSchemaAsync()`.

### Entity Framework Core

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddEntityCoreStores()
        .AddToolInstances(toolInstances => toolInstances
            .AddEntityCoreStores()
            .AddHttpApiRequestSource()
        )
    )
    .AddEntityCoreSqliteDataStore("Data Source=App_Data\\crestapps.db")
);
```

`AddEntityCoreStores()` registers the source-document catalog for `AIToolInstance`; the schema is created and migrated by the Entity Framework Core data store, so no extra index step is required.

Only the store, the source (`AddSource<TSource>(...)`), and the management UI are app-specific. The `CrestApps.Core.Mvc.Web` sample uses YesSql and `CrestApps.Core.Blazor.Web` uses Entity Framework Core, so each provider has a working reference host.

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
- [Parameters](#parameters) — declaring typed arguments, pinned values, and context-injected values on an instance.
- [Extensible Entity](extensible-entity) — how instance settings are persisted.
- The Orchard Core CMS integration builds on the same abstractions; see the downstream product docs at [orchardcore.crestapps.com](https://orchardcore.crestapps.com).
