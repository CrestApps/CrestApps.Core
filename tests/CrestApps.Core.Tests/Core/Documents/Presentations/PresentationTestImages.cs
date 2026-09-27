using System.Buffers.Binary;
using System.IO.Compression;

namespace CrestApps.Core.Tests.Core.Documents.Presentations;

/// <summary>
/// Makes small real pictures for tests, so nothing binary is checked in.
/// </summary>
internal static class PresentationTestImages
{
    /// <summary>
    /// Creates a PNG filled with a gradient.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The PNG.</returns>
    public static byte[] Png(int width, int height)
    {
        using var raw = new MemoryStream();

        for (var y = 0; y < height; y++)
        {
            // Each row starts with its filter type, 0 for none.
            raw.WriteByte(0);

            for (var x = 0; x < width; x++)
            {
                raw.WriteByte((byte)(x * 255 / Math.Max(1, width - 1)));
                raw.WriteByte((byte)(y * 255 / Math.Max(1, height - 1)));
                raw.WriteByte(160);
            }
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);

        return png.ToArray();
    }

    private static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in data)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static void Chunk(MemoryStream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        stream.Write(body);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(body));
        stream.Write(crc);
    }
}
