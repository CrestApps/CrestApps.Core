using System.IO.Compression;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Builds small, synthetic PNG images in memory so image-bearing fixtures never require a committed binary
/// file. The encoder writes 8-bit RGB, non-interlaced PNGs with filter type 0 on every scanline, which is the
/// narrowest shape every decoder in this repository is expected to handle.
/// </summary>
internal static class TestImageFactory
{
    private static readonly byte[] _signature = [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] _crcTable = BuildCrcTable();

    /// <summary>
    /// Encodes a PNG whose pixels are produced by the supplied sampler.
    /// </summary>
    /// <param name="width">The image width, in pixels.</param>
    /// <param name="height">The image height, in pixels.</param>
    /// <param name="pixel">The sampler invoked with the zero-based column and row of each pixel.</param>
    /// <param name="filterType">The scanline filter to encode with, from 0 (none) to 4 (Paeth).</param>
    /// <returns>The encoded PNG bytes.</returns>
    public static byte[] CreatePng(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel, byte filterType = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(filterType, (byte)4);
        ArgumentNullException.ThrowIfNull(pixel);

        var stride = width * 3;
        var pixels = new byte[height * stride];
        var offset = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                pixels[offset++] = r;
                pixels[offset++] = g;
                pixels[offset++] = b;
            }
        }

        var raw = Filter(pixels, width, height, filterType);

        using var output = new MemoryStream();
        output.Write(_signature);

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;

        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", Deflate(raw));
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    /// <summary>
    /// Creates a chart-like image: a white field, dark axes along the left and bottom edges, and six flat
    /// colour bars. Its low distinct-colour count and long axis-aligned runs are what a salience heuristic
    /// is expected to read as a chart rather than a photograph.
    /// </summary>
    /// <param name="width">The image width, in pixels.</param>
    /// <param name="height">The image height, in pixels.</param>
    /// <returns>The encoded PNG bytes.</returns>
    public static byte[] BarChart(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 12);

        (byte R, byte G, byte B)[] palette =
        [
            (200, 30, 30),
            (30, 120, 200),
            (240, 180, 20),
            (40, 160, 80),
            (140, 60, 180),
            (90, 90, 90),
        ];

        var barWidth = Math.Max(1, (width - 4) / palette.Length);

        return CreatePng(width, height, (x, y) =>
        {
            if (x < 2 || y >= height - 2)
            {
                return (20, 20, 20);
            }

            var barIndex = (x - 2) / barWidth;

            if (barIndex >= palette.Length)
            {
                return (255, 255, 255);
            }

            var barHeight = (height - 4) * (barIndex + 2) / (palette.Length + 2);

            if (y < height - 2 - barHeight)
            {
                return (255, 255, 255);
            }

            return palette[barIndex];
        });
    }

    /// <summary>
    /// Creates a deterministic pseudo-random colour field, standing in for a photograph: every pixel is its
    /// own colour, so the distinct-colour count is high and no axis-aligned run survives.
    /// </summary>
    /// <param name="width">The image width, in pixels.</param>
    /// <param name="height">The image height, in pixels.</param>
    /// <param name="seed">The seed that makes the field reproducible.</param>
    /// <returns>The encoded PNG bytes.</returns>
    public static byte[] Noise(int width, int height, int seed)
    {
        return CreatePng(width, height, (x, y) =>
        {
            var hash = Hash(x, y, seed);

            return ((byte)(hash & 0xFF), (byte)((hash >> 8) & 0xFF), (byte)((hash >> 16) & 0xFF));
        });
    }

    /// <summary>
    /// Creates a single-colour image.
    /// </summary>
    /// <param name="width">The image width, in pixels.</param>
    /// <param name="height">The image height, in pixels.</param>
    /// <param name="color">The colour every pixel takes.</param>
    /// <returns>The encoded PNG bytes.</returns>
    public static byte[] Solid(int width, int height, (byte R, byte G, byte B) color)
    {
        return CreatePng(width, height, (_, _) => color);
    }

    /// <summary>
    /// Applies one PNG scanline filter to every row, so a fixture can exercise each filter a decoder has to
    /// reverse.
    /// </summary>
    /// <param name="pixels">The unfiltered pixels, three bytes per pixel, row by row.</param>
    /// <param name="width">The image width, in pixels.</param>
    /// <param name="height">The image height, in pixels.</param>
    /// <param name="filterType">The filter to apply.</param>
    /// <returns>The filtered scanlines, each prefixed by its filter byte.</returns>
    private static byte[] Filter(byte[] pixels, int width, int height, byte filterType)
    {
        const int bytesPerPixel = 3;

        var stride = width * bytesPerPixel;
        var raw = new byte[height * (stride + 1)];

        for (var row = 0; row < height; row++)
        {
            var target = row * (stride + 1);
            var source = row * stride;

            raw[target] = filterType;

            for (var index = 0; index < stride; index++)
            {
                var current = pixels[source + index];
                var left = index >= bytesPerPixel ? pixels[source + index - bytesPerPixel] : (byte)0;
                var up = row > 0 ? pixels[source - stride + index] : (byte)0;
                var upLeft = row > 0 && index >= bytesPerPixel ? pixels[source - stride + index - bytesPerPixel] : (byte)0;

                raw[target + 1 + index] = filterType switch
                {
                    1 => (byte)(current - left),
                    2 => (byte)(current - up),
                    3 => (byte)(current - ((left + up) / 2)),
                    4 => (byte)(current - Paeth(left, up, upLeft)),
                    _ => current,
                };
            }
        }

        return raw;
    }

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var p = left + up - upLeft;
        var pa = Math.Abs(p - left);
        var pb = Math.Abs(p - up);
        var pc = Math.Abs(p - upLeft);

        if (pa <= pb && pa <= pc)
        {
            return left;
        }

        return pb <= pc ? up : upLeft;
    }

    private static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            var value = (uint)((x * 73856093) ^ (y * 19349663) ^ (seed * 83492791));
            value ^= value >> 13;
            value *= 2246822519;
            value ^= value >> 16;

            return value;
        }
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(MemoryStream output, string type, byte[] data)
    {
        var header = new byte[4];
        WriteBigEndian(header, 0, (uint)data.Length);
        output.Write(header);

        var typeBytes = new byte[4];

        for (var i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }

        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        output.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in type)
        {
            crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        foreach (var value in data)
        {
            crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (var i = 0; i < table.Length; i++)
        {
            var value = (uint)i;

            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? 0xEDB88320u ^ (value >> 1)
                    : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
