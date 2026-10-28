using CrestApps.Core.AI.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// One row per <see cref="AIProfileVersion"/>, by profile and version number.
/// </summary>
public sealed class AIProfileVersionIndex : MapIndex
{
    /// <summary>
    /// Gets or sets the identifier of the version.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the profile.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the version number.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets when the version was saved.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
}
