using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

/// <summary>
/// Builds small images in memory, so PDF tests need no fixture files.
/// </summary>
internal static class PdfTestImages
{
    private static readonly uint[] _crcTable = BuildCrcTable();

    /// <summary>
    /// Builds a solid red PNG.
    /// </summary>
    /// <param name="size">The width and height in pixels.</param>
    /// <returns>The PNG bytes.</returns>
    public static byte[] RedSquarePng(int size = 16)
    {
        return SolidPng(size, size, 0xE0, 0x20, 0x20);
    }

    /// <summary>
    /// Builds a solid-colour PNG.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="red">The red channel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    /// <returns>The PNG bytes.</returns>
    public static byte[] SolidPng(int width, int height, byte red, byte green, byte blue)
    {
        return Png(width, height, red, green, blue, null);
    }

    /// <summary>
    /// Builds a solid-colour PNG with an alpha channel.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="red">The red channel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    /// <param name="alpha">The opacity of every pixel.</param>
    /// <returns>The PNG bytes.</returns>
    public static byte[] TranslucentPng(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        return Png(width, height, red, green, blue, alpha);
    }

    /// <summary>
    /// Builds a two by two, 24-bit BMP: red, green, blue and white pixels.
    /// </summary>
    /// <returns>The BMP bytes.</returns>
    public static byte[] SmallBmp()
    {
        // Two rows of two pixels, padded to four bytes, stored bottom row first, each pixel as blue, green, red.
        byte[] pixels =
        [
            0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00,
            0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00,
        ];

        var file = new byte[54 + pixels.Length];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), 2);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), 2);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(28), 24);
        pixels.CopyTo(file, 54);

        return file;
    }

    /// <summary>
    /// Builds a one-pixel GIF whose only pixel is transparent.
    /// </summary>
    /// <returns>The GIF bytes.</returns>
    public static byte[] TransparentGif()
    {
        return Convert.FromBase64String("R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");
    }

    private static byte[] Png(int width, int height, byte red, byte green, byte blue, byte? alpha)
    {
        using var output = new MemoryStream();

        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = alpha is null ? (byte)2 : (byte)6;
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();

        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);

            for (var x = 0; x < width; x++)
            {
                raw.WriteByte(red);
                raw.WriteByte(green);
                raw.WriteByte(blue);

                if (alpha is not null)
                {
                    raw.WriteByte(alpha.Value);
                }
            }
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static void WriteChunk(MemoryStream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = 0xFFFFFFFFu;

        foreach (var value in typeBytes.Concat(data))
        {
            crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        var checksum = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc ^ 0xFFFFFFFFu);
        output.Write(checksum);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (var n = 0u; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
