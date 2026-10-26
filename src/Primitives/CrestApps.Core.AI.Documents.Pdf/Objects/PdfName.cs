namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A PDF name, such as <c>/Type</c>.
/// </summary>
internal sealed class PdfName : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfName"/> class.
    /// </summary>
    /// <param name="value">The name, with or without its leading slash.</param>
    public PdfName(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        Value = value[0] == '/'
            ? value
            : "/" + value;
    }

    /// <summary>
    /// Gets the name with its leading slash, as <c>/Type</c>.
    /// </summary>
    public string Value { get; }
}
