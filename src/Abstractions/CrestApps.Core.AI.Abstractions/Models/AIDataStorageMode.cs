namespace CrestApps.Core.AI.Models;

/// <summary>
/// What is kept of an AI profile's conversations once they end.
/// </summary>
public enum AIDataStorageMode
{
    /// <summary>
    /// The transcript, summaries and extracted fields are kept as they were.
    /// </summary>
    Everything,

    /// <summary>
    /// Personal details (names, addresses, phone numbers, email addresses, card and identity numbers) are replaced
    /// with placeholders in the transcript, the title, summaries, extracted fields and whatever a privacy handler
    /// adds.
    /// </summary>
    RedactPersonalData,

    /// <summary>
    /// The transcript is deleted, and the title, summaries and extracted values are cleared. Times, status, outcomes
    /// and scores are kept.
    /// </summary>
    MetadataOnly,
}
