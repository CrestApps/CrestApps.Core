---
sidebar_label: Architecture
sidebar_position: 2
title: AI Provider Architecture
description: Capability-based provider model, shared client factory, and credential strategy for CrestApps.Core AI providers.
---

# AI Provider Architecture

> **Status:** Implemented. The capability-flag model (`AIProviderCapability`), the consolidated `IAIProvider` contract, `ProviderBase`, and the deployment-type enforcement inside `DefaultAIClientFactory` ship today. The credential-resolver pipeline (`IProviderCredentialResolver` / `IProviderCredentialResolverSelector` / `ResolvedCredential.Fingerprint`), the abstract `ProviderClientFactory<TOptions, TClient>` base, and the `AddCrestAppsAIProvider<T>` registration helper are **deferred to a future pass** — providers continue to read credentials inline, keep their own `BoundedClientCache<TClient>`, and register themselves manually.

## Why change

The current provider model (`IAIClientProvider` + `AIClientProviderBase`) has served well, but a few things have become friction points:

1. **Implicit capability fan-out.** Every provider must implement five `Get*ClientAsync` methods, even when the underlying SDK has no concept of (e.g.) image generation or speech-to-text. Today providers throw `NotSupportedException` from the unsupported methods. That works, but callers can only discover the lack of support by *trying and catching*. There is no way to ask a provider "do you do embeddings?" up front.
2. **Per-provider boilerplate.** Each provider re-implements the same four moving parts: a `BoundedClientCache<TClient>`, a cache-key derived from `(endpoint, apiKey)`, a deployment-name fallback (`Get*DeploymentName` from the connection bag), and a credential read (`connection.GetApiKey()` / `connection.GetEndpoint()`). The shape is identical across OpenAI, Azure OpenAI, Claude, DeepSeek, Mistral, Google, and Bedrock.
3. **Credential handling is ad-hoc.** Some providers use API keys, some use Azure managed identities, some use AWS SigV4. The current pattern leaves each provider free to invent its own approach. This makes credential rotation, redaction, and auditing inconsistent.
4. **Provider registration is verbose.** Each provider ships its own `Add*` extension that wires up an `IAIClientProvider`, a connection source, and (for chat) an `IAICompletionClient`. The boilerplate is mechanical and hides the small bits that actually differ per provider.

## Goals

- **Discoverable capabilities.** Callers can ask `provider.Supports(AIProviderCapability.Embeddings)` without invoking the underlying SDK.
- **Consolidated infrastructure.** A single shared `ProviderClientFactory<TOptions, TClient>` handles caching, key derivation, and credential resolution. Providers contribute a small "build the SDK client from these options" delegate plus the per-capability adapter methods.
- **Consistent credential strategy.** A `IProviderCredentialResolver` abstraction normalises API key / Azure AD / AWS / OAuth flows. Providers declare the credential kinds they accept; the resolver does the rest.
- **Smaller `Add*` surface.** A common `services.AddCrestAppsAIProvider<TProvider>()` extension wires the standard registrations. Providers only override what is genuinely different.
- **Backwards compatible during the migration.** The legacy `IAIClientProvider` keeps working until every in-tree provider has been ported; the new model is additive first.

## Non-goals

- Replacing `Microsoft.Extensions.AI.IChatClient` (we will keep delegating to the official `IChatClient` / `IEmbeddingGenerator<,>` / `IImageGenerator` / `ISpeechToTextClient` / `ITextToSpeechClient` types).
- Changing how `AIDeployment` and `AIProviderConnectionEntry` look on the storage side.
- A new credential storage backend — credentials still come from the connection entry (or a `IProviderCredentialResolver` that knows how to read them from elsewhere).
- Migrating to EF Core migrations (covered separately in the data layer plan).

## Proposed shape

### 1. Capability flags

```csharp
namespace CrestApps.Core.AI.Providers;

[Flags]
public enum AIProviderCapability
{
    None         = 0,
    Chat         = 1 << 0,
    Embeddings   = 1 << 1,
    Images       = 1 << 2,
    SpeechToText = 1 << 3,
    TextToSpeech = 1 << 4,
}
```

A `[Flags]` enum lets a provider declare its full surface in one expression and lets call-sites do `provider.Capabilities.HasFlag(AIProviderCapability.Embeddings)`.

#### Mapping to `AIDeploymentType`

`AIDeployment.Type` already encodes the deployment surface (`Chat`, `Utility`, `Embedding`, `Image`, `SpeechToText`, `TextToSpeech`). The provider capability flag is the **provider-level** statement; deployment type is the **deployment-level** statement. The factory must validate both. The mapping is fixed:

| `AIDeploymentType` | Required `AIProviderCapability` |
|---|---|
| `Chat` | `Chat` |
| `Utility` | `Chat` *(utility deployments are chat models with relaxed defaults; no separate provider capability)* |
| `Embedding` | `Embeddings` |
| `Image` | `Images` |
| `SpeechToText` | `SpeechToText` |
| `TextToSpeech` | `TextToSpeech` |

`DefaultAIClientFactory` enforces this on every `Create*Async` call: it asserts both `provider.Supports(capability)` and `deployment.Type` matches the requested capability before invoking the provider. This makes mismatches surface as a deterministic exception at the factory boundary, not as an SDK-side `BadRequest` deep inside a stream.

### 2. `IAIProvider`

```csharp
namespace CrestApps.Core.AI.Providers;

public interface IAIProvider
{
    string Name { get; }

    AIProviderCapability Capabilities { get; }

    IReadOnlySet<CredentialKind> AcceptedCredentialKinds { get; }

    bool Supports(AIProviderCapability capability)
        => (Capabilities & capability) == capability;

    ValueTask<IChatClient>                                 GetChatClientAsync(AIProviderConnectionEntry connection, string deploymentName = null, CancellationToken cancellationToken = default);
    ValueTask<IEmbeddingGenerator<string, Embedding<float>>> GetEmbeddingGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null, CancellationToken cancellationToken = default);
    ValueTask<IImageGenerator>                             GetImageGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null, CancellationToken cancellationToken = default);
    ValueTask<ISpeechToTextClient>                         GetSpeechToTextClientAsync(AIProviderConnectionEntry connection, string deploymentName = null, CancellationToken cancellationToken = default);
    ValueTask<ITextToSpeechClient>                         GetTextToSpeechClientAsync(AIProviderConnectionEntry connection, string deploymentName = null, CancellationToken cancellationToken = default);
    Task<SpeechVoice[]>                                    GetSpeechVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null, CancellationToken cancellationToken = default);
}
```

Notes:

- The `Get*Async` methods stay on the interface (rather than splitting into `IChatProvider`, `IEmbeddingsProvider`, etc.). Splitting would force every consumer to do `provider as IChatProvider` instead of a flag check, and would explode DI registrations. The `Capabilities` flag is the discoverability story; the interface is the *capacity* story.
- A provider that does not declare a capability **must** throw `NotSupportedException` from the corresponding method. The base class enforces this so providers cannot accidentally lie about their flags.
- `CancellationToken` is now part of the contract. The current interface omits it; the new one threads it through (matches the work already done in Phase 5).
- **Lifetime: scoped.** Providers are registered scoped (matching today's `AIClientProviderBase` pattern). They are *thin* — the expensive bits (the cached SDK clients) live on the factory described below, which is registered as a singleton. Providers themselves only adapt the SDK client into `Microsoft.Extensions.AI` types and apply per-request pipeline concerns from the active scope.
- **No raw `IServiceProvider` capture.** The `Microsoft.Extensions.AI` pipeline (`ChatClientBuilder.Build(serviceProvider)`) is now applied by `DefaultAIClientFactory` from the request-scoped service provider, **not** by the provider. Providers return raw SDK adapter clients; the factory layers in middleware/logging/options. This keeps providers free of root-SP capture concerns.
- `AcceptedCredentialKinds` lets the credential resolver/selector know which credential modes the provider can consume. For example, OpenAI returns `{ ApiKey }`; Azure OpenAI returns `{ ApiKey, AzureCredential }`; Bedrock returns `{ AwsCredential }`.

### 3. `ProviderClientFactory<TOptions, TClient>`

```csharp
namespace CrestApps.Core.AI.Providers;

public abstract class ProviderClientFactory<TOptions, TClient>
    where TOptions : class
    where TClient  : class
{
    private readonly BoundedClientCache<TClient> _cache = new();
    private readonly IProviderCredentialResolverSelector _credentialSelector;

    protected ProviderClientFactory(IProviderCredentialResolverSelector credentialSelector)
    {
        _credentialSelector = credentialSelector;
    }

    protected ValueTask<TClient> GetOrCreateAsync(
        string providerName,
        AIProviderConnectionEntry connection,
        string deploymentName,
        AIProviderCapability capability,
        Func<TOptions, TClient> build,
        CancellationToken cancellationToken)
    {
        var resolver = _credentialSelector.Select(providerName, connection);
        var credential = resolver.Resolve(providerName, connection, AcceptedCredentialKinds);
        var options = ReadOptions(connection, deploymentName, capability);
        var key = BuildCacheKey(options, credential);

        return ValueTask.FromResult(_cache.GetOrAdd(key, _ => build(ApplyCredential(options, credential))));
    }

    protected abstract IReadOnlySet<CredentialKind> AcceptedCredentialKinds { get; }
    protected abstract TOptions ReadOptions(AIProviderConnectionEntry connection, string deploymentName, AIProviderCapability capability);
    protected abstract TOptions ApplyCredential(TOptions options, ResolvedCredential credential);
    protected abstract string  BuildCacheKey(TOptions options, ResolvedCredential credential);

    internal void Clear() => _cache.Clear();
}
```

This consolidates today's repeated `BoundedClientCache<T>` + `BuildCacheKey(endpoint, apiKey)` pattern. Each provider supplies the four small abstracts (declare accepted credential kinds, read settings off the connection bag *with deployment name and capability*, fold the credential into the SDK options object, hash the result for caching).

Two important constraints:

1. **`deploymentName` and `capability` are first-class inputs to `ReadOptions`.** This is what allows model-bound SDK clients (e.g., Ollama's `OllamaApiClient(endpoint, model)`) to participate in the cache without aliasing across deployments. Providers that own a *root* client (OpenAI, Azure OpenAI) ignore the deployment name in `ReadOptions`/`BuildCacheKey` and pass the deployment to the SDK at the per-call adapter (e.g., `client.GetChatClient(deploymentName)`).
2. **Cache keys never see raw secrets.** `BuildCacheKey` receives `ResolvedCredential`, which exposes a non-secret `Fingerprint` (see §4); provider implementations MUST use `credential.Fingerprint`, never `credential.ApiKey`. This is enforced by code review and a unit test in the test harness that probes each provider's cache-key for known-secret substrings.

The factory itself is registered as a **singleton** — it's where the cached SDK client objects live across requests. The provider that wraps it is scoped.

### 4. Credential strategy

```csharp
namespace CrestApps.Core.AI.Providers;

public enum CredentialKind
{
    None,
    ApiKey,
    AzureCredential,
    AwsCredential,
    OAuthToken,
}

public sealed class ResolvedCredential
{
    public CredentialKind Kind { get; init; }

    /// <summary>
    /// Raw API key, when the destination SDK only accepts a plain string. Null otherwise.
    /// </summary>
    public string ApiKey { get; init; }

    /// <summary>
    /// Native credential object (e.g., <c>Azure.Core.TokenCredential</c>, AWS <c>AWSCredentials</c>).
    /// Preferred over <see cref="ApiKey"/> whenever the SDK exposes a typed credential.
    /// </summary>
    public object NativeCredential { get; init; }

    /// <summary>
    /// Deterministic, non-secret fingerprint suitable for cache keys, log lines, and metrics.
    /// Computed by the resolver from a salted hash of the underlying secret material.
    /// </summary>
    public string Fingerprint { get; init; }

    /// <summary>
    /// Typed access to <see cref="NativeCredential"/> with an explicit cast failure mode.
    /// </summary>
    public T GetNativeCredential<T>() where T : class
        => NativeCredential as T
           ?? throw new InvalidOperationException($"Resolved credential is not of type {typeof(T)}.");
}

public interface IProviderCredentialResolver
{
    bool CanResolve(string providerName, AIProviderConnectionEntry connection);

    ResolvedCredential Resolve(
        string providerName,
        AIProviderConnectionEntry connection,
        IReadOnlySet<CredentialKind> acceptedKinds);
}

public interface IProviderCredentialResolverSelector
{
    IProviderCredentialResolver Select(string providerName, AIProviderConnectionEntry connection);
}
```

The default selector iterates registered `IProviderCredentialResolver`s and picks the first whose `CanResolve` returns true. A `DefaultProviderCredentialResolver` ships in `CrestApps.Core.AI` and handles the `ApiKey` case (reads `connection.GetApiKey()`, fingerprints with SHA-256 over the raw key bytes). Providers that need richer credential modes (Azure managed identity, AWS profile, OAuth refresh) ship their own resolver registered via `services.AddSingleton<IProviderCredentialResolver, AzureManagedIdentityCredentialResolver>()`.

`Fingerprint` is the canonical anchor for everything secret-adjacent: cache keys, structured log fields, and observability counters. The raw `ApiKey` field is set only when the destination SDK forces a string. This dovetails with the `RedactedSecret` work from Phase 1 — secrets never have to round-trip into the AI context bag, and the provider implementation is never responsible for hashing secrets correctly.

### 5. Provider registration

```csharp
namespace CrestApps.Core.AI.Providers;

public sealed class AIProviderRegistration
{
    public string Name { get; init; }
    public AIProviderCapability Capabilities { get; init; }
    public Action<AIProfileOptions> ConfigureProfile { get; init; }
    public Action<AIConnectionSourceOptions> ConfigureConnectionSource { get; init; }
}

public static class AIProviderServiceCollectionExtensions
{
    public static IServiceCollection AddCrestAppsAIProvider<TProvider>(
        this IServiceCollection services,
        Action<AIProviderRegistration> configure)
        where TProvider : class, IAIProvider
    {
        var registration = new AIProviderRegistration();
        configure(registration);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIProvider, TProvider>());
        services.AddCoreAIProfile(registration.Name, registration.ConfigureProfile);
        services.AddCoreAIConnectionSource(registration.Name, registration.ConfigureConnectionSource);
        services.TryAddSingleton<IProviderCredentialResolverSelector, DefaultProviderCredentialResolverSelector>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderCredentialResolver, DefaultProviderCredentialResolver>());

        return services;
    }
}
```

Each provider package then exposes:

```csharp
public static IServiceCollection AddCoreAIOllama(this IServiceCollection services)
{
    services.AddSingleton<OllamaClientFactory>();
    services.AddCrestAppsAIProvider<OllamaProvider>(r =>
    {
        r.Name = OllamaConstants.ClientName;
        r.Capabilities = AIProviderCapability.Chat | AIProviderCapability.Embeddings;
        r.ConfigureProfile = profile => { /* Ollama-specific profile defaults */ };
        r.ConfigureConnectionSource = source => { /* Ollama-specific connection metadata */ };
    });

    // Provider-specific extras (e.g., a non-default `IAICompletionClient`) go here only when non-standard.
    return services;
}
```

Compared to today, the only thing the package owns is the `OllamaProvider` itself, the `OllamaClientFactory`, and any provider-specific completion / response handler. The registration helper covers the boilerplate that today every package re-implements (profile source, connection source, default credential resolver wiring).

### 6. Reference port: Ollama

Ollama is the smallest provider surface (chat + embeddings, no auth, no images, no speech) which makes it the cleanest reference port:

```csharp
public sealed class OllamaProvider : ProviderBase<OllamaApiClient>
{
    public override string Name => OllamaConstants.ClientName;
    public override AIProviderCapability Capabilities => AIProviderCapability.Chat | AIProviderCapability.Embeddings;
    public override IReadOnlySet<CredentialKind> AcceptedCredentialKinds { get; } = new HashSet<CredentialKind> { CredentialKind.None };

    public OllamaProvider(OllamaClientFactory factory) : base(factory) { }

    protected override IChatClient BuildChatClient(OllamaApiClient client, string deploymentName)
        => client; // OllamaApiClient already implements IChatClient (model-bound at construction)

    protected override IEmbeddingGenerator<string, Embedding<float>> BuildEmbeddingGenerator(OllamaApiClient client, string deploymentName)
        => client; // same instance, different facet
}

internal sealed class OllamaClientFactory : ProviderClientFactory<OllamaClientOptions, OllamaApiClient>
{
    public OllamaClientFactory(IProviderCredentialResolverSelector selector) : base(selector) { }

    protected override IReadOnlySet<CredentialKind> AcceptedCredentialKinds { get; } = new HashSet<CredentialKind> { CredentialKind.None };

    protected override OllamaClientOptions ReadOptions(AIProviderConnectionEntry c, string deploymentName, AIProviderCapability _)
        => new() { Endpoint = c.GetEndpoint(), Model = deploymentName };

    protected override OllamaClientOptions ApplyCredential(OllamaClientOptions o, ResolvedCredential _) => o;

    // Cache key includes the model because Ollama's SDK client is model-bound at construction.
    protected override string BuildCacheKey(OllamaClientOptions o, ResolvedCredential _)
        => $"{o.Endpoint.AbsoluteUri}|{o.Model}";
}
```

That replaces the current `OllamaAIClientProvider` end-to-end. All five "unsupported" methods come from the `ProviderBase<TClient>` base class and throw a consistent `NotSupportedException` with the provider name pre-filled. **`DefaultAIClientFactory` then wraps the returned `IChatClient` with the request-scoped `Microsoft.Extensions.AI` pipeline** (so middleware, logging, and per-call options resolve from the right scope).

### 7. Deployment-name fallback (all capabilities)

`AIClientProviderBase` today reads default deployment names from the connection bag for chat, embedding, image, and speech-to-text — but **not** text-to-speech. The new `ProviderBase<TClient>` normalizes all six capabilities through the same fallback table:

| Capability | Connection key |
|---|---|
| `Chat` | `ChatDeploymentName` |
| `Chat` (utility) | `UtilityDeploymentName` (then falls back to `ChatDeploymentName`) |
| `Embeddings` | `EmbeddingDeploymentName` |
| `Images` | `ImagesDeploymentName` |
| `SpeechToText` | `SpeechToTextDeploymentName` |
| `TextToSpeech` | `TextToSpeechDeploymentName` *(new)* |

`GetSpeechVoicesAsync` is treated as part of the `TextToSpeech` capability (no separate flag).

## Migration plan

The framework is pre-GA; rather than maintaining a parallel legacy adapter, we land the refactor as one branch with the providers ported in series so each commit builds and tests:

1. **Add the new types** (`IAIProvider`, `ProviderClientFactory<,>`, `ProviderBase<TClient>`, `IProviderCredentialResolver`, `IProviderCredentialResolverSelector`, `ResolvedCredential`, `AIProviderRegistration`, `AddCrestAppsAIProvider<T>`) plus the default credential resolver/selector. Wire `DefaultAIClientFactory` to consume `IEnumerable<IAIProvider>` while keeping its existing `IAIClientProvider`-fed code path active behind the scenes.
2. **Port Ollama** as the reference. Delete `OllamaAIClientProvider` (no `IAIClientProvider` registration left for Ollama). Update tests.
3. **Port the rest in alphabetical order**: Azure AI Inference, Azure OpenAI, Bedrock, Claude, DeepSeek, Google, Mistral, OpenAI. Each port is its own commit; all in-tree tests pass at each step.
4. **Remove the legacy code path** in `DefaultAIClientFactory` and delete `IAIClientProvider` + `AIClientProviderBase`. No `[Obsolete]` shim — pre-GA cleanup.
5. **Add factory-level capability/deployment validation** (the mapping in §1.) and a unit test per provider proving its `BuildCacheKey` does not contain raw secrets.
6. **Docs:** promote this design doc into the canonical "AI provider architecture" page (drop the *(proposal)* suffix); update each per-provider doc with the new flag table.

## Open questions

- **Should `Get*Async` methods on providers that don't support a capability return `null` instead of throwing?** Throwing matches the current behaviour and is more discoverable in stack traces, but `null` would let callers do `if (provider.Supports(...))` once and be done. Current preference: keep throwing, since the `Supports` flag is the canonical discovery API and the factory enforces capability before invoking the provider anyway.
- **Should `BoundedClientCache<T>` capacity be configurable per provider?** The shared default (64) is fine for most deployments, but a Bedrock/SageMaker style fan-out might want more. Could surface as `ProviderClientFactoryOptions.MaxCachedClients`. Defer until somebody asks.
- **Provider-specific completion clients.** Today some providers register a non-default `IAICompletionClient` (e.g. for SDK-specific streaming quirks). The new registration helper does *not* register a default `IAICompletionClient` automatically — provider packages keep doing that explicitly when needed. Whether to fold a "default `IChatClient`-backed completion client" into `AddCrestAppsAIProvider` is left as a future cleanup once we see how many providers actually need a custom one.

## Out of scope

- Per-provider quota / rate-limit middleware (lives in `Microsoft.Extensions.AI` pipelines today).
- Streaming protocol differences (handled inside each SDK; not a framework concern).
- Server-Sent Events transport hardening (separate work item).
