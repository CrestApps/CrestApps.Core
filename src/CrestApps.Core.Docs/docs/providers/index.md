---
sidebar_label: Overview
sidebar_position: 1
title: AI Clients
description: AI client architecture and how to connect to OpenAI, Azure OpenAI, Ollama, and Azure AI Inference.
---

# AI Clients

> Connect to one or more LLM providers. Each provider registers an `IAIClientProvider` that creates typed AI clients.

## Quick Start

Register the providers you use:

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    .AddCoreAIOpenAI()          // OpenAI (api.openai.com)
    .AddCoreAIAzureOpenAI()     // Azure OpenAI Service
    .AddCoreAIOllama()          // Ollama (local models)
    .AddCoreAIAzureAIInference(); // Azure AI Inference / GitHub Models
```

You only need to register the providers you actually use.

## Architecture

Each provider follows the same pattern:

1. **Registers an `IAIClientProvider`** — Creates chat clients, embedding generators, image generators, etc.
2. **Registers an `IAICompletionClient`** — Handles completion requests for that provider
3. **Registers a connection source** — Provides connection metadata (API keys, endpoints)

```text
IAIClientFactory
    │
    ├── OpenAIClientProvider
    │       └── Creates OpenAI.ChatClient
    │
    ├── AzureOpenAIClientProvider
    │       └── Creates AzureOpenAI.ChatClient
    │
    ├── OllamaAIClientProvider
    │       └── Creates Ollama ChatClient
    │
    └── AzureAIInferenceClientProvider
            └── Creates Azure.AI.Inference ChatClient
```

## Client Connection

Each provider needs at least one **connection** that stores credentials:

```csharp
public class AIProviderConnectionEntry
{
    public string Name { get; set; }           // Unique connection name
    public string ClientName { get; set; }     // "OpenAI", "Azure", "Ollama", etc.
    public string GetApiKey();                 // API key
    public Uri GetEndpoint();                  // Endpoint URL (optional for OpenAI)
}
```

Connections are typically stored in a configuration file or database and loaded at startup. See the [MVC Example](../core/mvc-example.md) for a complete setup.

## The host's own transport

Each provider's SDK client sends its requests through a named `HttpClient` from `IHttpClientFactory`. Configure a
client of the same name to choose the transport. For example, a handler can check the address of every connection a
provider opens, so an endpoint whose host name resolved to a public address when the connection was saved, and to an
internal one later, is refused instead of being called with the provider's API key.

| Provider | `HttpClient` name |
|---|---|
| OpenAI | `OpenAIConstants.HttpClientName` (`"CrestApps.OpenAI"`) |
| Azure OpenAI, including its Responses API client | `AzureOpenAIConstants.HttpClientName` (`"CrestApps.AzureOpenAI"`) |
| Azure AI Inference | `AzureAIInferenceConstants.HttpClientName` (`"CrestApps.AzureAIInference"`) |
| Ollama | `OllamaConstants.HttpClientName` (`"CrestApps.Ollama"`) |
| Claude | `ClaudeClientService.HttpClientName` (`"CrestApps.Claude"`) |

```csharp
builder.Services
    .AddHttpClient(OpenAIConstants.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),

        // Your own check of the address each new connection resolves to.
        ConnectCallback = AddressGuard.ConnectAsync,
    });
```

The providers register these clients with no `HttpClient.Timeout`, because the SDK applies its own network timeout
and retries. They do not follow redirects, so a provider's API key header is never sent on to the host a redirect
names, and they keep no cookies. The Claude client also decompresses responses, as the Anthropic SDK's own client does.
The Ollama client keeps the default `HttpClient` it always used.

Configure the client after registering the provider, so a primary handler you supply replaces theirs, and give it the
same settings. Keep `PooledConnectionLifetime` set: the SDK clients live as long as the container, and a connection is
only opened again, and its host name resolved again, when the pool retires it. Each request is also written to the
standard `HttpClient` logs, under the `System.Net.Http.HttpClient.<name>` categories.

### One set of clients per container

The OpenAI, Azure OpenAI and Ollama SDK clients are built once and kept in `AIProviderClientCache`, which each provider
registers as a singleton. Each container, and so each tenant of a multi-tenant host, keeps its own clients and never
uses another one's client, credentials or transport. A client is keyed on everything it is built from, including a
hash of its API key, so a rotated key gets a new client. Call `Clear()` on the cache to release the clients built with
an old key.

### Copilot

The Copilot CLI makes its own model requests. A host that registers a `CopilotRequestHandler` has them sent through
it, and so through the `HttpClient` it gives the handler. The request handler is an experimental API of the Copilot SDK
(`GHCP001`). WebSocket connections also reach the handler, but it opens them with its own `ClientWebSocket`, not the
`HttpClient`.

```csharp
#pragma warning disable GHCP001 // The request handler API of the Copilot SDK is experimental.
builder.Services.AddHttpClient("CopilotModelRequests", client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton(services => new CopilotRequestHandler(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("CopilotModelRequests")));
#pragma warning restore GHCP001
```

### Not covered yet

The Azure OpenAI realtime client and the Azure Speech clients open their own connections and do not use these named
clients.

## Adding a Custom Provider

Implement these interfaces:

1. **`IAIClientProvider`** — Creates client instances
2. **`IAICompletionClient`** — Handles completions

```csharp
public sealed class MyProviderClientProvider : IAIClientProvider
{
    public bool CanHandle(string clientName)
    {
        return string.Equals(clientName, "MyProvider", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<IChatClient> GetChatClientAsync(
        AIProviderConnectionEntry connection, string deploymentName)
    {
        // Create and return your chat client
        return ValueTask.FromResult<IChatClient>(new MyProviderChatClient());
    }

    // Implement other client creation methods...
}

public sealed class MyProviderCompletionClient : IAICompletionClient
{
    public async Task<ChatResponse> CompleteAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        // Send completion request to your provider
    }
}
```

Register:

```csharp
builder.Services.AddScoped<IAIClientProvider, MyProviderClientProvider>();
builder.Services.AddCoreAICompletionClient<MyProviderCompletionClient>("MyProvider");
builder.Services.AddCoreAIConnectionSource("MyProvider", configure => { /* ... */ });
```

## Available Clients

| Client | Extension | ClientName | Documentation |
|--------|-----------|------------|---------------|
| OpenAI | `AddCoreAIOpenAI()` | `"OpenAI"` | [OpenAI](./openai.md) |
| Azure OpenAI | `AddCoreAIAzureOpenAI()` | `"Azure"` | [Azure OpenAI](./azure-openai.md) |
| Ollama | `AddCoreAIOllama()` | `"Ollama"` | [Ollama](./ollama.md) |
| Azure AI Inference | `AddCoreAIAzureAIInference()` | `"AzureAIInference"` | [Azure AI Inference](./azure-ai-inference.md) |

## Real-time (speech-to-speech) clients

A **real-time client** exchanges audio (and optionally text) with the provider over a persistent, bidirectional
session, enabling low-latency speech-to-speech conversations without separate speech-to-text and text-to-speech
steps. The client is the `Microsoft.Extensions.AI` `IRealtimeClient` / `IRealtimeClientSession` type, created from a
resolved deployment:

```csharp
#pragma warning disable MEAI001 // The Microsoft.Extensions.AI realtime API is evaluation-only.
IRealtimeClient client = await clientFactory.CreateRealtimeClientAsync(deployment);

await using var session = await client.CreateSessionAsync(new RealtimeSessionOptions
{
    Instructions = "You are a friendly voice assistant.",
    Voice = "alloy",
    InputAudioFormat = new RealtimeAudioFormat("audio/pcm", 24000),
    OutputAudioFormat = new RealtimeAudioFormat("audio/pcm", 24000),
    OutputModalities = ["audio"],
    VoiceActivityDetection = new VoiceActivityDetectionOptions { Enabled = true },
});
```

- **`IAIClientFactory.CreateRealtimeClientAsync(AIDeployment)`** resolves the provider and connection, then returns
  the realtime client. Under the hood each provider implements
  **`IAIClientProvider.GetRealtimeClientAsync(connection, deploymentName)`**; providers without a realtime API throw
  `NotSupportedException`.
- **OpenAI** and **Azure OpenAI** support realtime (both use the OpenAI `gpt-realtime` models). Ollama and Azure AI
  Inference do not.
- Declare the **`realtime`** model capability on the deployment; it then fills the **`realtime`** deployment
  slot. A chat profile or interaction becomes a voice conversation simply by selecting that deployment as its
  chat deployment; there is no separate realtime mode or field.

### Realtime voices

Realtime voices are a fixed, provider-defined set (the realtime API has no enumeration endpoint). Resolve them the
same way as speech voices — through a resolver that delegates to the provider:

```csharp
SpeechVoice[] voices = await realtimeVoiceResolver.GetVoicesAsync(deployment);
```

- **`IRealtimeVoiceResolver.GetVoicesAsync(AIDeployment)`** (registered by default) mirrors `ISpeechVoiceResolver`
  and returns `SpeechVoice[]`. It delegates to **`IAIClientProvider.GetRealtimeVoicesAsync(...)`**.
- OpenAI and Azure OpenAI return the `gpt-realtime` voice set — `alloy`, `ash`, `ballad`, `cedar`, `coral`, `echo`,
  `marin`, `sage`, `shimmer`, `verse` — declared in `OpenAIRealtimeVoices`. Each carries a best-effort (unofficial)
  gender to help group a voice selector.

:::note
Realtime sessions apply the profile's system prompt, tools, retrieval-augmented knowledge, and orchestration.
Use Chat Interactions, AI Profile chat sessions, or the admin chat widget to exercise realtime sessions end to end.
:::

## Provider Comparison

| Capability | OpenAI | Azure OpenAI | Ollama | Azure AI Inference |
|-----------|--------|-------------|--------|-------------------|
| Chat completions | ✅ | ✅ | ✅ | ✅ |
| Streaming | ✅ | ✅ | ✅ | ✅ |
| Function calling | ✅ | ✅ | ⚠️ Model-dependent | ⚠️ Model-dependent |
| Embeddings | ✅ | ✅ | ✅ | ✅ |
| Image generation | ✅ (DALL·E) | ✅ (DALL·E) | ❌ | ❌ |
| Speech-to-text | ✅ (Whisper) | ✅ (via Azure Speech) | ❌ | ❌ |
| Text-to-speech | ✅ | ✅ (via Azure Speech) | ❌ | ❌ |
| Realtime (speech-to-speech) | ✅ | ✅ | ❌ | ❌ |
| Vision (image input) | ✅ | ✅ | ⚠️ Model-dependent | ⚠️ Model-dependent |
| Managed identity | ❌ | ✅ | N/A | ✅ |
| Data residency | ❌ | ✅ (per region) | ✅ (local) | ✅ (per region) |
| Cost tier | Pay-per-token | Pay-per-token | Free (self-hosted) | Pay-per-token |

## When to Choose Which Provider

| Scenario | Recommended Provider | Why |
|----------|---------------------|-----|
| **Prototyping / getting started** | OpenAI | Simplest setup — just an API key |
| **Enterprise production** | Azure OpenAI | Data residency, SLAs, managed identity, VNET support |
| **Local development** | Ollama | No API costs, fast iteration, offline capable |
| **Privacy-sensitive workloads** | Ollama | Data never leaves your infrastructure |
| **Multi-model exploration** | Azure AI Inference | Access GPT, Llama, Mistral, Cohere through a single endpoint |
| **GitHub-integrated workflows** | Azure AI Inference | Use your GitHub token to access models via GitHub Models |
| **Image generation** | OpenAI or Azure OpenAI | Only providers with DALL·E support |
| **Speech capabilities** | OpenAI or Azure OpenAI | Only providers with Whisper/TTS support |

:::tip
You can register **multiple providers simultaneously** and assign different profiles to different providers. For example, use Ollama for development and Azure OpenAI for production by switching connection names per environment.
:::

## Custom Provider Walkthrough

To add a provider for a service not covered by the built-in providers, implement three components:

### Step 1: Implement `IAIClientProvider`

The client provider creates typed AI clients (chat, embedding, image) from a connection entry:

```csharp
public sealed class MyProviderClientProvider : IAIClientProvider
{
    public bool CanHandle(string clientName)
    {
        return string.Equals(clientName, "MyProvider", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<IChatClient> GetChatClientAsync(
        AIProviderConnectionEntry connection,
        string deploymentName)
    {
        var apiKey = connection.GetApiKey();
        var endpoint = connection.GetEndpoint()
            ?? new Uri("https://api.myprovider.com");

        // Use Microsoft.Extensions.AI abstractions
        return ValueTask.FromResult<IChatClient>(
            new MyProviderChatClient(endpoint, apiKey, deploymentName));
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        AIProviderConnectionEntry connection,
        string deploymentName)
    {
        var apiKey = connection.GetApiKey();
        var endpoint = connection.GetEndpoint()
            ?? new Uri("https://api.myprovider.com");

        return new MyProviderEmbeddingGenerator(endpoint, apiKey, deploymentName);
    }

    // Return null for capabilities the provider does not support
    public object CreateImageGenerator(
        AIProviderConnectionEntry connection,
        string deploymentName)
        => null;
}
```

### Step 2: Implement `IAICompletionClient`

The completion client handles the request/response cycle:

```csharp
public sealed class MyProviderCompletionClient(
    IAIClientFactory clientFactory,
    ILogger<MyProviderCompletionClient> logger) : IAICompletionClient
{
    public async Task<ChatResponse> CompleteAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var chatClient = clientFactory.GetChatClient(context);

        if (chatClient is null)
        {
            logger.LogWarning("No chat client available for connection '{Name}'.",
                context.ConnectionName);
            return ChatResponse.Empty;
        }

        var options = new ChatOptions
        {
            Temperature = context.Profile.Temperature,
            MaxOutputTokens = context.Profile.MaxOutputTokens,
        };

        // Delegate to the Microsoft.Extensions.AI IChatClient
        return await chatClient.GetResponseAsync(
            context.Messages,
            options,
            cancellationToken);
    }
}
```

### Step 3: Register Connection Source and Services

```csharp
public static class MyProviderServiceExtensions
{
    public static AIServiceBuilder AddMyProvider(this AIServiceBuilder builder)
    {
        var services = builder.Services;

        // Register the client provider
        services.AddScoped<IAIClientProvider, MyProviderClientProvider>();

        // Register the completion client for this client name
        services.AddCoreAICompletionClient<MyProviderCompletionClient>("MyProvider");

        // Register the connection source (how credentials are loaded)
        services.AddCoreAIConnectionSource("MyProvider", options =>
        {
            // Connections can be loaded from configuration, database, etc.
            options.Connections.Add(new AIProviderConnectionEntry
            {
                Name = "my-connection",
                ClientName = "MyProvider",
            });
        });

        return builder;
    }
}
```

Use it:

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    .AddMyProvider();
```

## Telemetry

Every provider's streamed completions report their time to first token and their failures on the `CrestApps.Core`
meter, tagged with the client, and get the OpenTelemetry `gen_ai.*` spans and metrics when `EnableOpenTelemetry` is
on. See [Observability](../core/observability.md).

## Fallback Strategies

The framework does not include automatic provider failover, but you can implement fallback logic at the application level:

### Connection-Level Fallback

Register multiple connections for different providers and switch on failure:

```csharp
public sealed class FallbackCompletionService(
    IEnumerable<IAICompletionClient> clients,
    ILogger<FallbackCompletionService> logger)
{
    private readonly string[] _providerOrder = ["Azure", "OpenAI", "Ollama"];

    public async Task<ChatResponse> CompleteWithFallbackAsync(
        AICompletionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var providerName in _providerOrder)
        {
            var client = clients.FirstOrDefault(
                c => c.GetType().Name.Contains(providerName));

            if (client is null)
            {
                continue;
            }

            try
            {
                return await client.CompleteAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Provider '{Provider}' failed, trying next.", providerName);
            }
        }

        throw new InvalidOperationException("All AI providers failed.");
    }
}
```

### Profile-Level Fallback

Assign a primary and fallback connection at the profile level:

```json
{
  "Profiles": {
    "my-chat": {
      "ConnectionName": "azure-primary",
      "FallbackConnectionName": "openai-backup"
    }
  }
}
```

:::warning
When implementing fallback logic, be mindful of token format differences between providers. A conversation started with one provider's tokenizer may behave differently when sent to another provider mid-stream.
:::
