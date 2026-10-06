namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// The document-wide look a word-processing document is written with: its typefaces, sizes, colors and
/// spacing. The style sheet is generated from it, so headings, body text, lists, quotes and tables all
/// follow one design and a change to it restyles the whole document.
/// </summary>
internal sealed class WordDesign
{
    /// <summary>
    /// Gets or sets the name of the preset the design started from.
    /// </summary>
    public string Preset { get; set; } = "default";

    /// <summary>
    /// Gets or sets the body typeface.
    /// </summary>
    public string BodyFont { get; set; } = "Calibri";

    /// <summary>
    /// Gets or sets the heading and title typeface.
    /// </summary>
    public string HeadingFont { get; set; } = "Calibri Light";

    /// <summary>
    /// Gets or sets the typeface code is set in.
    /// </summary>
    public string CodeFont { get; set; } = "Consolas";

    /// <summary>
    /// Gets or sets the body text size in points.
    /// </summary>
    public double BodySize { get; set; } = 11;

    /// <summary>
    /// Gets or sets the body text color.
    /// </summary>
    public string TextColor { get; set; } = "262626";

    /// <summary>
    /// Gets or sets the heading color.
    /// </summary>
    public string HeadingColor { get; set; } = "1F3864";

    /// <summary>
    /// Gets or sets the accent color: rules, quote bars and table headers.
    /// </summary>
    public string AccentColor { get; set; } = "2F5496";

    /// <summary>
    /// Gets or sets the hyperlink color.
    /// </summary>
    public string LinkColor { get; set; } = "0563C1";

    /// <summary>
    /// Gets or sets the line spacing as a multiple of single spacing.
    /// </summary>
    public double LineSpacing { get; set; } = 1.15;

    /// <summary>
    /// Gets or sets the space after a body paragraph, in points.
    /// </summary>
    public double ParagraphSpacing { get; set; } = 8;

    /// <summary>
    /// Gets or sets the fill of a table's header row.
    /// </summary>
    public string TableHeaderFill { get; set; } = "2F5496";

    /// <summary>
    /// Gets or sets the text color of a table's header row.
    /// </summary>
    public string TableHeaderTextColor { get; set; } = "FFFFFF";

    /// <summary>
    /// Gets or sets the color of table borders.
    /// </summary>
    public string TableBorderColor { get; set; } = "BFBFBF";

    /// <summary>
    /// Gets or sets the fill of every other table row, or <see langword="null"/> for no banding.
    /// </summary>
    public string TableBandFill { get; set; } = "F2F2F2";

    /// <summary>
    /// Gets the names of the presets a design can start from.
    /// </summary>
    public static IReadOnlyList<string> PresetNames { get; } = ["default", "professional", "modern", "classic", "minimal", "vibrant", "elegant"];

    /// <summary>
    /// Creates the design a preset describes.
    /// </summary>
    /// <param name="preset">The preset name; an unknown or empty name gives the default design.</param>
    /// <returns>The design.</returns>
    public static WordDesign FromPreset(string preset)
    {
        var name = string.IsNullOrWhiteSpace(preset) ? "default" : preset.Trim().ToLowerInvariant();

        return name switch
        {
            "professional" or "corporate" or "business" => new WordDesign
            {
                Preset = "professional",
                BodyFont = "Calibri",
                HeadingFont = "Calibri",
                HeadingColor = "1F3864",
                AccentColor = "1F3864",
                TableHeaderFill = "1F3864",
                TableBandFill = "EDF1F8",
            },
            "modern" => new WordDesign
            {
                Preset = "modern",
                BodyFont = "Segoe UI",
                HeadingFont = "Segoe UI Semibold",
                BodySize = 10.5,
                TextColor = "333333",
                HeadingColor = "0F6E73",
                AccentColor = "14919B",
                TableHeaderFill = "0F6E73",
                TableBandFill = "E8F4F5",
                LineSpacing = 1.2,
            },
            "classic" or "academic" or "serif" => new WordDesign
            {
                Preset = "classic",
                BodyFont = "Cambria",
                HeadingFont = "Cambria",
                BodySize = 11.5,
                TextColor = "1A1A1A",
                HeadingColor = "1A1A1A",
                AccentColor = "7F6000",
                TableHeaderFill = "404040",
                TableBandFill = "F2F2F2",
                LineSpacing = 1.2,
            },
            "minimal" or "simple" or "plain" => new WordDesign
            {
                Preset = "minimal",
                BodyFont = "Arial",
                HeadingFont = "Arial",
                BodySize = 10.5,
                TextColor = "222222",
                HeadingColor = "222222",
                AccentColor = "7F7F7F",
                TableHeaderFill = "E7E6E6",
                TableHeaderTextColor = "222222",
                TableBandFill = null,
            },
            "vibrant" or "bold" or "colorful" => new WordDesign
            {
                Preset = "vibrant",
                BodyFont = "Calibri",
                HeadingFont = "Calibri",
                HeadingColor = "C00060",
                AccentColor = "7030A0",
                TableHeaderFill = "7030A0",
                TableBandFill = "F3E9FA",
            },
            "elegant" => new WordDesign
            {
                Preset = "elegant",
                BodyFont = "Georgia",
                HeadingFont = "Georgia",
                BodySize = 11,
                TextColor = "2B2B2B",
                HeadingColor = "5A3E1B",
                AccentColor = "A67C52",
                TableHeaderFill = "5A3E1B",
                TableBandFill = "F7F1EA",
                LineSpacing = 1.25,
            },
            _ => new WordDesign(),
        };
    }

    /// <summary>
    /// Copies the design, so a change can be tried without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public WordDesign Clone()
    {
        return (WordDesign)MemberwiseClone();
    }
}
