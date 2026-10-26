namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A PDF array.
/// </summary>
internal sealed class PdfArray : PdfObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfArray"/> class.
    /// </summary>
    /// <param name="owner">The document the array belongs to, or <see langword="null"/>.</param>
    /// <param name="items">The items the array starts with.</param>
    public PdfArray(PdfDocument owner, params IEnumerable<PdfItem> items)
        : base(owner)
    {
        Elements = [.. items ?? []];
    }

    /// <summary>
    /// Gets the items of the array, as stored: an item stored elsewhere is a <see cref="PdfReference"/>.
    /// </summary>
    public List<PdfItem> Elements { get; }
}
