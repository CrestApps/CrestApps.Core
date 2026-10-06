using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// The paragraph formatting a paragraph can be given. Every property is optional: what is not set is left as
/// the paragraph's style has it.
/// </summary>
internal sealed class WordParagraphFormat
{
    /// <summary>
    /// Gets or sets the alignment: <c>left</c>, <c>center</c>, <c>right</c> or <c>justify</c>.
    /// </summary>
    public string Alignment { get; set; }

    /// <summary>
    /// Gets or sets the space before, in points.
    /// </summary>
    public double? SpaceBefore { get; set; }

    /// <summary>
    /// Gets or sets the space after, in points.
    /// </summary>
    public double? SpaceAfter { get; set; }

    /// <summary>
    /// Gets or sets the line spacing as a multiple of single spacing.
    /// </summary>
    public double? LineSpacing { get; set; }

    /// <summary>
    /// Gets or sets an exact line height, in points.
    /// </summary>
    public double? ExactLineHeight { get; set; }

    /// <summary>
    /// Gets or sets the left indent, in points.
    /// </summary>
    public double? IndentLeft { get; set; }

    /// <summary>
    /// Gets or sets the right indent, in points.
    /// </summary>
    public double? IndentRight { get; set; }

    /// <summary>
    /// Gets or sets the first-line indent, in points.
    /// </summary>
    public double? FirstLineIndent { get; set; }

    /// <summary>
    /// Gets or sets the hanging indent, in points.
    /// </summary>
    public double? HangingIndent { get; set; }

    /// <summary>
    /// Gets or sets whether the paragraph stays on the page of the one after it.
    /// </summary>
    public bool? KeepWithNext { get; set; }

    /// <summary>
    /// Gets or sets whether the paragraph's lines stay on one page.
    /// </summary>
    public bool? KeepLinesTogether { get; set; }

    /// <summary>
    /// Gets or sets whether the paragraph starts a new page.
    /// </summary>
    public bool? PageBreakBefore { get; set; }

    /// <summary>
    /// Gets or sets the paragraph's background fill.
    /// </summary>
    public string Shading { get; set; }

    /// <summary>
    /// Gets or sets a border: <c>top</c>, <c>bottom</c>, <c>left</c>, <c>box</c> or <c>none</c>.
    /// </summary>
    public string Border { get; set; }

    /// <summary>
    /// Gets or sets the border color.
    /// </summary>
    public string BorderColor { get; set; }

    /// <summary>
    /// Gets or sets the outline level from 1 to 9, making the paragraph a heading in the outline.
    /// </summary>
    public int? OutlineLevel { get; set; }

    /// <summary>
    /// Writes the format onto paragraph properties, changing only what is set.
    /// </summary>
    /// <param name="properties">The paragraph properties.</param>
    public void ApplyTo(ParagraphProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (OpenXml.Word.WordTableWriter.ReadAlignment(Alignment) is { } alignment)
        {
            properties.Justification = new Justification { Val = alignment };
        }

        if (KeepWithNext is not null)
        {
            properties.KeepNext = KeepWithNext.Value ? new KeepNext() : new KeepNext { Val = false };
        }

        if (KeepLinesTogether is not null)
        {
            properties.KeepLines = KeepLinesTogether.Value ? new KeepLines() : new KeepLines { Val = false };
        }

        if (PageBreakBefore is not null)
        {
            properties.PageBreakBefore = PageBreakBefore.Value ? new PageBreakBefore() : new PageBreakBefore { Val = false };
        }

        if (Shading is not null)
        {
            properties.Shading = WordColor.TryParse(Shading, out var fill)
                ? new Shading { Val = ShadingPatternValues.Clear, Fill = fill, Color = "auto" }
                : null;
        }

        if (Border is not null)
        {
            properties.ParagraphBorders = CreateBorders(Border, WordColor.ParseOrDefault(BorderColor, "7F7F7F"));
        }

        if (SpaceBefore is not null || SpaceAfter is not null || LineSpacing is not null || ExactLineHeight is not null)
        {
            var spacing = properties.SpacingBetweenLines ?? new SpacingBetweenLines();

            if (SpaceBefore is not null)
            {
                spacing.Before = WordStyleSheet.Twips(Math.Max(0, SpaceBefore.Value));
            }

            if (SpaceAfter is not null)
            {
                spacing.After = WordStyleSheet.Twips(Math.Max(0, SpaceAfter.Value));
            }

            if (ExactLineHeight is > 0)
            {
                spacing.Line = WordStyleSheet.Twips(ExactLineHeight.Value);
                spacing.LineRule = LineSpacingRuleValues.Exact;
            }
            else if (LineSpacing is > 0)
            {
                spacing.Line = WordStyleSheet.LineValue(LineSpacing.Value);
                spacing.LineRule = LineSpacingRuleValues.Auto;
            }

            properties.SpacingBetweenLines = spacing;
        }

        if (IndentLeft is not null || IndentRight is not null || FirstLineIndent is not null || HangingIndent is not null)
        {
            var indentation = properties.Indentation ?? new Indentation();

            if (IndentLeft is not null)
            {
                indentation.Left = WordStyleSheet.Twips(IndentLeft.Value);
            }

            if (IndentRight is not null)
            {
                indentation.Right = WordStyleSheet.Twips(IndentRight.Value);
            }

            if (FirstLineIndent is not null)
            {
                indentation.FirstLine = WordStyleSheet.Twips(Math.Max(0, FirstLineIndent.Value));
                indentation.Hanging = null;
            }

            if (HangingIndent is not null)
            {
                indentation.Hanging = WordStyleSheet.Twips(Math.Max(0, HangingIndent.Value));
                indentation.FirstLine = null;
            }

            properties.Indentation = indentation;
        }

        if (OutlineLevel is not null)
        {
            properties.OutlineLevel = OutlineLevel.Value is >= 1 and <= 9 ? new OutlineLevel { Val = OutlineLevel.Value - 1 } : null;
        }
    }

    private static ParagraphBorders CreateBorders(string border, string color)
    {
        var sides = (border ?? string.Empty).Trim().ToLowerInvariant();

        if (sides is "" or "none" or "false")
        {
            return null;
        }

        var borders = new ParagraphBorders();

        if (sides is "box" or "all" or "top")
        {
            borders.TopBorder = new TopBorder { Val = BorderValues.Single, Size = 6U, Space = 4U, Color = color };
        }

        if (sides is "box" or "all" or "left")
        {
            borders.LeftBorder = new LeftBorder { Val = BorderValues.Single, Size = sides == "left" ? 18U : 6U, Space = 4U, Color = color };
        }

        if (sides is "box" or "all" or "bottom" or "true")
        {
            borders.BottomBorder = new BottomBorder { Val = BorderValues.Single, Size = 6U, Space = 4U, Color = color };
        }

        if (sides is "box" or "all" or "right")
        {
            borders.RightBorder = new RightBorder { Val = BorderValues.Single, Size = 6U, Space = 4U, Color = color };
        }

        return borders;
    }
}
