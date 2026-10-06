namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// How a picture is sized, placed and described.
/// </summary>
internal sealed class WordImageOptions
{
    /// <summary>
    /// Gets or sets the width in points, or <see langword="null"/> to follow the height or the natural size.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Gets or sets the height in points, or <see langword="null"/> to follow the width or the natural size.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Gets or sets the alternative text a screen reader reads.
    /// </summary>
    public string AltText { get; set; }

    /// <summary>
    /// Gets or sets the picture's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the alignment: <c>left</c>, <c>center</c> or <c>right</c>.
    /// </summary>
    public string Alignment { get; set; }

    /// <summary>
    /// Gets or sets how text wraps: <c>inline</c> (the default), <c>square</c>, <c>tight</c>,
    /// <c>top_and_bottom</c>, <c>behind_text</c> or <c>in_front_of_text</c>.
    /// </summary>
    public string Wrap { get; set; }

    /// <summary>
    /// Gets or sets the horizontal offset of a floating picture from the margin, in points.
    /// </summary>
    public double? OffsetX { get; set; }

    /// <summary>
    /// Gets or sets the vertical offset of a floating picture from its paragraph, in points.
    /// </summary>
    public double? OffsetY { get; set; }

    /// <summary>
    /// Gets or sets how much is cropped from each edge, in percent of the picture: left, top, right, bottom.
    /// </summary>
    public double[] Crop { get; set; }

    /// <summary>
    /// Gets or sets the outline color, or <see langword="null"/> for none.
    /// </summary>
    public string BorderColor { get; set; }

    /// <summary>
    /// Gets or sets the outline width in points.
    /// </summary>
    public double? BorderWidth { get; set; }

    /// <summary>
    /// Gets a value indicating whether the picture floats rather than sitting in a line of text.
    /// </summary>
    public bool IsFloating => !string.IsNullOrWhiteSpace(Wrap) && !string.Equals(Wrap.Trim(), "inline", StringComparison.OrdinalIgnoreCase);
}
