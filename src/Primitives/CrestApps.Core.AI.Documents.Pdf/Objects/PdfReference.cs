namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A reference to an object stored on its own in the file, such as <c>12 0 R</c>.
/// </summary>
/// <remarks>
/// A document holds one reference per object, so two references to the same object are the same instance.
/// The object behind a reference read from a file is loaded the first time it is asked for.
/// </remarks>
internal sealed class PdfReference : PdfItem
{
    private readonly Func<PdfReference, PdfItem> _load;
    private PdfItem _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfReference"/> class for an object already in memory.
    /// </summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="value">The object.</param>
    public PdfReference(int objectNumber, PdfItem value)
    {
        ObjectNumber = objectNumber;
        _value = value ?? PdfNull.Value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfReference"/> class for an object loaded when first needed.
    /// </summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="generation">The generation number.</param>
    /// <param name="load">Loads the object.</param>
    public PdfReference(int objectNumber, int generation, Func<PdfReference, PdfItem> load)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
        _load = load;
    }

    /// <summary>
    /// Gets the object number.
    /// </summary>
    public int ObjectNumber { get; }

    /// <summary>
    /// Gets the generation number.
    /// </summary>
    public int Generation { get; }

    /// <summary>
    /// Gets or sets the object the reference names. A reference to an object that does not exist names null.
    /// </summary>
    public PdfItem Value
    {
        get => _value ??= _load?.Invoke(this) ?? PdfNull.Value;
        set => _value = value ?? PdfNull.Value;
    }
}
