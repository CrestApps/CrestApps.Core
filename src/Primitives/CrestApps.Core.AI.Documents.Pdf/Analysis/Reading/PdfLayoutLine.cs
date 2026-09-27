namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One line of text as a page shows it.
/// </summary>
/// <param name="Text">The words of the line, joined by single spaces.</param>
/// <param name="Box">Where the line is drawn, in user space.</param>
/// <param name="Style">The style most of the line is set in.</param>
internal sealed record PdfLayoutLine(string Text, PdfBox Box, PdfTextStyle Style);
