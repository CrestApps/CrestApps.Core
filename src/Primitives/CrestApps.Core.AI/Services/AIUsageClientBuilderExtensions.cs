using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // Several of these builder types are for evaluation purposes only and may change in future updates.
namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Labels the usage of every request a client makes with a category and a purpose, so usage reports can be broken
/// down by the feature that made it. Every client the AI client factory creates is metered whether or not these are
/// used; they only name what the requests were for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage:</b>
/// <code>
/// var client = await clientFactory.CreateChatClientAsync(deployment, builder => builder
///     .UseDefaultResilience()
///     .UseUsageLabels(contextType: "Leads", purpose: "LeadScoring"));
/// </code>
/// </para>
/// <para>
/// A label set on a single request through <see cref="AICompletionContextKeys.UsageContextType"/> or
/// <see cref="AICompletionContextKeys.UsagePurpose"/> takes precedence over the client's. The client's labels take
/// precedence over the current <see cref="AIUsageScope"/>, and a label left <see langword="null"/> falls back to it.
/// Because the labels travel on each request rather than on the async flow, they also hold for streamed responses
/// read from async iterators, where an <see cref="AIUsageScope"/> begun inside the iterator would not.
/// </para>
/// </remarks>
public static class AIUsageClientBuilderExtensions
{
    /// <summary>
    /// Labels the usage of every request sent through the chat client.
    /// </summary>
    /// <param name="builder">The chat-client builder.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <returns>The updated builder.</returns>
    public static ChatClientBuilder UseUsageLabels(
        this ChatClientBuilder builder,
        string contextType = null,
        string purpose = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(innerClient => new AIUsageLabelingChatClient(innerClient, contextType, purpose));
    }

    /// <summary>
    /// Labels the usage of every request sent through the embedding generator.
    /// </summary>
    /// <typeparam name="TInput">The embedding input type.</typeparam>
    /// <typeparam name="TEmbedding">The embedding result type.</typeparam>
    /// <param name="builder">The embedding-generator builder.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <returns>The updated builder.</returns>
    public static EmbeddingGeneratorBuilder<TInput, TEmbedding> UseUsageLabels<TInput, TEmbedding>(
        this EmbeddingGeneratorBuilder<TInput, TEmbedding> builder,
        string contextType = null,
        string purpose = null)
        where TEmbedding : Embedding
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(innerGenerator => new AIUsageLabelingEmbeddingGenerator<TInput, TEmbedding>(innerGenerator, contextType, purpose));
    }

    /// <summary>
    /// Labels the usage of every request sent through the image generator.
    /// </summary>
    /// <param name="builder">The image-generator builder.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <returns>The updated builder.</returns>
    public static ImageGeneratorBuilder UseUsageLabels(
        this ImageGeneratorBuilder builder,
        string contextType = null,
        string purpose = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(innerGenerator => new AIUsageLabelingImageGenerator(innerGenerator, contextType, purpose));
    }

    /// <summary>
    /// Labels the usage of every transcription sent through the speech-to-text client.
    /// </summary>
    /// <param name="builder">The speech-to-text builder.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <returns>The updated builder.</returns>
    public static SpeechToTextClientBuilder UseUsageLabels(
        this SpeechToTextClientBuilder builder,
        string contextType = null,
        string purpose = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(innerClient => new AIUsageLabelingSpeechToTextClient(innerClient, contextType, purpose));
    }

    /// <summary>
    /// Labels the usage of every synthesis sent through the text-to-speech client.
    /// </summary>
    /// <param name="builder">The text-to-speech builder.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/> to leave it to the request or scope.</param>
    /// <returns>The updated builder.</returns>
    public static TextToSpeechClientBuilder UseUsageLabels(
        this TextToSpeechClientBuilder builder,
        string contextType = null,
        string purpose = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(innerClient => new AIUsageLabelingTextToSpeechClient(innerClient, contextType, purpose));
    }

    /// <summary>
    /// Labels the usage of every session started through the realtime client: each response and transcription the
    /// session produces is recorded with these labels.
    /// </summary>
    /// <param name="builder">The realtime-client builder.</param>
    /// <param name="contextType">The category to record, or <see langword="null"/> to leave it to the scope.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/> to leave it to the scope.</param>
    /// <returns>The updated builder.</returns>
    public static RealtimeClientBuilder UseUsageLabels(
        this RealtimeClientBuilder builder,
        string contextType = null,
        string purpose = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(innerClient => new AIUsageLabelingRealtimeClient(innerClient, contextType, purpose));
    }
}
#pragma warning restore MEAI001 // Several of these builder types are for evaluation purposes only and may change in future updates.
