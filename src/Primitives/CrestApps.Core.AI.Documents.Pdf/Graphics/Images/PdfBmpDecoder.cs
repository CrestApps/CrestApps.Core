using System.Buffers.Binary;
using System.Numerics;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

/// <summary>
/// Decodes uncompressed BMP files, of 1, 4, 8, 16, 24 or 32 bits a pixel, for drawing in a PDF.
/// </summary>
internal static class PdfBmpDecoder
{
    private const int MaxPixels = 100_000_000;
    private const int Uncompressed = 0;
    private const int BitFields = 3;
    private const int AlphaBitFields = 6;

    /// <summary>
    /// Decodes a BMP.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <returns>The picture.</returns>
    public static PdfRasterImage Decode(byte[] bytes)
    {
        if (bytes.Length < 26 || bytes[0] != 'B' || bytes[1] != 'M')
        {
            throw new InvalidDataException("The file is not a BMP.");
        }

        var span = bytes.AsSpan();
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(span[10..]);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(span[14..]);
        int width;
        int signedHeight;
        int bitsPerPixel;
        int compression;

        // The old OS/2 header stores the size in 16 bits; every later one in 32.
        var isCore = headerSize == 12;

        if (isCore)
        {
            width = BinaryPrimitives.ReadInt16LittleEndian(span[18..]);
            signedHeight = BinaryPrimitives.ReadInt16LittleEndian(span[20..]);
            bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(span[24..]);
            compression = Uncompressed;
        }
        else
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(span[18..]);
            signedHeight = BinaryPrimitives.ReadInt32LittleEndian(span[22..]);
            bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(span[28..]);
            compression = BinaryPrimitives.ReadInt32LittleEndian(span[30..]);
        }

        var height = Math.Abs(signedHeight);

        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels || compression is not (Uncompressed or BitFields or AlphaBitFields))
        {
            throw new InvalidDataException("The BMP is compressed or has no image this writer can read.");
        }

        var masks = ReadMasks(span, headerSize, bitsPerPixel, compression);
        var palette = ReadPalette(span, headerSize, isCore, bitsPerPixel);
        var stride = ((width * bitsPerPixel) + 31) / 32 * 4;
        var samples = new byte[width * height * 3];
        var alpha = masks.Alpha != 0
            ? new byte[width * height]
            : null;

        var anyAlpha = false;

        for (var row = 0; row < height; row++)
        {
            // Rows are stored from the bottom up, unless the height is negative.
            var line = signedHeight > 0
                ? height - 1 - row
                : row;

            var start = dataOffset + (line * stride);

            if (start + stride > bytes.Length)
            {
                throw new InvalidDataException("The BMP's pixels are cut short.");
            }

            for (var column = 0; column < width; column++)
            {
                var target = (row * width) + column;
                byte red;
                byte green;
                byte blue;
                var opacity = (byte)255;

                if (bitsPerPixel <= 8)
                {
                    var bitIndex = column * bitsPerPixel;
                    var index = (bytes[start + (bitIndex / 8)] >> (8 - bitsPerPixel - (bitIndex % 8))) & ((1 << bitsPerPixel) - 1);

                    (red, green, blue) = (index * 3) + 2 < palette.Length
                        ? (palette[index * 3], palette[(index * 3) + 1], palette[(index * 3) + 2])
                        : ((byte)0, (byte)0, (byte)0);
                }
                else if (bitsPerPixel == 24)
                {
                    var pixel = start + (column * 3);

                    (blue, green, red) = (bytes[pixel], bytes[pixel + 1], bytes[pixel + 2]);
                }
                else
                {
                    var pixel = bitsPerPixel == 16
                        ? BinaryPrimitives.ReadUInt16LittleEndian(span[(start + (column * 2))..])
                        : BinaryPrimitives.ReadUInt32LittleEndian(span[(start + (column * 4))..]);

                    red = Channel(pixel, masks.Red);
                    green = Channel(pixel, masks.Green);
                    blue = Channel(pixel, masks.Blue);

                    if (masks.Alpha != 0)
                    {
                        opacity = Channel(pixel, masks.Alpha);
                        anyAlpha |= opacity != 0;
                    }
                }

                samples[target * 3] = red;
                samples[(target * 3) + 1] = green;
                samples[(target * 3) + 2] = blue;

                if (alpha is not null)
                {
                    alpha[target] = opacity;
                }
            }
        }

        // Many 32-bit files leave the alpha byte at zero although they are opaque.
        if (!anyAlpha)
        {
            alpha = null;
        }

        return new PdfRasterImage(width, height, 3, samples, alpha);
    }

    private static (uint Red, uint Green, uint Blue, uint Alpha) ReadMasks(ReadOnlySpan<byte> span, int headerSize, int bitsPerPixel, int compression)
    {
        if (compression is BitFields or AlphaBitFields && span.Length >= 14 + Math.Max(headerSize, 40) + 12)
        {
            // The masks follow a 40-byte header, or are part of a longer one.
            var at = 14 + 40;
            var alpha = (compression == AlphaBitFields || headerSize >= 56) && span.Length >= at + 16
                ? BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 12)..])
                : 0;

            return (BinaryPrimitives.ReadUInt32LittleEndian(span[at..]), BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 4)..]), BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 8)..]), alpha);
        }

        // Without masks, 16-bit pixels are five bits a channel and 32-bit ones a byte; only a header that can
        // describe alpha gives the fourth byte that meaning.
        return (bitsPerPixel, headerSize >= 56) switch
        {
            (16, _) => (0x7C00, 0x03E0, 0x001F, 0),
            (32, true) => (0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000),
            (32, false) => (0x00FF0000, 0x0000FF00, 0x000000FF, 0),
            _ => (0, 0, 0, 0),
        };
    }

    private static byte[] ReadPalette(ReadOnlySpan<byte> span, int headerSize, bool isCore, int bitsPerPixel)
    {
        if (bitsPerPixel > 8)
        {
            return [];
        }

        var entries = 1 << bitsPerPixel;
        var entrySize = isCore
            ? 3
            : 4;

        var start = 14 + headerSize;

        if (!isCore && headerSize >= 36)
        {
            var used = BinaryPrimitives.ReadInt32LittleEndian(span[46..]);

            if (used is > 0 and <= 256)
            {
                entries = used;
            }
        }

        var palette = new byte[entries * 3];

        for (var index = 0; index < entries && start + (index * entrySize) + 2 < span.Length; index++)
        {
            var entry = start + (index * entrySize);

            palette[index * 3] = span[entry + 2];
            palette[(index * 3) + 1] = span[entry + 1];
            palette[(index * 3) + 2] = span[entry];
        }

        return palette;
    }

    private static byte Channel(uint pixel, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = BitOperations.TrailingZeroCount(mask);
        var maximum = mask >> shift;
        var value = (pixel & mask) >> shift;

        return (byte)(value * 255 / maximum);
    }
}
