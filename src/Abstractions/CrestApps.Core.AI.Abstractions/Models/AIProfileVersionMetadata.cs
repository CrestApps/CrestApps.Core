namespace CrestApps.Core.AI.Models;

/// <summary>
/// The version an AI profile is at, kept on the profile, and on each chat session it answers so reports can compare
/// versions.
/// </summary>
public sealed class AIProfileVersionMetadata
{
    /// <summary>
    /// Gets or sets the version number.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets a note for the version the next save makes. Cleared once that version is kept.
    /// </summary>
    public string PendingNote { get; set; }
}
