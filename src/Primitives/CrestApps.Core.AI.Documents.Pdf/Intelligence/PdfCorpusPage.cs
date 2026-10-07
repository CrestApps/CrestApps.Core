namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// The text of one page of a PDF, in reading order.
/// </summary>
/// <param name="Document">The name the document is cited by.</param>
/// <param name="Number">The one-based page number.</param>
/// <param name="Text">The page's text; empty when it has none.</param>
internal sealed record PdfCorpusPage(string Document, int Number, string Text);
