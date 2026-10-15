---
sidebar_label: Azure OpenAI
sidebar_position: 3
title: Azure OpenAI Provider
description: Connect to Azure OpenAI Service for enterprise-grade AI completions with Azure-specific optimizations.
---

# Azure OpenAI Provider

> Connect to Azure OpenAI through the OpenAI SDK on the resource's `/openai/v1/` endpoint, with API key or Microsoft Entra ID authentication.

## Quick Start

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    .AddCoreAIAzureOpenAI();
```

## Services Registered

| Service | Implementation | Lifetime |
|---------|---------------|----------|
| `IAIClientProvider` | `AzureOpenAIClientProvider` | Scoped |
| `IAICompletionClient` | `AzureOpenAICompletionClient` | Scoped |
| `IOpenAIChatOptionsConfiguration` | `AzurePatchOpenAIDataSourceHandler` | Scoped |
| Connection source | — | Scoped |

## Configuration

### Connection Setup

Azure OpenAI requires an endpoint URL and either an API key or Azure AD credentials:

```json
{
  "CrestApps": {
    "AI": {
        "Connections": [
          {
            "Name": "azure-production",
            "ClientName": "Azure",
            "Endpoint": "https://my-resource.openai.azure.com/",
            "ApiKey": "your-api-key"
          }
        ]
      }
    }
}
```

### The v1 endpoint

Azure OpenAI serves the OpenAI API on every resource at `https://<resource>.openai.azure.com/openai/v1/`, and the
provider talks to it through the plain `OpenAI` SDK. Keep `Endpoint` set to the resource URL (a
`https://<resource>.services.ai.azure.com/` endpoint works too); the provider appends `/openai/v1/` itself, sends the
deployment name as the model, and needs no `api-version`. Chat, Responses, embeddings, images, audio and realtime all
go through this one client.

An API key is sent as a bearer token on HTTP requests and as the `api-key` header on realtime WebSocket handshakes.
Managed identity and `DefaultAzureCredential` connections request a Microsoft Entra ID token for the
`https://cognitiveservices.azure.com/.default` scope, which the identity needs the `Cognitive Services OpenAI User`
role for; realtime sessions acquire a fresh token when they start.

### Constants and global client settings

| Constant | Value |
|----------|-------|
| `AzureOpenAIConstants.ClientName` | `"Azure"` |

Use `CrestApps:AI:AzureClient` for the OpenAI SDK client settings shared by all Azure OpenAI connections, including logging plus the default SDK retry policy used by the framework's Azure OpenAI completion path:

```json
{
  "CrestApps": {
    "AI": {
      "AzureClient": {
        "EnableDefaultRetryPolicy": true,
        "MaxRetryAttempts": 5,
        "RateLimitRetryDelay": "00:00:01",
        "BackoffType": "Exponential",
        "UseJitter": true,
        "MaxRetryDelay": "00:00:32",
        "EnableLogging": false,
        "EnableMessageLogging": false,
        "EnableMessageContentLogging": false
      }
    }
  }
}
```

The SDK retry defaults match the framework resilience defaults:

| Setting | Default |
|---|---|
| `MaxRetryAttempts` | `5` |
| `RateLimitRetryDelay` | `00:00:01` |
| `BackoffType` | `Exponential` |
| `UseJitter` | `true` |
| `MaxRetryDelay` | `00:00:32` |

That produces an approximate schedule of `1-2`, `2-4`, `4-8`, `8-16`, and `16-32` seconds across retries.

## Azure-Specific Behavior

The Azure provider includes `AzurePatchOpenAIDataSourceHandler` which automatically:

- Maps `max_tokens` to Azure-compatible format
- Handles Azure-specific stream options
- Patches `ChatCompletionOptions` for Azure API compatibility

## Capabilities

| Capability | Supported |
|-----------|-----------|
| Chat completions | ✅ |
| Streaming | ✅ |
| Embeddings | ✅ |
| Image generation | ✅ |
| Speech-to-text | ✅ (via Azure Speech) |
| Text-to-speech | ✅ (via Azure Speech) |
| Realtime (speech-to-speech) | ✅ (`gpt-realtime`) |

Realtime is exposed through `IAIClientFactory.CreateRealtimeClientAsync` (see
[Real-time clients](./index.md#real-time-speech-to-speech-clients)). Azure OpenAI connects to the realtime WebSocket
endpoint (`/openai/v1/realtime`) through the OpenAI SDK's realtime client, with the connection's API key or a
Microsoft Entra ID token on every session's handshake.

Reasoning models such as gpt-6-luna reject function tools together with a reasoning effort on Chat Completions, so
the chat client sends those requests to the Responses endpoint (`/openai/v1/responses`) and keeps every other request
on Chat Completions. Responses requests are sent with `store: false`, so conversations are not kept on the service.

The connection's optional `ChatApi` setting overrides this: `Auto` (the default) and `Responses` route as described,
and `ChatCompletions` sends every request to Chat Completions, for example in a region where the Responses API is not
available yet. See [Chat API](./openai.md#chat-api) for the values.

## Azure Setup

Before configuring the provider, create the required Azure resources:

1. **Create an Azure OpenAI resource** in the [Azure Portal](https://portal.azure.com/#create/Microsoft.CognitiveServicesOpenAI).
2. **Deploy a model** — In your Azure OpenAI resource, go to **Model deployments** → **Create new deployment**. Choose a model (e.g., `gpt-4o`) and give the deployment a name.
3. **Copy the endpoint and key** — Found under **Keys and Endpoint** in the Azure Portal.

:::info
The deployment name in Azure OpenAI is what you pass as the `deploymentName` parameter when creating profiles. It does **not** need to match the model name (e.g., you can name a `gpt-4o` deployment `"my-chat-model"`).
:::

## Configuration

Full `appsettings.json` configuration with endpoint, deployment, optional Azure SDK retry settings, and optional global logging:

```json
{
  "CrestApps": {
    "AI": {
        "AzureClient": {
          "EnableDefaultRetryPolicy": true,
          "MaxRetryAttempts": 5,
          "RateLimitRetryDelay": "00:00:01",
          "BackoffType": "Exponential",
          "UseJitter": true,
          "MaxRetryDelay": "00:00:32",
          "EnableLogging": false,
          "EnableMessageLogging": false,
          "EnableMessageContentLogging": false
        },
        "Connections": [
          {
            "Name": "azure-production",
            "ClientName": "Azure",
            "Endpoint": "https://my-resource.openai.azure.com/",
            "ApiKey": "your-api-key-here"
          }
        ],
        "Deployments": [
          {
            "Name": "gpt-4o",
            "ClientName": "Azure",
            "ConnectionName": "azure-production",
            "Type": "Chat"
          }
        ]
      }
    }
}
```

Or register programmatically:

```csharp
builder.Services.AddCoreAIConnectionSource("Azure", options =>
{
    options.Connections.Add(new AIProviderConnectionEntry
    {
        Name = "azure-production",
        ClientName = "Azure",
        // Endpoint and API key are loaded from configuration
    });
});
```

## Authentication

### API Key

The simplest authentication method. Suitable for development and testing:

```json
{
  "CrestApps": {
    "AI": {
        "Connections": [
          {
            "Name": "azure-production",
            "ClientName": "Azure",
            "Endpoint": "https://my-resource.openai.azure.com/",
            "ApiKey": "your-api-key-here"
          }
        ]
      }
    }
}
```

:::warning
API keys grant full access to your Azure OpenAI resource. Rotate keys regularly and never commit them to source control.
:::

### DefaultAzureCredential

For production environments, use `DefaultAzureCredential` from the Azure Identity SDK. This supports managed identity, Azure CLI, Visual Studio, and other credential sources without storing secrets:

```csharp
builder.Services.AddCoreAIConnectionSource("Azure", options =>
{
    options.Connections.Add(new AIProviderConnectionEntry
    {
        Name = "azure-production",
        ClientName = "Azure",
        // When no API key is set, the provider uses DefaultAzureCredential
    });
});
```

When no API key is configured, the `AzureOpenAIClientProvider` automatically falls back to `DefaultAzureCredential`, which tries these credential sources in order:

1. Environment variables (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`)
2. Workload identity (Kubernetes)
3. Managed identity
4. Azure CLI / Azure PowerShell
5. Visual Studio / VS Code credentials

## Managed Identity

To use managed identity in production (Azure App Service, Azure Container Apps, Azure VMs):

1. **Enable managed identity** on your hosting resource (System-assigned or User-assigned).
2. **Assign the role** `Cognitive Services OpenAI User` to the identity on your Azure OpenAI resource.
3. **Remove the API key** from configuration — the provider uses `DefaultAzureCredential` automatically.

```json
{
  "CrestApps": {
    "AI": {
        "Connections": [
          {
            "Name": "azure-production",
            "ClientName": "Azure",
            "Endpoint": "https://my-resource.openai.azure.com/"
          }
        ]
      }
    }
}
```

:::tip
Managed identity eliminates the need to manage and rotate API keys. It is the recommended authentication method for all Azure-hosted production workloads.
:::
