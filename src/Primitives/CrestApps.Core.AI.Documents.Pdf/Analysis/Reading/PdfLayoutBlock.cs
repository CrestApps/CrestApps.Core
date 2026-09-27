using System.Runtime.InteropServices;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A block of text on a page — a heading, a paragraph, a list item, a caption — with the role its layout
/// suggests.
/// </summary>
internal sealed class PdfLayoutBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutBlock"/> class.
    /// </summary>
    /// <param name="lines">The lines, top to bottom.</param>
    public PdfLayoutBlock(List<PdfLayoutLine> lines)
    {
        Lines = lines;
        Box = lines.Count == 0
            ? default
            : lines.Skip(1).Aggregate(lines[0].Box, (box, line) => box.Union(line.Box));
        Style = DominantStyle(lines);
        Text = string.Join('\n', lines.Select(line => line.Text));
    }

    /// <summary>
    /// Gets the lines, top to bottom.
    /// </summary>
    public List<PdfLayoutLine> Lines { get; }

    /// <summary>
    /// Gets where the block is drawn, in user space.
    /// </summary>
    public PdfBox Box { get; }

    /// <summary>
    /// Gets the style most of the block's text is set in.
    /// </summary>
    public PdfTextStyle Style { get; }

    /// <summary>
    /// Gets the block's text, one line per line.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets or sets the block's role, one of <see cref="PdfLayoutRoles"/>.
    /// </summary>
    public string Role { get; set; }

    /// <summary>
    /// Gets or sets a heading's level, one for the most prominent.
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// Gets or sets the one-based column the block sits in, or <see langword="null"/> when it spans columns.
    /// </summary>
    public int? Column { get; set; }

    /// <summary>
    /// Gets or sets the block's one-based place in the page's reading order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the table or figure a caption describes.
    /// </summary>
    public PdfRegion CaptionOf { get; set; }

    private static PdfTextStyle DominantStyle(List<PdfLayoutLine> lines)
    {
        var weights = new Dictionary<PdfTextStyle, int>();

        foreach (var line in lines)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(weights, line.Style, out _) += line.Text.Length;
        }

        var best = default(PdfTextStyle);
        var bestWeight = -1;

        foreach (var (style, weight) in weights)
        {
            if (weight > bestWeight)
            {
                best = style;
                bestWeight = weight;
            }
        }

        return best;
    }
}
