---
sidebar_label: Overview
sidebar_position: 1
title: AI Clients
description: AI client architecture and how to connect to OpenAI, Azure OpenAI, Ollama, and Azure AI Inference.
---

# AI Clients

> Connect one or more model providers to the shared CrestApps runtime. Most applications should register providers through `AddAISuite(...)` first and drop to the lower-level service extensions only when they need custom composition.

## Quick Start

Register the providers you use:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddAzureOpenAI()
        .AddOllama()
        .AddAzureAIInference()
    )
);
```

The matching lower-level `IServiceCollection` extensions are still available:

- `AddCoreAIOpenAI()`
- `AddCoreAIAzureOpenAI()`
- `AddCoreAIOllama()`
- `AddCoreAIAzureAIInference()`

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

## Connections and deployments

Each provider usually needs:

1. a **connection** that stores credentials and endpoint details
2. a **deployment** that names the model or deployment you want to use

In the shared configuration model, those live under:

- `CrestApps:AI:Connections`
- `CrestApps:AI:Deployments`

The concrete runtime model is `AIProviderConnection`, while some lower-level APIs and converters still use `AIProviderConnectionEntry` for configuration-oriented shapes.

```json
{
  "CrestApps": {
    "AI": {
      "Connections": [
        {
          "Name": "primary-openai",
          "ClientName": "OpenAI",
          "ApiKey": "YOUR_API_KEY"
        }
      ],
      "Deployments": [
        {
          "Name": "gpt-4.1",
          "ConnectionName": "primary-openai",
          "ModelName": "gpt-4.1",
          "Type": "Chat"
        }
      ]
    }
  }
}
```

## Client connection model

```csharp
public sealed class AIProviderConnection
{
    public string Name { get; set; }
    public string ClientName { get; set; }
    public string DisplayText { get; set; }
}
```

Connections are typically stored in configuration, a first-party store package, or another custom catalog implementation. See the [MVC Example](../core/mvc-example.md) for a complete setup.

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
