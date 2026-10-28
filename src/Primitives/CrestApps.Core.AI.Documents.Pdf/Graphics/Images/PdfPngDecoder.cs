using System.Buffers.Binary;
using System.IO.Compression;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

/// <summary>
/// Reads PNG files for drawing in a PDF.
/// </summary>
/// <remarks>
/// A PDF reader decodes a PNG's own compressed data when it is told the PNG's row filters, so an opaque,
/// non-interlaced PNG of up to eight bits a sample is stored without decoding it. Every other PNG is
/// decoded into samples and an alpha channel.
/// </remarks>
internal static class PdfPngDecoder
{
    private const int MaxPixels = 100_000_000;

    // Where each of the seven Adam7 passes starts and how far apart its pixels are: x, y, dx, dy.
    private static readonly (int X, int Y, int Dx, int Dy)[] _adam7 =
    [
        (0, 0, 8, 8),
        (4, 0, 8, 8),
        (0, 4, 4, 8),
        (2, 0, 4, 4),
        (0, 2, 2, 4),
        (1, 0, 2, 2),
        (0, 1, 1, 2),
    ];

    /// <summary>
    /// Returns whether bytes start with the PNG signature.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <returns><see langword="true"/> for a PNG.</returns>
    public static bool IsPng(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length > 8 && bytes[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    }

    /// <summary>
    /// Prepares a PNG a PDF reader can draw from its own compressed data, without decoding it.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="create">Creates the image object for a document.</param>
    /// <returns><see langword="true"/> when the PNG can be stored as it is.</returns>
    public static bool TryCreateDirect(byte[] bytes, out int width, out int height, out Func<PdfDocument, PdfDictionary> create)
    {
        var png = Read(bytes);

        width = png.Width;
        height = png.Height;
        create = null;

        if (png.Interlaced || png.Transparency is not null || png.BitDepth > 8 || png.ColorType is not (0 or 2 or 3) || (png.ColorType == 2 && png.BitDepth != 8) || (png.ColorType == 3 && png.Palette is null))
        {
            return false;
        }

        create = document =>
        {
            var image = new PdfDictionary(document);
            var colors = png.ColorType == 2
                ? 3
                : 1;

            var parameters = new PdfDictionary(document);

            image.Elements.SetName("/Type", "/XObject");
            image.Elements.SetName("/Subtype", "/Image");
            image.Elements.SetInteger("/Width", png.Width);
            image.Elements.SetInteger("/Height", png.Height);
            image.Elements.SetInteger("/BitsPerComponent", png.BitDepth);

            if (png.ColorType == 3)
            {
                image.Elements["/ColorSpace"] = new PdfArray(
                    document,
                    new PdfName("/Indexed"),
                    new PdfName("/DeviceRGB"),
                    new PdfInteger((png.Palette.Length / 3) - 1),
                    new PdfString(png.Palette, isHex: true));
            }
            else
            {
                image.Elements.SetName("/ColorSpace", colors == 3
                    ? "/DeviceRGB"
                    : "/DeviceGray");
            }

            parameters.Elements.SetInteger("/Predictor", 15);
            parameters.Elements.SetInteger("/Colors", colors);
            parameters.Elements.SetInteger("/BitsPerComponent", png.BitDepth);
            parameters.Elements.SetInteger("/Columns", png.Width);
            image.Elements.SetName("/Filter", "/FlateDecode");
            image.Elements["/DecodeParms"] = parameters;
            image.CreateStream(png.Data);

            return image;
        };

        return true;
    }

    /// <summary>
    /// Decodes a PNG into eight-bit samples and an alpha channel.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <returns>The picture.</returns>
    public static PdfRasterImage Decode(byte[] bytes)
    {
        var png = Read(bytes);
        var channels = png.ColorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException("The PNG has an unknown colour type."),
        };

        var raw = Inflate(png.Data);
        var bitsPerPixel = channels * png.BitDepth;
        var bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
        var outputComponents = png.ColorType is 0 or 4
            ? 1
            : 3;

        var samples = new byte[png.Width * png.Height * outputComponents];
        var hasAlpha = png.ColorType is 4 or 6 || png.Transparency is not null;
        var alpha = hasAlpha
            ? new byte[png.Width * png.Height]
            : null;

        var position = 0;
        (int X, int Y, int Dx, int Dy)[] passes = png.Interlaced
            ? _adam7
            : [(0, 0, 1, 1)];

        var pixel = new int[channels];

        if (alpha is not null)
        {
            Array.Fill(alpha, (byte)255);
        }

        foreach (var (startX, startY, dx, dy) in passes)
        {
            var passWidth = (png.Width - startX + dx - 1) / dx;
            var passHeight = (png.Height - startY + dy - 1) / dy;

            if (passWidth <= 0 || passHeight <= 0)
            {
                continue;
            }

            var rowBytes = ((passWidth * bitsPerPixel) + 7) / 8;
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];

            for (var row = 0; row < passHeight; row++)
            {
                if (position + 1 + rowBytes > raw.Length)
                {
                    throw new InvalidDataException("The PNG's image data is cut short.");
                }

                var filter = raw[position];

                Array.Copy(raw, position + 1, current, 0, rowBytes);
                position += 1 + rowBytes;
                Unfilter(filter, current, previous, bytesPerPixel);

                var y = startY + (row * dy);

                for (var column = 0; column < passWidth; column++)
                {
                    ReadPixel(current, column, channels, png.BitDepth, pixel);
                    Store(png, pixel, (y * png.Width) + startX + (column * dx), samples, alpha);
                }

                (previous, current) = (current, previous);
            }
        }

        return new PdfRasterImage(png.Width, png.Height, outputComponents, samples, alpha);
    }

    private static void Store(Png png, int[] pixel, int index, byte[] samples, byte[] alpha)
    {
        var maximum = (1 << png.BitDepth) - 1;

        switch (png.ColorType)
        {
            case 0:
                samples[index] = Scale(pixel[0], png.BitDepth);

                if (png.Transparency is { Length: >= 2 } grey && pixel[0] == BinaryPrimitives.ReadUInt16BigEndian(grey))
                {
                    alpha[index] = 0;
                }

                break;

            case 2:
                for (var channel = 0; channel < 3; channel++)
                {
                    samples[(index * 3) + channel] = Scale(pixel[channel], png.BitDepth);
                }

                if (png.Transparency is { Length: >= 6 } key &&
                    pixel[0] == BinaryPrimitives.ReadUInt16BigEndian(key) &&
                    pixel[1] == BinaryPrimitives.ReadUInt16BigEndian(key.AsSpan(2)) &&
                    pixel[2] == BinaryPrimitives.ReadUInt16BigEndian(key.AsSpan(4)))
                {
                    alpha[index] = 0;
                }

                break;

            case 3:
                {
                    var entry = Math.Min(pixel[0], maximum);
                    var palette = png.Palette ?? [];

                    if ((entry * 3) + 2 < palette.Length)
                    {
                        samples[index * 3] = palette[entry * 3];
                        samples[(index * 3) + 1] = palette[(entry * 3) + 1];
                        samples[(index * 3) + 2] = palette[(entry * 3) + 2];
                    }

                    if (png.Transparency is not null && entry < png.Transparency.Length)
                    {
                        alpha[index] = png.Transparency[entry];
                    }

                    break;
                }

            case 4:
                samples[index] = Scale(pixel[0], png.BitDepth);
                alpha[index] = Scale(pixel[1], png.BitDepth);

                break;

            default:
                for (var channel = 0; channel < 3; channel++)
                {
                    samples[(index * 3) + channel] = Scale(pixel[channel], png.BitDepth);
                }

                alpha[index] = Scale(pixel[3], png.BitDepth);

                break;
        }
    }

    private static void ReadPixel(byte[] row, int column, int channels, int bitDepth, int[] pixel)
    {
        for (var channel = 0; channel < channels; channel++)
        {
            var sample = (column * channels) + channel;

            pixel[channel] = bitDepth switch
            {
                16 => BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(sample * 2)),
                8 => row[sample],
                _ => (row[sample * bitDepth / 8] >> (8 - bitDepth - (sample * bitDepth % 8))) & ((1 << bitDepth) - 1),
            };
        }
    }

    private static byte Scale(int value, int bitDepth)
    {
        return bitDepth switch
        {
            16 => (byte)(value >> 8),
            8 => (byte)value,
            _ => (byte)(value * 255 / ((1 << bitDepth) - 1)),
        };
    }

    private static void Unfilter(byte filter, byte[] current, byte[] previous, int bytesPerPixel)
    {
        for (var index = 0; index < current.Length; index++)
        {
            var left = index >= bytesPerPixel
                ? current[index - bytesPerPixel]
                : 0;

            var up = previous[index];
            var upLeft = index >= bytesPerPixel
                ? previous[index - bytesPerPixel]
                : 0;


            current[index] = filter switch
            {
                0 => current[index],
                1 => (byte)(current[index] + left),
                2 => (byte)(current[index] + up),
                3 => (byte)(current[index] + ((left + up) / 2)),
                4 => (byte)(current[index] + Paeth(left, up, upLeft)),
                _ => throw new InvalidDataException("The PNG uses an unknown row filter."),
            };
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);

        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }

        return toUp <= toUpLeft
            ? up
            : upLeft;
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        zlib.CopyTo(output);

        return output.ToArray();
    }

    private static Png Read(byte[] bytes)
    {
        if (!IsPng(bytes))
        {
            throw new InvalidDataException("The file is not a PNG.");
        }

        var png = new Png();
        var data = new MemoryStream();
        var position = 8;

        while (position + 8 <= bytes.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position));
            var type = bytes.AsSpan(position + 4, 4);
            var start = position + 8;

            if (length < 0 || start + length > bytes.Length)
            {
                break;
            }

            var chunk = bytes.AsSpan(start, length);

            if (type.SequenceEqual("IHDR"u8) && length >= 13)
            {
                png.Width = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk);
                png.Height = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                png.BitDepth = chunk[8];
                png.ColorType = chunk[9];
                png.Interlaced = chunk[12] == 1;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                png.Palette = chunk.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                png.Transparency = chunk.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                data.Write(chunk);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            position = start + length + 4;
        }

        if (png.Width <= 0 || png.Height <= 0 || (long)png.Width * png.Height > MaxPixels || png.BitDepth is not (1 or 2 or 4 or 8 or 16) || data.Length == 0)
        {
            throw new InvalidDataException("The PNG has no image this writer can read.");
        }

        png.Data = data.ToArray();

        return png;
    }

    /// <summary>
    /// What a PNG's chunks say.
    /// </summary>
    private sealed class Png
    {
        /// <summary>
        /// Gets or sets the width.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets the height.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Gets or sets the bits per sample.
        /// </summary>
        public int BitDepth { get; set; }

        /// <summary>
        /// Gets or sets the colour type: 0 grey, 2 RGB, 3 palette, 4 grey and alpha, 6 RGB and alpha.
        /// </summary>
        public int ColorType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the rows are Adam7 interlaced.
        /// </summary>
        public bool Interlaced { get; set; }

        /// <summary>
        /// Gets or sets the palette, three bytes an entry.
        /// </summary>
        public byte[] Palette { get; set; }

        /// <summary>
        /// Gets or sets the transparency chunk: an opacity per palette entry, or the colour that is transparent.
        /// </summary>
        public byte[] Transparency { get; set; }

        /// <summary>
        /// Gets or sets the compressed image data.
        /// </summary>
        public byte[] Data { get; set; }
    }
}
