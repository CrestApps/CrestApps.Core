using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// A piece of one laid-out line that is drawn in a single style.
/// </summary>
internal sealed class TextLayoutSpan
{
    /// <summary>
    /// Gets or sets the text, already capitalised as it is drawn.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the style it is drawn in; its size is the drawn size.
    /// </summary>
    public PresentationTextRun Style { get; set; }

    /// <summary>
    /// Gets or sets the estimated width in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the piece is only white space, which a wrapped line drops at
    /// its start and end.
    /// </summary>
    public bool IsSpace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the piece ends the line, as a soft line break does.
    /// </summary>
    public bool IsBreak { get; set; }
}
