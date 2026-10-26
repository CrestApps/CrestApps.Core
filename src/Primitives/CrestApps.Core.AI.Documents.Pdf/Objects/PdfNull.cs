namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The PDF null value.
/// </summary>
internal sealed class PdfNull : PdfItem
{
    private PdfNull()
    {
    }

    /// <summary>
    /// Gets the one null value.
    /// </summary>
    public static PdfNull Value { get; } = new();
}
