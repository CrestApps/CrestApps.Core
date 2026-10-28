namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A PDF number with a fractional part.
/// </summary>
internal sealed class PdfReal : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfReal"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    public PdfReal(double value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the value.
    /// </summary>
    public double Value { get; }
}
