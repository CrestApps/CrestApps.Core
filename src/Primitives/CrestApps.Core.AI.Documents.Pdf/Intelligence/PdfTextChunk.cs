namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// A piece of a document short enough for a model to read in one pass.
/// </summary>
/// <param name="Pages">The pages the chunk holds text from, in order.</param>
/// <param name="Text">The text, each page introduced by its label such as <c>[Page 3]</c>.</param>
internal sealed record PdfTextChunk(IReadOnlyList<int> Pages, string Text);
