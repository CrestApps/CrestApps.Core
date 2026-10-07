namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Keys a provider may set in <c>UsageDetails.AdditionalCounts</c> to report billable units that are not tokens.
/// Usage metering copies them onto the matching <see cref="Models.AICompletionUsageRecord"/> properties, so a
/// provider that knows a unit better than the metering client can measure it reports it this way.
/// </summary>
public static class AIUsageAdditionalCounts
{
    /// <summary>
    /// The length of the audio transcribed, in milliseconds. Copied to
    /// <see cref="Models.AICompletionUsageRecord.AudioDurationMs"/>.
    /// </summary>
    public const string AudioDurationMs = "AudioDurationMs";

    /// <summary>
    /// The number of characters synthesized. Copied to <see cref="Models.AICompletionUsageRecord.CharacterCount"/>.
    /// </summary>
    public const string CharacterCount = "CharacterCount";

    /// <summary>
    /// The number of images generated. Copied to <see cref="Models.AICompletionUsageRecord.ImageCount"/>.
    /// </summary>
    public const string ImageCount = "ImageCount";
}
