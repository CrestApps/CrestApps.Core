---
sidebar_position: 4
title: AI Resilience
description: Builder-based resilience middleware for Microsoft.Extensions.AI chat, embeddings, image, speech-to-text, and text-to-speech clients.
---

# AI Resilience

> Add reusable retry middleware to Microsoft.Extensions.AI clients without forcing a global policy on every host-created client.

`CrestApps.Core.AI.Resilience` is a standalone package that adds builder-based resilience extensions for:

- `IChatClient`
- `IEmbeddingGenerator<TInput, TEmbedding>`
- `IImageGenerator`
- `ISpeechToTextClient`
- `ITextToSpeechClient`

Framework-owned completion and utility chat paths in `CrestApps.Core` already use the default retry policy internally. This package is for host-created clients and for applications that want to opt into the same pattern explicitly.

## Package

```xml
<PackageReference Include="CrestApps.Core.AI.Resilience" Version="*" />
```

The package depends on:

- `Microsoft.Extensions.AI`
- `Microsoft.Extensions.Resilience`

## Builder Extensions

Every supported client follows one of these patterns:

1. Resolve the client through `IAIClientFactory` and configure the builder pipeline through the factory overload, or
2. Resolve or create the raw Microsoft.Extensions.AI client yourself, convert it to the corresponding builder with `.AsBuilder()`, apply `UseDefaultResilience()` or `UseResilience(...)`, and finish with `Build(serviceProvider)`.

When you build manually, always pass the active `IServiceProvider` to `Build(serviceProvider)`. Do not use `Build()` or `Build(null)`, because downstream middleware may need DI to resolve services such as tools and related runtime components.

## Default Policy

`UseDefaultResilience()` is intentionally narrow: it retries provider rate-limit failures such as HTTP `429 Too Many Requests`.

Default settings:

| Setting | Default |
|---|---|
| `MaxRateLimitRetries` | `5` |
| `RateLimitRetryDelay` | `1 second` |
| `BackoffType` | `Exponential` |
| `UseJitter` | `true` |
| `MaxRetryDelay` | `32 seconds` |

That produces an approximate retry schedule like this:

| Attempt | Delay |
|---|---|
| Initial | immediately |
| Retry 1 | ~1-2 seconds |
| Retry 2 | ~2-4 seconds |
| Retry 3 | ~4-8 seconds |
| Retry 4 | ~8-16 seconds |
| Retry 5 | ~16-32 seconds |

The exact delay varies because jitter is enabled by default.

## Chat Example

If you are resolving the client through `IAIClientFactory`, use the overload and let the factory own the final build:

```csharp
var resilientClient = await aiClientFactory.CreateChatClientAsync(
    deployment,
    builder => builder.UseDefaultResilience());
```

If you already have a raw `IChatClient`, use the builder directly:

```csharp
var resilientClient = chatClient
    .AsBuilder()
    .UseDefaultResilience()
    .Build(serviceProvider);
```

## Customizing the Default Settings

Use the options callback when you want to keep the built-in rate-limit handling but tune the retry shape:

```csharp
var resilientClient = await aiClientFactory.CreateChatClientAsync(
    deployment,
    builder => builder.UseDefaultResilience(options =>
    {
        options.MaxRateLimitRetries = 3;
        options.RateLimitRetryDelay = TimeSpan.FromSeconds(2);
        options.BackoffType = DelayBackoffType.Exponential;
        options.UseJitter = true;
        options.MaxRetryDelay = TimeSpan.FromSeconds(20);
    }));
```

The equivalent direct-builder form is:

```csharp
var resilientClient = chatClient
    .AsBuilder()
    .UseDefaultResilience(options =>
    {
        options.MaxRateLimitRetries = 3;
        options.RateLimitRetryDelay = TimeSpan.FromSeconds(2);
        options.BackoffType = DelayBackoffType.Exponential;
        options.UseJitter = true;
        options.MaxRetryDelay = TimeSpan.FromSeconds(20);
    })
    .Build(serviceProvider);
```

If you prefer the old fixed schedule, configure it explicitly:

```csharp
var resilientClient = chatClient
    .AsBuilder()
    .UseDefaultResilience(options =>
    {
        options.MaxRateLimitRetries = 4;
        options.RateLimitRetryDelay = TimeSpan.FromSeconds(5);
        options.BackoffType = DelayBackoffType.Constant;
        options.UseJitter = false;
        options.MaxRetryDelay = TimeSpan.FromSeconds(5);
    })
    .Build(serviceProvider);
```

## Fully Custom Pipelines

Use `UseResilience(...)` when you want full control over the Polly pipeline:

```csharp
var resilientClient = chatClient
    .AsBuilder()
    .UseResilience(pipeline => pipeline.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is HttpRequestException ex &&
            ex.StatusCode == HttpStatusCode.TooManyRequests),
    }))
    .Build(serviceProvider);
```

You can also supply a prebuilt `ResiliencePipeline`.

## Other Client Types

The same extension methods are available on the other Microsoft.Extensions.AI builders:

### Embeddings

```csharp
var resilientGenerator = await aiClientFactory.CreateEmbeddingGeneratorAsync(
    deployment,
    builder => builder.UseDefaultResilience());
```

```csharp
var resilientGenerator = embeddingGenerator
    .AsBuilder()
    .UseDefaultResilience()
    .Build(serviceProvider);
```

### Image Generation

```csharp
var resilientGenerator = await aiClientFactory.CreateImageGeneratorAsync(
    deployment,
    builder => builder.UseDefaultResilience());
```

```csharp
var resilientGenerator = imageGenerator
    .AsBuilder()
    .UseDefaultResilience()
    .Build(serviceProvider);
```

### Speech to Text

```csharp
var resilientClient = await aiClientFactory.CreateSpeechToTextClientAsync(
    deployment,
    builder => builder.UseDefaultResilience());
```

```csharp
var resilientClient = speechToTextClient
    .AsBuilder()
    .UseDefaultResilience()
    .Build(serviceProvider);
```

### Text to Speech

```csharp
var resilientClient = await aiClientFactory.CreateTextToSpeechClientAsync(
    deployment,
    builder => builder.UseDefaultResilience());
```

```csharp
var resilientClient = textToSpeechClient
    .AsBuilder()
    .UseDefaultResilience()
    .Build(serviceProvider);
```

## Streaming Notes

- `ITextToSpeechClient` streaming retries are supported when the failure happens before the first streamed update is yielded.
- `ISpeechToTextClient` non-streaming retries work for both seekable and non-seekable streams.
- `ISpeechToTextClient` streaming retries require a seekable input stream so the audio can be replayed safely across retry attempts.

## When to Use It

Use `UseDefaultResilience()` when:

- you want a safe default for provider throttling
- you want framework-style retries on your own clients
- you do not need a custom Polly pipeline yet

Use `UseResilience(...)` when:

- you need custom retry predicates
- you want to add additional strategies yourself
- you want one shared prebuilt pipeline across multiple clients
