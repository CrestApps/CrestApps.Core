using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Chat.Hubs;

/// <summary>
/// Builds the additional properties that label the usage of a hub's speech-to-text and text-to-speech requests.
/// </summary>
internal static class SpeechUsageProperties
{
    /// <summary>
    /// Creates the additional properties for a speech request.
    /// </summary>
    /// <param name="contextType">The usage category, such as a chat session or a chat interaction.</param>
    /// <param name="purpose">The usage purpose, one of <see cref="AIUsagePurposes"/>.</param>
    public static AdditionalPropertiesDictionary Create(string contextType, string purpose)
    {
        return new AdditionalPropertiesDictionary
        {
            [AICompletionContextKeys.UsageContextType] = contextType,
            [AICompletionContextKeys.UsagePurpose] = purpose,
        };
    }
}
