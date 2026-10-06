using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// One thing drawn on a laid-out page, positioned in points from the page's top-left corner.
/// </summary>
internal abstract class WordDrawItem
{
    /// <summary>
    /// Gets or sets the block the item belongs to, so a block can be found and outlined on the page.
    /// </summary>
    public OpenXmlElement Source { get; set; }
}

/// <summary>
/// A run of text on one line.
/// </summary>
internal sealed class WordTextItem : WordDrawItem
{
    /// <summary>
    /// Gets or sets the left edge.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the baseline.
    /// </summary>
    public double Baseline { get; set; }

    /// <summary>
    /// Gets or sets the width the text was measured at.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the formatting.
    /// </summary>
    public WordResolvedRun Format { get; set; }

    /// <summary>
    /// Gets or sets the color tracked-change markup draws the text in, or <see langword="null"/>.
    /// </summary>
    public string MarkupColor { get; set; }
}

/// <summary>
/// A filled or outlined rectangle.
/// </summary>
internal sealed class WordRectItem : WordDrawItem
{
    /// <summary>
    /// Gets or sets the left edge.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the top edge.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the fill, or <see langword="null"/> for none.
    /// </summary>
    public string Fill { get; set; }

    /// <summary>
    /// Gets or sets the outline color, or <see langword="null"/> for none.
    /// </summary>
    public string Stroke { get; set; }

    /// <summary>
    /// Gets or sets the outline width.
    /// </summary>
    public double StrokeWidth { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether the corners are rounded.
    /// </summary>
    public bool Rounded { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the shape is an ellipse.
    /// </summary>
    public bool Ellipse { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the outline is dashed.
    /// </summary>
    public bool Dashed { get; set; }
}

/// <summary>
/// A straight line.
/// </summary>
internal sealed class WordLineItem : WordDrawItem
{
    /// <summary>
    /// Gets or sets the start's horizontal position.
    /// </summary>
    public double X1 { get; set; }

    /// <summary>
    /// Gets or sets the start's vertical position.
    /// </summary>
    public double Y1 { get; set; }

    /// <summary>
    /// Gets or sets the end's horizontal position.
    /// </summary>
    public double X2 { get; set; }

    /// <summary>
    /// Gets or sets the end's vertical position.
    /// </summary>
    public double Y2 { get; set; }

    /// <summary>
    /// Gets or sets the color.
    /// </summary>
    public string Color { get; set; } = "000000";

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets a value indicating whether the line is dotted.
    /// </summary>
    public bool Dotted { get; set; }
}

/// <summary>
/// A picture.
/// </summary>
internal sealed class WordImageItem : WordDrawItem
{
    /// <summary>
    /// Gets or sets the left edge.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the top edge.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the picture, or <see langword="null"/> when it is drawn as a labelled placeholder.
    /// </summary>
    public byte[] Bytes { get; set; }

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    public string MediaType { get; set; }

    /// <summary>
    /// Gets or sets the label a placeholder shows.
    /// </summary>
    public string Label { get; set; }
}
