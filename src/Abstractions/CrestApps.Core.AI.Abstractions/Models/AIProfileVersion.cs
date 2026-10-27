using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// A saved version of an AI profile: the whole profile as it was saved, numbered from 1 for each profile.
/// </summary>
public sealed class AIProfileVersion : CatalogItem
{
    /// <summary>
    /// Gets or sets the profile this is a version of.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the version number, from 1.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets when the version was saved.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets who saved it.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets a note on the version, such as the version it was restored from.
    /// </summary>
    public string Note { get; set; }

    /// <summary>
    /// Gets or sets the profile as it was saved.
    /// </summary>
    public AIProfile Snapshot { get; set; }
}
