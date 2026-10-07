using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

#pragma warning disable MEAI001 // The audio token counts are for evaluation purposes only and may change in future updates.
/// <summary>
/// Converts the token usage the OpenAI SDK reports into the provider-agnostic <see cref="UsageDetails"/>, keeping
/// the cached, reasoning, and audio token counts that are billed as separate meters.
/// </summary>
internal static class AzureOpenAIUsage
{
    /// <summary>
    /// Converts the SDK's token usage.
    /// </summary>
    /// <param name="usage">The SDK's token usage, or <see langword="null"/>.</param>
    /// <returns>The converted usage, or <see langword="null"/> when none was reported.</returns>
    public static UsageDetails ToUsageDetails(ChatTokenUsage usage)
    {
        if (usage is null)
        {
            return null;
        }

        return new UsageDetails
        {
            InputTokenCount = usage.InputTokenCount,
            OutputTokenCount = usage.OutputTokenCount,
            TotalTokenCount = usage.TotalTokenCount,
            CachedInputTokenCount = usage.InputTokenDetails?.CachedTokenCount,
            InputAudioTokenCount = usage.InputTokenDetails?.AudioTokenCount,
            ReasoningTokenCount = usage.OutputTokenDetails?.ReasoningTokenCount,
            OutputAudioTokenCount = usage.OutputTokenDetails?.AudioTokenCount,
        };
    }

    /// <summary>
    /// Adds the SDK's token usage for one more round trip to the usage gathered so far.
    /// </summary>
    /// <param name="total">The usage gathered so far, or <see langword="null"/>.</param>
    /// <param name="usage">The SDK's token usage for the round trip, or <see langword="null"/>.</param>
    /// <returns>The combined usage, or <see langword="null"/> when neither reported any.</returns>
    public static UsageDetails Add(UsageDetails total, ChatTokenUsage usage)
    {
        var details = ToUsageDetails(usage);

        if (details is null)
        {
            return total;
        }

        if (total is null)
        {
            return details;
        }

        total.Add(details);

        return total;
    }
}
#pragma warning restore MEAI001 // The audio token counts are for evaluation purposes only and may change in future updates.
