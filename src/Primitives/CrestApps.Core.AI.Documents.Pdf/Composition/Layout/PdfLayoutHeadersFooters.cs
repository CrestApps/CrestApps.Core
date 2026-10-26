namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A section's headers or footers: the one every page carries, and the one its first page carries instead
/// when <see cref="PdfLayoutPageSetup.DifferentFirstPageHeaderFooter"/> is set.
/// </summary>
internal sealed class PdfLayoutHeadersFooters
{
    /// <summary>
    /// Gets the header or footer every page carries.
    /// </summary>
    public PdfLayoutHeaderFooter Primary { get; } = new();

    /// <summary>
    /// Gets the header or footer the first page carries instead.
    /// </summary>
    public PdfLayoutHeaderFooter FirstPage { get; } = new();
}
