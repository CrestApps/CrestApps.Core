namespace CrestApps.Core.AI.Models;

/// <summary>
/// Records a single AI completion usage event, capturing token counts, latency, identifiers,
/// and context metadata for billing and analytics.
/// </summary>
public sealed class AICompletionUsageRecord : ExtensibleEntity
{
    /// <summary>
    /// Gets or sets the category of the feature that made the request, such as <c>AIChatSession</c> for
    /// chat sessions, <c>ChatInteraction</c> for chat interactions, or a custom category a caller sets
    /// through <see cref="Completions.AIUsageScope"/>.
    /// </summary>
    public string ContextType { get; set; }

    /// <summary>
    /// Gets or sets the kind of request that was metered. Well-known values are listed in
    /// <see cref="Completions.AIUsageOperationTypes"/>.
    /// </summary>
    public string OperationType { get; set; }

    /// <summary>
    /// Gets or sets why the request was made within its category, such as answering the user, generating a
    /// search query, or indexing documents. Well-known values are listed in
    /// <see cref="Completions.AIUsagePurposes"/>, and callers may use their own.
    /// </summary>
    public string Purpose { get; set; }

    /// <summary>
    /// Gets or sets the chat session identifier associated with this usage.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the AI profile identifier used for this completion.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the interaction identifier that groups related prompts and responses.
    /// </summary>
    public string InteractionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the authenticated user who triggered this completion.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the username of the user who triggered this completion.
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    /// Gets or sets the visitor identifier for anonymous users.
    /// </summary>
    public string VisitorId { get; set; }

    /// <summary>
    /// Gets or sets the client identifier (e.g., browser fingerprint or device ID).
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request came from an authenticated user.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets the registered AI client name used for the completion.
    /// </summary>
    public string ClientName { get; set; }

    /// <summary>
    /// Gets or sets the provider connection name used for the completion.
    /// </summary>
    public string ConnectionName { get; set; }

    /// <summary>
    /// Gets or sets the deployment name that resolved the model for this completion.
    /// </summary>
    public string DeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the model name reported by the AI provider.
    /// </summary>
    public string ModelName { get; set; }

    /// <summary>
    /// Gets or sets the response identifier returned by the AI provider.
    /// </summary>
    public string ResponseId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the response was streamed.
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// Gets or sets the number of tokens in the input (prompt) sent to the model.
    /// </summary>
    public int InputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the number of tokens in the output (completion) returned by the model.
    /// </summary>
    public int OutputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the total token count (input + output).
    /// </summary>
    public int TotalTokenCount { get; set; }

    /// <summary>
    /// Gets or sets how many of the input tokens were served from the provider's prompt cache. Providers
    /// usually bill these at a discount, so they are a separate billing meter from regular input tokens.
    /// </summary>
    public int CachedInputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets how many of the output tokens the model spent on reasoning before answering.
    /// </summary>
    public int ReasoningTokenCount { get; set; }

    /// <summary>
    /// Gets or sets how many of the input tokens were audio tokens.
    /// </summary>
    public int InputAudioTokenCount { get; set; }

    /// <summary>
    /// Gets or sets how many of the output tokens were audio tokens.
    /// </summary>
    public int OutputAudioTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the length of the audio that was transcribed, in milliseconds, for providers that bill
    /// speech-to-text by audio duration rather than by tokens.
    /// </summary>
    public long AudioDurationMs { get; set; }

    /// <summary>
    /// Gets or sets how many characters of text were synthesized, for providers that bill text-to-speech by
    /// character.
    /// </summary>
    public int CharacterCount { get; set; }

    /// <summary>
    /// Gets or sets how many images were generated, for providers that bill image generation per image.
    /// </summary>
    public int ImageCount { get; set; }

    /// <summary>
    /// Gets or sets the wall-clock latency in milliseconds from request to first response byte.
    /// </summary>
    public double ResponseLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this usage record was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
}
