namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A PDF dictionary, and the data of a stream when the dictionary is a stream's.
/// </summary>
internal class PdfDictionary : PdfObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDictionary"/> class.
    /// </summary>
    /// <param name="owner">The document the dictionary belongs to, or <see langword="null"/>.</param>
    public PdfDictionary(PdfDocument owner)
        : base(owner)
    {
        Elements = new PdfDictionaryElements(this);
    }

    /// <summary>
    /// Gets the entries of the dictionary.
    /// </summary>
    public PdfDictionaryElements Elements { get; }

    /// <summary>
    /// Gets the stream data, or <see langword="null"/> when the dictionary is not a stream.
    /// </summary>
    public PdfStream Stream { get; private set; }

    /// <summary>
    /// Makes the dictionary a stream holding the given bytes, replacing any data it held. The bytes are
    /// stored as they are, so <c>/Filter</c> must describe them.
    /// </summary>
    /// <param name="value">The bytes.</param>
    /// <returns>The stream.</returns>
    public PdfStream CreateStream(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Stream = new PdfStream(this, value);

        return Stream;
    }
}
