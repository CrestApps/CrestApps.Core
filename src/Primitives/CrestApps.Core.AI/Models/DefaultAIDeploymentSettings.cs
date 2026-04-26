using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Global site settings for default AI deployments by type.
/// Configurable under Settings >> Artificial Intelligence >> Default Deployments.
/// These provide the ultimate fallback when a specific deployment of a given type
/// is not configured at the connection or profile level.
/// </summary>
public sealed class DefaultAIDeploymentSettings
{
    /// <summary>
    /// Gets or sets the default chat deployment technical name.
    /// Used globally for chat sessions and chat interactions when no specific chat deployment is configured.
    /// </summary>
    public string DefaultChatDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the default utility deployment technical name.
    /// Used globally for auxiliary tasks such as planning, intent detection,
    /// query rewriting, and chart generation when no specific utility deployment is configured.
    /// </summary>
    public string DefaultUtilityDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the default embedding deployment technical name.
    /// Used globally for embedding generation in document indexing, data sources,
    /// and vector search when no specific embedding deployment is configured.
    /// </summary>
    public string DefaultEmbeddingDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the default image generation deployment technical name.
    /// Used globally for image generation when no specific image deployment is configured.
    /// </summary>
    public string DefaultImageDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the default speech-to-text deployment technical name.
    /// Used globally for speech-to-text transcription when no specific deployment is configured.
    /// </summary>
    public string DefaultSpeechToTextDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the default text-to-speech deployment technical name.
    /// Used globally for text-to-speech synthesis when no specific deployment is configured.
    /// </summary>
    public string DefaultTextToSpeechDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the default Chat Deployment ID.
    /// </summary>
    [Obsolete("Use DefaultChatDeploymentName instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public string DefaultChatDeploymentId
    {
        get => DefaultChatDeploymentName;
        set => DefaultChatDeploymentName = value;
    }

    /// <summary>
    /// Gets or sets the default Utility Deployment ID.
    /// </summary>
    [Obsolete("Use DefaultUtilityDeploymentName instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public string DefaultUtilityDeploymentId
    {
        get => DefaultUtilityDeploymentName;
        set => DefaultUtilityDeploymentName = value;
    }

    /// <summary>
    /// Gets or sets the default Embedding Deployment ID.
    /// </summary>
    [Obsolete("Use DefaultEmbeddingDeploymentName instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public string DefaultEmbeddingDeploymentId
    {
        get => DefaultEmbeddingDeploymentName;
        set => DefaultEmbeddingDeploymentName = value;
    }

    /// <summary>
    /// Gets or sets the default Image Deployment ID.
    /// </summary>
    [Obsolete("Use DefaultImageDeploymentName instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public string DefaultImageDeploymentId
    {
        get => DefaultImageDeploymentName;
        set => DefaultImageDeploymentName = value;
    }

    /// <summary>
    /// Gets or sets the default Speech To Text Deployment ID.
    /// </summary>
    [Obsolete("Use DefaultSpeechToTextDeploymentName instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public string DefaultSpeechToTextDeploymentId
    {
        get => DefaultSpeechToTextDeploymentName;
        set => DefaultSpeechToTextDeploymentName = value;
    }

    /// <summary>
    /// Gets or sets the default Text To Speech Deployment ID.
    /// </summary>
    [Obsolete("Use DefaultTextToSpeechDeploymentName instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public string DefaultTextToSpeechDeploymentId
    {
        get => DefaultTextToSpeechDeploymentName;
        set => DefaultTextToSpeechDeploymentName = value;
    }

    /// <summary>
    /// Gets the legacy Default Chat Deployment ID.
    /// </summary>
    [JsonPropertyName("DefaultChatDeploymentId")]
    public string LegacyDefaultChatDeploymentId
    {
        set => DefaultChatDeploymentName = value;
    }

    /// <summary>
    /// Gets the legacy Default Utility Deployment ID.
    /// </summary>
    [JsonPropertyName("DefaultUtilityDeploymentId")]
    public string LegacyDefaultUtilityDeploymentId
    {
        set => DefaultUtilityDeploymentName = value;
    }

    /// <summary>
    /// Gets the legacy Default Embedding Deployment ID.
    /// </summary>
    [JsonPropertyName("DefaultEmbeddingDeploymentId")]
    public string LegacyDefaultEmbeddingDeploymentId
    {
        set => DefaultEmbeddingDeploymentName = value;
    }

    /// <summary>
    /// Gets the legacy Default Image Deployment ID.
    /// </summary>
    [JsonPropertyName("DefaultImageDeploymentId")]
    public string LegacyDefaultImageDeploymentId
    {
        set => DefaultImageDeploymentName = value;
    }

    /// <summary>
    /// Gets the legacy Default Speech To Text Deployment ID.
    /// </summary>
    [JsonPropertyName("DefaultSpeechToTextDeploymentId")]
    public string LegacyDefaultSpeechToTextDeploymentId
    {
        set => DefaultSpeechToTextDeploymentName = value;
    }

    /// <summary>
    /// Gets the legacy Default Text To Speech Deployment ID.
    /// </summary>
    [JsonPropertyName("DefaultTextToSpeechDeploymentId")]
    public string LegacyDefaultTextToSpeechDeploymentId
    {
        set => DefaultTextToSpeechDeploymentName = value;
    }

    /// <summary>
    /// Gets or sets the default voice identifier for text-to-speech synthesis.
    /// When a profile does not specify a voice, this value is used as the fallback.
    /// </summary>
    public string DefaultTextToSpeechVoiceId { get; set; }
}
