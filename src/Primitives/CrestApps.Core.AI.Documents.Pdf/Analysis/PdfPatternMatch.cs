namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One value <see cref="PdfPatternLibrary"/> recognised in a text.
/// </summary>
/// <param name="Kind">The kind, such as <see cref="PdfPatternLibrary.Email"/>.</param>
/// <param name="Value">The value as written.</param>
/// <param name="Index">Where the value starts in the text.</param>
/// <param name="Length">How many characters it spans.</param>
internal sealed record PdfPatternMatch(string Kind, string Value, int Index, int Length);
