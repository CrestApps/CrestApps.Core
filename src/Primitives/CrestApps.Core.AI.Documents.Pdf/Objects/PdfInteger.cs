namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A PDF whole number.
/// </summary>
internal sealed class PdfInteger : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfInteger"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    public PdfInteger(long value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the value.
    /// </summary>
    public long Value { get; }
}
