namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A PDF string: text such as a title, or bytes such as a file identifier or a signature.
/// </summary>
internal sealed class PdfString : PdfItem
{
    private string _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfString"/> class holding text.
    /// </summary>
    /// <param name="value">The text.</param>
    public PdfString(string value)
        : this(value, PdfStringEncoding.Automatic)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfString"/> class holding text stored in a given way.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="encoding">How the text is stored.</param>
    public PdfString(string value, PdfStringEncoding encoding)
    {
        _value = value ?? string.Empty;
        Bytes = PdfTextEncoding.Encode(_value, encoding == PdfStringEncoding.Unicode);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfString"/> class holding bytes as they are.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="isHex">Whether the string is written in hexadecimal, as binary values usually are.</param>
    public PdfString(byte[] bytes, bool isHex)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        Bytes = bytes;
        IsHex = isHex;
    }

    /// <summary>
    /// Gets the bytes the string holds.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// Gets a value indicating whether the string is written in hexadecimal.
    /// </summary>
    public bool IsHex { get; }

    /// <summary>
    /// Gets the string read as text: UTF-16 or UTF-8 when it starts with a byte order mark, and
    /// PDFDocEncoding otherwise.
    /// </summary>
    public string Value => _value ??= PdfTextEncoding.Decode(Bytes);
}
