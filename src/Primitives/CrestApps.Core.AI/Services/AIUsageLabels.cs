using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Reads and writes the usage labels a request carries in its options' additional properties.
/// </summary>
internal static class AIUsageLabels
{
    /// <summary>
    /// Returns a copy of the request's additional properties carrying the given labels. A label the request already
    /// carries is kept, so a value set on the request itself takes precedence over one set on its client.
    /// </summary>
    /// <param name="properties">The request's additional properties, or <see langword="null"/>.</param>
    /// <param name="contextType">The category to apply, or <see langword="null"/>.</param>
    /// <param name="purpose">The purpose to apply, or <see langword="null"/>.</param>
    public static AdditionalPropertiesDictionary Apply(AdditionalPropertiesDictionary properties, string contextType, string purpose)
    {
        var labeled = properties?.Clone() ?? [];

        if (!string.IsNullOrEmpty(contextType))
        {
            labeled.TryAdd(AICompletionContextKeys.UsageContextType, contextType);
        }

        if (!string.IsNullOrEmpty(purpose))
        {
            labeled.TryAdd(AICompletionContextKeys.UsagePurpose, purpose);
        }

        return labeled;
    }

    /// <summary>
    /// Returns the request's additional properties without the usage labels, so they are never sent to a provider,
    /// or the same instance when it carries none.
    /// </summary>
    /// <param name="properties">The request's additional properties, or <see langword="null"/>.</param>
    public static AdditionalPropertiesDictionary Remove(AdditionalPropertiesDictionary properties)
    {
        if (properties is null || !HasLabels(properties))
        {
            return properties;
        }

        var stripped = properties.Clone();
        stripped.Remove(AICompletionContextKeys.UsageContextType);
        stripped.Remove(AICompletionContextKeys.UsagePurpose);
        stripped.Remove(AICompletionContextKeys.DefaultUsagePurpose);

        return stripped;
    }

    /// <summary>
    /// Returns options the provider can be sent: the same instance when they carry no usage labels, otherwise a copy
    /// without them.
    /// </summary>
    /// <param name="options">The request options, or <see langword="null"/>.</param>
    public static ChatOptions ForProvider(ChatOptions options)
    {
        if (options?.AdditionalProperties is not { } properties || !HasLabels(properties))
        {
            return options;
        }

        var stripped = options.Clone();
        stripped.AdditionalProperties = Remove(properties);

        return stripped;
    }

    /// <summary>
    /// Returns options the provider can be sent: the same instance when they carry no usage labels, otherwise a copy
    /// without them.
    /// </summary>
    /// <param name="options">The request options, or <see langword="null"/>.</param>
    public static EmbeddingGenerationOptions ForProvider(EmbeddingGenerationOptions options)
    {
        if (options?.AdditionalProperties is not { } properties || !HasLabels(properties))
        {
            return options;
        }

        var stripped = options.Clone();
        stripped.AdditionalProperties = Remove(properties);

        return stripped;
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
    /// <summary>
    /// Returns options the provider can be sent: the same instance when they carry no usage labels, otherwise a copy
    /// without them.
    /// </summary>
    /// <param name="options">The request options, or <see langword="null"/>.</param>
    public static ImageGenerationOptions ForProvider(ImageGenerationOptions options)
    {
        if (options?.AdditionalProperties is not { } properties || !HasLabels(properties))
        {
            return options;
        }

        var stripped = options.Clone();
        stripped.AdditionalProperties = Remove(properties);

        return stripped;
    }

    /// <summary>
    /// Returns options the provider can be sent: the same instance when they carry no usage labels, otherwise a copy
    /// without them.
    /// </summary>
    /// <param name="options">The request options, or <see langword="null"/>.</param>
    public static SpeechToTextOptions ForProvider(SpeechToTextOptions options)
    {
        if (options?.AdditionalProperties is not { } properties || !HasLabels(properties))
        {
            return options;
        }

        var stripped = options.Clone();
        stripped.AdditionalProperties = Remove(properties);

        return stripped;
    }

    /// <summary>
    /// Returns options the provider can be sent: the same instance when they carry no usage labels, otherwise a copy
    /// without them.
    /// </summary>
    /// <param name="options">The request options, or <see langword="null"/>.</param>
    public static TextToSpeechOptions ForProvider(TextToSpeechOptions options)
    {
        if (options?.AdditionalProperties is not { } properties || !HasLabels(properties))
        {
            return options;
        }

        var stripped = options.Clone();
        stripped.AdditionalProperties = Remove(properties);

        return stripped;
    }
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.

    private static bool HasLabels(AdditionalPropertiesDictionary properties)
    {
        return properties.ContainsKey(AICompletionContextKeys.UsageContextType) ||
            properties.ContainsKey(AICompletionContextKeys.UsagePurpose) ||
            properties.ContainsKey(AICompletionContextKeys.DefaultUsagePurpose);
    }
}
