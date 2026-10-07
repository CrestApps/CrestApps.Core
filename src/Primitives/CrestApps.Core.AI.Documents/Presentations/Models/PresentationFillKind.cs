namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// How an area is painted.
/// </summary>
public enum PresentationFillKind
{
    /// <summary>
    /// Nothing is painted; whatever is behind shows through.
    /// </summary>
    None,

    /// <summary>
    /// A single colour.
    /// </summary>
    Solid,

    /// <summary>
    /// A blend between two or more colours.
    /// </summary>
    Gradient,

    /// <summary>
    /// A picture stretched or tiled over the area.
    /// </summary>
    Picture,

    /// <summary>
    /// A two-colour pattern, approximated by its foreground colour.
    /// </summary>
    Pattern,
}
