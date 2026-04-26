namespace CrestApps.Core.AI.Models;

/// <summary>
/// Holds the extracted state for a single data-extraction field across the lifetime of a session.
/// </summary>
public sealed class ExtractedFieldState
{
    /// <summary>
    /// Gets or sets the extracted values. Always a list, even for single-value fields.
    /// </summary>
    public List<string> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the UTC timestamp of the last extraction.
    /// </summary>
    public DateTime? LastExtractedUtc { get; set; }
}
