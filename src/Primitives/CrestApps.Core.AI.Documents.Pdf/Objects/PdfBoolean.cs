namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A PDF boolean.
/// </summary>
internal sealed class PdfBoolean : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfBoolean"/> class.
    /// </summary>
    /// <param name="value">The value.</param>
    public PdfBoolean(bool value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the shared <see langword="true"/> value.
    /// </summary>
    public static PdfBoolean True { get; } = new(true);

    /// <summary>
    /// Gets the shared <see langword="false"/> value.
    /// </summary>
    public static PdfBoolean False { get; } = new(false);

    /// <summary>
    /// Gets the value.
    /// </summary>
    public bool Value { get; }
}
