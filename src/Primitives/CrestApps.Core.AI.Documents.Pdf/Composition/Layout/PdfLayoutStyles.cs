using System.Globalization;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// The paragraph styles of a document: <see cref="Normal"/>, the nine heading levels, the header and the
/// footer, and any added.
/// </summary>
internal sealed class PdfLayoutStyles
{
    /// <summary>
    /// The style every other is based on.
    /// </summary>
    public const string Normal = "Normal";

    /// <summary>
    /// The style headers are set in.
    /// </summary>
    public const string Header = "Header";

    /// <summary>
    /// The style footers are set in.
    /// </summary>
    public const string Footer = "Footer";

    private readonly Dictionary<string, PdfLayoutStyle> _styles = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutStyles"/> class with the built-in styles.
    /// </summary>
    public PdfLayoutStyles()
    {
        var normal = AddStyle(Normal, null);

        normal.Font.Name = "Arial";
        normal.Font.Size = 10;
        normal.Font.Bold = false;
        normal.Font.Italic = false;
        normal.Font.Underline = false;
        normal.Font.Color = new PdfColor(0, 0, 0);
        normal.ParagraphFormat.Alignment = PdfLayoutAlignment.Left;
        normal.ParagraphFormat.LineSpacingRule = PdfLayoutLineSpacing.Single;
        normal.ParagraphFormat.OutlineLevel = 0;

        // A heading level is the outline level of its paragraphs.
        for (var level = 1; level <= 9; level++)
        {
            AddStyle(Heading(level), Normal).ParagraphFormat.OutlineLevel = level;
        }

        AddStyle(Header, Normal);
        AddStyle(Footer, Normal);
    }

    /// <summary>
    /// Gets a style.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The style.</returns>
    public PdfLayoutStyle this[string name] => _styles[name];

    /// <summary>
    /// Returns the name of a heading level's style.
    /// </summary>
    /// <param name="level">The level, 1 to 9.</param>
    /// <returns>The name.</returns>
    public static string Heading(int level)
    {
        return "Heading" + Math.Clamp(level, 1, 9).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Adds a style.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="baseStyle">The name of the style it is based on, or <see langword="null"/>.</param>
    /// <returns>The style.</returns>
    public PdfLayoutStyle AddStyle(string name, string baseStyle)
    {
        var style = new PdfLayoutStyle(name, baseStyle);

        _styles[name] = style;

        return style;
    }

    /// <summary>
    /// Returns the format a style gives, with everything the styles it is based on give.
    /// </summary>
    /// <param name="name">The style's name, or <see langword="null"/> for <see cref="Normal"/>.</param>
    /// <returns>The format.</returns>
    public PdfLayoutParagraphFormat Resolve(string name)
    {
        var format = new PdfLayoutParagraphFormat();
        var current = name ?? Normal;

        for (var depth = 0; depth < 16 && current is not null && _styles.TryGetValue(current, out var style); depth++)
        {
            format = format.Over(style.ParagraphFormat);
            current = style.BaseStyle;
        }

        return format;
    }
}
