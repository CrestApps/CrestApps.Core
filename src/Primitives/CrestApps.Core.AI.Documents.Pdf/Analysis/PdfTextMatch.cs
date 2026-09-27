namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One place a search found on a page.
/// </summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="Text">The matched text.</param>
/// <param name="Snippet">The match with some of the text around it.</param>
/// <param name="Boxes">Where the match is drawn, one box per line it spans, in user space.</param>
internal sealed record PdfTextMatch(int PageNumber, string Text, string Snippet, IReadOnlyList<PdfBox> Boxes);
