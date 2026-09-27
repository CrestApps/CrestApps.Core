using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One place a search found on a page.
/// </summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="Text">The matched text.</param>
/// <param name="Snippet">The match with some of the text around it.</param>
/// <param name="Boxes">Where the match is drawn, one box per line it spans, in user space.</param>
internal sealed record PdfTextMatch(int PageNumber, string Text, string Snippet, IReadOnlyList<PdfBox> Boxes)
{
    /// <summary>
    /// Gets the glyphs the match is drawn with, in order, so a replacement can be set in the same size and
    /// colour on the same baseline.
    /// </summary>
    public IReadOnlyList<Letter> Letters { get; init; } = [];

    /// <summary>
    /// Gets the kind of value the match is, when it was found by <see cref="PdfPatternLibrary"/>.
    /// </summary>
    public string Kind { get; init; }
}
