using System.IO.Compression;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The data of a stream: page content, an image, a font, an attached file.
/// </summary>
internal sealed class PdfStream
{
    private readonly PdfDictionary _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStream"/> class.
    /// </summary>
    /// <param name="owner">The stream's dictionary.</param>
    /// <param name="value">The bytes, as stored.</param>
    public PdfStream(PdfDictionary owner, byte[] value)
    {
        _owner = owner;
        Value = value;
    }

    /// <summary>
    /// Gets or sets the bytes as stored, encoded by the filters the dictionary names.
    /// </summary>
    public byte[] Value { get; set; }

    /// <summary>
    /// Gets the number of bytes stored.
    /// </summary>
    public int Length => Value.Length;

    /// <summary>
    /// Gets the bytes with the lossless filters (Flate, LZW, ASCII hex and 85, run length) undone. When the
    /// stream also uses an image filter such as DCT or JPX, or cannot be decoded, the bytes are returned as
    /// stored.
    /// </summary>
    public byte[] UnfilteredValue => PdfStreamFilters.Decode(_owner, Value);

    /// <summary>
    /// Stores the given bytes compressed with Flate, replacing the stream's data and filters.
    /// </summary>
    /// <param name="value">The bytes, unencoded.</param>
    public void SetCompressed(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = Compress(value);
        _owner.Elements.SetName("/Filter", "/FlateDecode");
        _owner.Elements.Remove("/DecodeParms");
    }

    /// <summary>
    /// Stores the given bytes as they are, replacing the stream's data and removing its filters.
    /// </summary>
    /// <param name="value">The bytes, unencoded.</param>
    public void SetUncompressed(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
        _owner.Elements.Remove("/Filter");
        _owner.Elements.Remove("/DecodeParms");
    }

    /// <summary>
    /// Compresses bytes the way a Flate stream stores them.
    /// </summary>
    /// <param name="value">The bytes.</param>
    /// <param name="level">How hard to compress.</param>
    /// <returns>The compressed bytes.</returns>
    public static byte[] Compress(byte[] value, CompressionLevel level = CompressionLevel.Optimal)
    {
        ArgumentNullException.ThrowIfNull(value);

        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, level, leaveOpen: true))
        {
            zlib.Write(value);
        }

        return output.ToArray();
    }
}
