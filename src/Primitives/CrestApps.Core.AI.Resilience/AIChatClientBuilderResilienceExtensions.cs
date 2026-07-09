using CrestApps.Core.AI.Resilience.Models;
using CrestApps.Core.AI.Resilience.Services;
using Microsoft.Extensions.AI;
using Polly;
using Polly.Retry;

#pragma warning disable MEAI001
namespace CrestApps.Core.AI.Resilience;

/// <summary>
/// Provides resilience extensions for Microsoft.Extensions.AI client builders.
/// </summary>
public static class AIChatClientBuilderResilienceExtensions
{
    private static readonly TimeSpan _defaultRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Adds a custom resilience pipeline to the chat-client middleware pipeline.
    /// </summary>
    /// <param name="builder">The chat-client builder.</param>
    /// <param name="configure">The resilience-pipeline configuration delegate.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="ChatClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware such as tool invocation can resolve required services correctly.
    /// </remarks>
    public static ChatClientBuilder UseResilience(
        this ChatClientBuilder builder,
        Action<ResiliencePipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var pipelineBuilder = new ResiliencePipelineBuilder();
        configure(pipelineBuilder);

        return builder.Use(innerClient => new AIResilienceChatClient(innerClient, pipelineBuilder.Build()));
    }

    /// <summary>
    /// Adds a prebuilt resilience pipeline to the chat-client middleware pipeline.
    /// </summary>
    /// <param name="builder">The chat-client builder.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="ChatClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware such as tool invocation can resolve required services correctly.
    /// </remarks>
    public static ChatClientBuilder UseResilience(
        this ChatClientBuilder builder,
        ResiliencePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.Use(innerClient => new AIResilienceChatClient(innerClient, pipeline));
    }

    /// <summary>
    /// Adds the framework's default resilience policy for provider rate-limit failures.
    /// </summary>
    /// <param name="builder">The chat-client builder.</param>
    /// <param name="configure">An optional delegate to customize the default retry settings.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="ChatClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware such as tool invocation can resolve required services correctly.
    /// </remarks>
    public static ChatClientBuilder UseDefaultResilience(
        this ChatClientBuilder builder,
        Action<AIChatClientRetryOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseResilience(CreateDefaultPipeline(configure));
    }

    /// <summary>
    /// Adds a custom resilience pipeline to the embedding-generator middleware pipeline.
    /// </summary>
    /// <typeparam name="TInput">The embedding input type.</typeparam>
    /// <typeparam name="TEmbedding">The embedding result type.</typeparam>
    /// <param name="builder">The embedding-generator builder.</param>
    /// <param name="configure">The resilience-pipeline configuration delegate.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="EmbeddingGeneratorBuilder{TInput, TEmbedding}.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static EmbeddingGeneratorBuilder<TInput, TEmbedding> UseResilience<TInput, TEmbedding>(
        this EmbeddingGeneratorBuilder<TInput, TEmbedding> builder,
        Action<ResiliencePipelineBuilder> configure)
        where TEmbedding : Embedding
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var pipelineBuilder = new ResiliencePipelineBuilder();
        configure(pipelineBuilder);

        return builder.Use(innerGenerator => new AIResilienceEmbeddingGenerator<TInput, TEmbedding>(innerGenerator, pipelineBuilder.Build()));
    }

    /// <summary>
    /// Adds a prebuilt resilience pipeline to the embedding-generator middleware pipeline.
    /// </summary>
    /// <typeparam name="TInput">The embedding input type.</typeparam>
    /// <typeparam name="TEmbedding">The embedding result type.</typeparam>
    /// <param name="builder">The embedding-generator builder.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="EmbeddingGeneratorBuilder{TInput, TEmbedding}.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static EmbeddingGeneratorBuilder<TInput, TEmbedding> UseResilience<TInput, TEmbedding>(
        this EmbeddingGeneratorBuilder<TInput, TEmbedding> builder,
        ResiliencePipeline pipeline)
        where TEmbedding : Embedding
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.Use(innerGenerator => new AIResilienceEmbeddingGenerator<TInput, TEmbedding>(innerGenerator, pipeline));
    }

    /// <summary>
    /// Adds the framework's default resilience policy for provider rate-limit failures.
    /// </summary>
    /// <typeparam name="TInput">The embedding input type.</typeparam>
    /// <typeparam name="TEmbedding">The embedding result type.</typeparam>
    /// <param name="builder">The embedding-generator builder.</param>
    /// <param name="configure">An optional delegate to customize the default retry settings.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="EmbeddingGeneratorBuilder{TInput, TEmbedding}.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static EmbeddingGeneratorBuilder<TInput, TEmbedding> UseDefaultResilience<TInput, TEmbedding>(
        this EmbeddingGeneratorBuilder<TInput, TEmbedding> builder,
        Action<AIChatClientRetryOptions> configure = null)
        where TEmbedding : Embedding
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseResilience(CreateDefaultPipeline(configure));
    }

    /// <summary>
    /// Adds a custom resilience pipeline to the image-generator middleware pipeline.
    /// </summary>
    /// <param name="builder">The image-generator builder.</param>
    /// <param name="configure">The resilience-pipeline configuration delegate.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="ImageGeneratorBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static ImageGeneratorBuilder UseResilience(
        this ImageGeneratorBuilder builder,
        Action<ResiliencePipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var pipelineBuilder = new ResiliencePipelineBuilder();
        configure(pipelineBuilder);

        return builder.Use(innerGenerator => new AIResilienceImageGenerator(innerGenerator, pipelineBuilder.Build()));
    }

    /// <summary>
    /// Adds a prebuilt resilience pipeline to the image-generator middleware pipeline.
    /// </summary>
    /// <param name="builder">The image-generator builder.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="ImageGeneratorBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static ImageGeneratorBuilder UseResilience(
        this ImageGeneratorBuilder builder,
        ResiliencePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.Use(innerGenerator => new AIResilienceImageGenerator(innerGenerator, pipeline));
    }

    /// <summary>
    /// Adds the framework's default resilience policy for provider rate-limit failures.
    /// </summary>
    /// <param name="builder">The image-generator builder.</param>
    /// <param name="configure">An optional delegate to customize the default retry settings.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="ImageGeneratorBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static ImageGeneratorBuilder UseDefaultResilience(
        this ImageGeneratorBuilder builder,
        Action<AIChatClientRetryOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseResilience(CreateDefaultPipeline(configure));
    }

    /// <summary>
    /// Adds a custom resilience pipeline to the speech-to-text middleware pipeline.
    /// </summary>
    /// <param name="builder">The speech-to-text builder.</param>
    /// <param name="configure">The resilience-pipeline configuration delegate.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="SpeechToTextClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static SpeechToTextClientBuilder UseResilience(
        this SpeechToTextClientBuilder builder,
        Action<ResiliencePipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var pipelineBuilder = new ResiliencePipelineBuilder();
        configure(pipelineBuilder);

        return builder.Use(innerClient => new AIResilienceSpeechToTextClient(innerClient, pipelineBuilder.Build()));
    }

    /// <summary>
    /// Adds a prebuilt resilience pipeline to the speech-to-text middleware pipeline.
    /// </summary>
    /// <param name="builder">The speech-to-text builder.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="SpeechToTextClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static SpeechToTextClientBuilder UseResilience(
        this SpeechToTextClientBuilder builder,
        ResiliencePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.Use(innerClient => new AIResilienceSpeechToTextClient(innerClient, pipeline));
    }

    /// <summary>
    /// Adds the framework's default resilience policy for provider rate-limit failures.
    /// </summary>
    /// <param name="builder">The speech-to-text builder.</param>
    /// <param name="configure">An optional delegate to customize the default retry settings.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="SpeechToTextClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static SpeechToTextClientBuilder UseDefaultResilience(
        this SpeechToTextClientBuilder builder,
        Action<AIChatClientRetryOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseResilience(CreateDefaultPipeline(configure));
    }

    /// <summary>
    /// Adds a custom resilience pipeline to the text-to-speech middleware pipeline.
    /// </summary>
    /// <param name="builder">The text-to-speech builder.</param>
    /// <param name="configure">The resilience-pipeline configuration delegate.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="TextToSpeechClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static TextToSpeechClientBuilder UseResilience(
        this TextToSpeechClientBuilder builder,
        Action<ResiliencePipelineBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var pipelineBuilder = new ResiliencePipelineBuilder();
        configure(pipelineBuilder);

        return builder.Use(innerClient => new AIResilienceTextToSpeechClient(innerClient, pipelineBuilder.Build()));
    }

    /// <summary>
    /// Adds a prebuilt resilience pipeline to the text-to-speech middleware pipeline.
    /// </summary>
    /// <param name="builder">The text-to-speech builder.</param>
    /// <param name="pipeline">The resilience pipeline.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="TextToSpeechClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static TextToSpeechClientBuilder UseResilience(
        this TextToSpeechClientBuilder builder,
        ResiliencePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.Use(innerClient => new AIResilienceTextToSpeechClient(innerClient, pipeline));
    }

    /// <summary>
    /// Adds the framework's default resilience policy for provider rate-limit failures.
    /// </summary>
    /// <param name="builder">The text-to-speech builder.</param>
    /// <param name="configure">An optional delegate to customize the default retry settings.</param>
    /// <returns>The updated builder.</returns>
    /// <remarks>
    /// When finishing the pipeline with <see cref="TextToSpeechClientBuilder.Build(IServiceProvider)"/>,
    /// pass the active <see cref="IServiceProvider"/> rather than <c>null</c> so downstream
    /// middleware can resolve required services correctly.
    /// </remarks>
    public static TextToSpeechClientBuilder UseDefaultResilience(
        this TextToSpeechClientBuilder builder,
        Action<AIChatClientRetryOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseResilience(CreateDefaultPipeline(configure));
    }

    private static ResiliencePipeline CreateDefaultPipeline(Action<AIChatClientRetryOptions> configure = null)
    {
        var options = new AIChatClientRetryOptions();
        configure?.Invoke(options);

        var maxRetryAttempts = Math.Max(options.MaxRateLimitRetries, 0);
        var retryDelay = options.RateLimitRetryDelay < TimeSpan.Zero
            ? _defaultRetryDelay
            : options.RateLimitRetryDelay;
        var maxRetryDelay = options.MaxRetryDelay is { } configuredMaxRetryDelay && configuredMaxRetryDelay < TimeSpan.Zero
            ? null
            : options.MaxRetryDelay;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = retryDelay,
                MaxDelay = maxRetryDelay,
                BackoffType = options.BackoffType,
                UseJitter = options.UseJitter,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is { } ex &&
                    AIProviderErrorHelper.IsRateLimitException(ex)),
            })
            .Build();
    }
}
#pragma warning restore MEAI001
