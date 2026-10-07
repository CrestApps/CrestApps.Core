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
        using var output = new MemoryStream();

        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 2;
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
