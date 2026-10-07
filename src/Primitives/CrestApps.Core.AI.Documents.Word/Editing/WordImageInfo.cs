using System.Buffers.Binary;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// The format and pixel size of a picture, read from its header.
/// </summary>
/// <param name="MediaType">The media type, such as <c>image/png</c>.</param>
/// <param name="Extension">The file extension a document part is stored with, such as <c>.png</c>.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="DpiX">The horizontal resolution in dots per inch, or 96 when the file does not say.</param>
/// <param name="DpiY">The vertical resolution in dots per inch, or 96 when the file does not say.</param>
internal sealed record WordImageInfo(string MediaType, string Extension, int Width, int Height, double DpiX, double DpiY)
{
    /// <summary>
    /// Gets the natural width in points, at the picture's resolution.
    /// </summary>
    public double WidthPoints => Width * 72d / (DpiX > 0 ? DpiX : 96);

    /// <summary>
    /// Gets the natural height in points, at the picture's resolution.
    /// </summary>
    public double HeightPoints => Height * 72d / (DpiY > 0 ? DpiY : 96);

    /// <summary>
    /// Reads a picture's format and size. PNG, JPEG, GIF, BMP and TIFF are read; anything else is not a picture
    /// a document places.
    /// </summary>
    /// <param name="bytes">The picture.</param>
    /// <param name="info">The format and size.</param>
    /// <returns><see langword="true"/> when the bytes are a supported picture.</returns>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out WordImageInfo info)
    {
        info = null;

        try
        {
            info = ReadPng(bytes) ?? ReadJpeg(bytes) ?? ReadGif(bytes) ?? ReadBmp(bytes) ?? ReadTiff(bytes);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            info = null;
        }

        return info is { Width: > 0, Height: > 0 };
    }

    private static WordImageInfo ReadPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != (byte)'P' || bytes[2] != (byte)'N' || bytes[3] != (byte)'G')
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..]);
        double dpiX = 96, dpiY = 96;

        // The physical size chunk, when present, gives pixels per metre.
        var offset = 8;

        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes[offset..]);
            var type = bytes.Slice(offset + 4, 4);

            if (type.SequenceEqual("pHYs"u8) && length >= 9 && offset + 17 <= bytes.Length && bytes[offset + 16] == 1)
            {
                dpiX = BinaryPrimitives.ReadInt32BigEndian(bytes[(offset + 8)..]) * 0.0254;
                dpiY = BinaryPrimitives.ReadInt32BigEndian(bytes[(offset + 12)..]) * 0.0254;

                break;
            }

            if (type.SequenceEqual("IDAT"u8) || length < 0)
            {
                break;
            }

            offset += 12 + length;
        }

        return new WordImageInfo("image/png", ".png", width, height, Sane(dpiX), Sane(dpiY));
    }

    private static WordImageInfo ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return null;
        }

        double dpiX = 96, dpiY = 96;
        var offset = 2;

        while (offset + 9 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;

                continue;
            }

            var marker = bytes[offset + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 2)..]);

            if (marker == 0xE0 && offset + 18 < bytes.Length && bytes.Slice(offset + 4, 5).SequenceEqual("JFIF\0"u8))
            {
                var units = bytes[offset + 11];
                var x = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 12)..]);
                var y = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 14)..]);

                if (units == 1)
                {
                    dpiX = x;
                    dpiY = y;
                }
                else if (units == 2)
                {
                    dpiX = x * 2.54;
                    dpiY = y * 2.54;
                }
            }

            // Start-of-frame markers carry the size; the others in that range are not frames.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 7)..]);

                return new WordImageInfo("image/jpeg", ".jpeg", width, height, Sane(dpiX), Sane(dpiY));
            }

            offset += 2 + length;
        }

        return null;
    }

    private static WordImageInfo ReadGif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || !bytes[..3].SequenceEqual("GIF"u8))
        {
            return null;
        }

        return new WordImageInfo(
            "image/gif",
            ".gif",
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
            96,
            96);
    }

    private static WordImageInfo ReadBmp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 26 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
        var height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]));

        return new WordImageInfo("image/bmp", ".bmp", width, height, 96, 96);
    }

    private static WordImageInfo ReadTiff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8)
        {
            return null;
        }

        var little = bytes[0] == (byte)'I' && bytes[1] == (byte)'I' && bytes[2] == 42;
        var big = bytes[0] == (byte)'M' && bytes[1] == (byte)'M' && bytes[3] == 42;

        if (!little && !big)
        {
            return null;
        }

        var directory = Read32(bytes, 4, little);
        var entries = Read16(bytes, directory, little);
        int width = 0, height = 0;

        for (var index = 0; index < entries; index++)
        {
            var entry = directory + 2 + (index * 12);
            var tag = Read16(bytes, entry, little);
            var type = Read16(bytes, entry + 2, little);
            var value = type == 3 ? Read16(bytes, entry + 8, little) : Read32(bytes, entry + 8, little);

            if (tag == 256)
            {
                width = value;
            }
            else if (tag == 257)
            {
                height = value;
            }
        }

        return new WordImageInfo("image/tiff", ".tiff", width, height, 96, 96);
    }

    private static int Read16(ReadOnlySpan<byte> bytes, int at, bool little)
    {
        return little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]) : BinaryPrimitives.ReadUInt16BigEndian(bytes[at..]);
    }

    private static int Read32(ReadOnlySpan<byte> bytes, int at, bool little)
    {
        return little ? BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]) : BinaryPrimitives.ReadInt32BigEndian(bytes[at..]);
    }

    private static double Sane(double dpi)
    {
        return dpi is >= 20 and <= 2400 ? dpi : 96;
    }
}
