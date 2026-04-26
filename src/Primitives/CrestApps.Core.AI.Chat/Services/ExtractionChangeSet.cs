namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Represents the extraction Change Set.
/// </summary>
public sealed class ExtractionChangeSet
{
    /// <summary>
    /// Gets or sets the new Fields.
    /// </summary>
    public List<ExtractedFieldChange> NewFields { get; set; } = [];

    /// <summary>
    /// Gets or sets the session Ended.
    /// </summary>
    public bool SessionEnded { get; set; }

    /// <summary>
    /// Gets or sets whether all configured extraction fields have been collected.
    /// </summary>
    public bool AllFieldsCollected { get; set; }
}
