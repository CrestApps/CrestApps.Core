using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Query context used to filter AI profile listings.
/// </summary>
public sealed class AIProfileQueryContext : QueryContext
{
    /// <summary>
    /// Gets or sets a value indicating whether to return only listable profiles.
    /// </summary>
    public bool IsListableOnly { get; set; }
}
