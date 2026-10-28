using System.Buffers.Binary;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

/// <summary>
/// Decodes the first frame of a GIF file, with its transparent colour, for drawing in a PDF.
/// </summary>
internal static class PdfGifDecoder
{
    private const int MaxPixels = 100_000_000;
    private const int MaxCodes = 4096;

    /// <summary>
    /// Decodes a GIF.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <returns>The picture.</returns>
    public static PdfRasterImage Decode(byte[] bytes)
    {
        if (bytes.Length < 13 || bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F')
        {
            throw new InvalidDataException("The file is not a GIF.");
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));

        if (width == 0 || height == 0 || (long)width * height > MaxPixels)
        {
            throw new InvalidDataException("The GIF has no image this writer can read.");
        }

        var position = 13;
        var globalPalette = ReadPalette(bytes, ref position, bytes[10]);
        var transparent = -1;

        while (position < bytes.Length)
        {
            switch (bytes[position++])
            {
                case 0x21:
                    {
                        var label = bytes[position++];

                        // The graphic control extension names the frame's transparent colour.
                        if (label == 0xF9 && position + 5 <= bytes.Length && bytes[position] >= 4 && (bytes[position + 1] & 1) != 0)
                        {
                            transparent = bytes[position + 4];
                        }

                        SkipBlocks(bytes, ref position);

                        break;
                    }

                case 0x2C:
                    return DecodeFrame(bytes, position, width, height, globalPalette, transparent);

                case 0x3B:
                    position = bytes.Length;

                    break;

                default:
                    throw new InvalidDataException("The GIF is damaged.");
            }
        }

        throw new InvalidDataException("The GIF holds no image.");
    }

    private static PdfRasterImage DecodeFrame(byte[] bytes, int position, int width, int height, byte[] globalPalette, int transparent)
    {
        var left = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position));
        var top = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 2));
        var frameWidth = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 4));
        var frameHeight = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 6));
        var flags = bytes[position + 8];
        var interlaced = (flags & 0x40) != 0;

        position += 9;

        var palette = ReadPalette(bytes, ref position, flags) ?? globalPalette ?? [];
        var minimumCodeSize = bytes[position++];

        using var data = new MemoryStream();

        while (position < bytes.Length && bytes[position] != 0)
        {
            var length = bytes[position];

            data.Write(bytes, position + 1, Math.Min(length, bytes.Length - position - 1));
            position += length + 1;
        }

        var indices = Decompress(data.ToArray(), minimumCodeSize, frameWidth * frameHeight);
        var samples = new byte[width * height * 3];
        var alpha = new byte[width * height];

        // Outside the frame the picture is transparent, as a viewer shows it.
        for (var row = 0; row < frameHeight; row++)
        {
            var y = interlaced
                ? top + InterlacedRow(row, frameHeight)
                : top + row;


            if (y >= height)
            {
                continue;
            }

            for (var column = 0; column < frameWidth && left + column < width; column++)
            {
                var index = indices[(row * frameWidth) + column];
                var target = (y * width) + left + column;

                if (index == transparent || (index * 3) + 2 >= palette.Length)
                {
                    continue;
                }

                samples[target * 3] = palette[index * 3];
                samples[(target * 3) + 1] = palette[(index * 3) + 1];
                samples[(target * 3) + 2] = palette[(index * 3) + 2];
                alpha[target] = 255;
            }
        }

        return new PdfRasterImage(width, height, 3, samples, alpha);
    }

    private static int InterlacedRow(int row, int height)
    {
        // The rows are stored every eighth from 0, every eighth from 4, every fourth from 2, then every second from 1.
        (int Start, int Step)[] passes = [(0, 8), (4, 8), (2, 4), (1, 2)];

        foreach (var (start, step) in passes)
        {
            var count = (height - start + step - 1) / step;

            if (row < count)
            {
                return start + (row * step);
            }

            row -= count;
        }

        return row;
    }

    private static byte[] Decompress(byte[] data, int minimumCodeSize, int pixelCount)
    {
        var output = new byte[pixelCount];

        if (minimumCodeSize is < 2 or > 11)
        {
            return output;
        }

        var clear = 1 << minimumCodeSize;
        var end = clear + 1;
        var codeSize = minimumCodeSize + 1;
        var next = clear + 2;
        var prefix = new int[MaxCodes];
        var suffix = new byte[MaxCodes];
        var stack = new byte[MaxCodes + 1];
        var written = 0;
        var old = -1;
        byte first = 0;
        var bitBuffer = 0;
        var bitCount = 0;
        var position = 0;

        for (var code = 0; code < clear; code++)
        {
            suffix[code] = (byte)code;
        }

        while (written < pixelCount)
        {
            while (bitCount < codeSize && position < data.Length)
            {
                bitBuffer |= data[position++] << bitCount;
                bitCount += 8;
            }

            if (bitCount < codeSize)
            {
                break;
            }

            var code = bitBuffer & ((1 << codeSize) - 1);

            bitBuffer >>= codeSize;
            bitCount -= codeSize;

            if (code == clear)
            {
                codeSize = minimumCodeSize + 1;
                next = clear + 2;
                old = -1;

                continue;
            }

            if (code == end)
            {
                break;
            }

            if (old == -1)
            {
                if (code >= clear)
                {
                    break;
                }

                first = suffix[code];
                output[written++] = first;
                old = code;

                continue;
            }

            var current = code;
            var top = 0;

            if (code >= next)
            {
                stack[top++] = first;
                code = old;
            }

            while (code >= clear && top < MaxCodes)
            {
                stack[top++] = suffix[code];
                code = prefix[code];
            }

            first = suffix[code];
            stack[top++] = first;

            while (top > 0 && written < pixelCount)
            {
                output[written++] = stack[--top];
            }

            if (next < MaxCodes)
            {
                prefix[next] = old;
                suffix[next] = first;
                next++;

                if (next == 1 << codeSize && codeSize < 12)
                {
                    codeSize++;
                }
            }

            old = current;
        }

        return output;
    }

    private static byte[] ReadPalette(byte[] bytes, ref int position, byte flags)
    {
        if ((flags & 0x80) == 0)
        {
            return null;
        }

        var length = 3 * (1 << ((flags & 0x07) + 1));
        var palette = bytes.AsSpan(position, Math.Min(length, bytes.Length - position)).ToArray();

        position += length;

        return palette;
    }

    private static void SkipBlocks(byte[] bytes, ref int position)
    {
        while (position < bytes.Length && bytes[position] != 0)
        {
            position += bytes[position] + 1;
        }

        position++;
    }
}
