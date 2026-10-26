using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

/// <summary>
/// A picture ready to draw on a PDF page: a JPEG, PNG, GIF or BMP file, read once and stored in each
/// document that draws it as one image object, however often it is drawn.
/// </summary>
/// <remarks>
/// A JPEG is stored as it is. A PNG is stored as its own compressed data when a PDF reader can draw that
/// directly; any other PNG, a GIF and a BMP are decoded, and their transparency kept as a soft mask.
/// </remarks>
internal sealed class PdfImage
{
    private static readonly ConditionalWeakTable<PdfDocument, Dictionary<PdfImage, PdfDictionary>> _stored = [];

    private readonly Func<PdfDocument, PdfDictionary> _create;

    private PdfImage(int width, int height, Func<PdfDocument, PdfDictionary> create)
    {
        Width = width;
        Height = height;
        _create = create;
    }

    /// <summary>
    /// Gets the width, in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height, in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Reads a picture file.
    /// </summary>
    /// <param name="bytes">A JPEG, PNG, GIF or BMP file.</param>
    /// <returns>The picture, or <see langword="null"/> when the file is not one this writer can draw.</returns>
    public static PdfImage TryLoad(byte[] bytes)
    {
        if (bytes is not { Length: > 8 })
        {
            return null;
        }

        try
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return FromJpeg(bytes);
            }

            if (PdfPngDecoder.IsPng(bytes))
            {
                return PdfPngDecoder.TryCreateDirect(bytes, out var width, out var height, out var create)
                    ? new PdfImage(width, height, create)
                    : FromRaster(PdfPngDecoder.Decode(bytes));
            }

            if (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
            {
                return FromRaster(PdfGifDecoder.Decode(bytes));
            }

            if (bytes[0] == 'B' && bytes[1] == 'M')
            {
                return FromRaster(PdfBmpDecoder.Decode(bytes));
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentException or EndOfStreamException or OverflowException)
        {
            // A damaged picture is not drawn; the caller reports it like a missing one.
            return null;
        }

        return null;
    }

    /// <summary>
    /// Returns the image object a document draws the picture with, storing it in the document the first time.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The image object, stored on its own in the document.</returns>
    public PdfDictionary GetXObject(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var stored = _stored.GetValue(document, _ => []);

        if (!stored.TryGetValue(this, out var image))
        {
            image = _create(document);
            document.AddObject(image);
            stored[this] = image;
        }

        return image;
    }

    private static PdfImage FromRaster(PdfRasterImage raster)
    {
        return raster is null
            ? null
            : new PdfImage(raster.Width, raster.Height, raster.ToXObject);
    }

    private static PdfImage FromJpeg(byte[] bytes)
    {
        if (!TryReadJpegFrame(bytes, out var width, out var height, out var components, out var isAdobe))
        {
            return null;
        }

        return new PdfImage(width, height, document =>
        {
            var image = new PdfDictionary(document);

            image.Elements.SetName("/Type", "/XObject");
            image.Elements.SetName("/Subtype", "/Image");
            image.Elements.SetInteger("/Width", width);
            image.Elements.SetInteger("/Height", height);
            image.Elements.SetInteger("/BitsPerComponent", 8);
            image.Elements.SetName("/ColorSpace", components switch
            {
                1 => "/DeviceGray",
                4 => "/DeviceCMYK",
                _ => "/DeviceRGB",
            });

            // Photoshop and other Adobe tools write CMYK JPEGs inverted.
            if (components == 4 && isAdobe)
            {
                image.Elements["/Decode"] = new PdfArray(document, Enumerable.Range(0, 8).Select(index => new PdfInteger(1 - (index % 2))));
            }

            image.Elements.SetName("/Filter", "/DCTDecode");
            image.CreateStream(bytes);

            return image;
        });
    }

    private static bool TryReadJpegFrame(byte[] bytes, out int width, out int height, out int components, out bool isAdobe)
    {
        width = 0;
        height = 0;
        components = 0;
        isAdobe = false;

        var position = 2;

        while (position + 4 <= bytes.Length)
        {
            if (bytes[position] != 0xFF)
            {
                position++;

                continue;
            }

            var marker = bytes[position + 1];

            // A fill byte is skipped alone; markers without a length are skipped with their prefix.
            if (marker == 0xFF)
            {
                position++;

                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                position += 2;

                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 2));

            if (marker == 0xEE && position + 9 <= bytes.Length && bytes.AsSpan(position + 4, 5).SequenceEqual("Adobe"u8))
            {
                isAdobe = true;
            }

            // Start-of-frame markers carry the dimensions; C4, C8 and CC are tables that share the range.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && position + 10 <= bytes.Length)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 5));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 7));
                components = bytes[position + 9];

                return width > 0 && height > 0 && components is 1 or 3 or 4;
            }

            position += 2 + length;
        }

        return false;
    }
}
