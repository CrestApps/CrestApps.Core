namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// A short stretch of one page's text, the unit a search ranks and cites.
/// </summary>
/// <param name="Document">The name the document is cited by.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="Text">The passage.</param>
internal sealed record PdfPassage(string Document, int Page, string Text);
