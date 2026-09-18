using System.Buffers.Binary;
using System.IO.Compression;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// Draws a set of line segments into a grayscale PNG.
/// </summary>
/// <remarks>
/// A chart drawn as vector geometry has no image to extract: there is nothing in the file but instructions
/// for drawing lines. Everything downstream — storing the figure, showing it to a reader, handing it to a
/// vision model — needs a picture, so one is drawn here rather than the figure being dropped for having no
/// bytes.
/// <para>
/// It renders lines and nothing else: no fills, no colour, no text. The result is a line drawing that says
/// what shape the figure is, which is exactly what a vision model needs and all that can honestly be
/// reconstructed from stroked geometry.
/// </para>
/// </remarks>
internal static class PngLineDrawingWriter
{
    /// <summary>
    /// The longest edge of the rendered image. Large enough for a model to read the shape, small enough that
    /// a page full of vector art does not cost megabytes.
    /// </summary>
    private const int MaxEdge = 1000;

    /// <summary>
    /// The shortest edge worth rendering. Anything thinner is a rule, not a drawing.
    /// </summary>
    private const int MinEdge = 16;

    private const int Margin = 8;

    private static readonly uint[] _crcTable = BuildCrcTable();

    /// <summary>
    /// Draws the supplied segments.
    /// </summary>
    /// <param name="segments">The segments, in PDF user space.</param>
    /// <param name="bounds">The region to render, in PDF user space, as left, bottom, right and top.</param>
    /// <returns>The PNG bytes, or <see langword="null"/> when there is nothing to draw.</returns>
    public static byte[] Write(IReadOnlyList<PdfSegment> segments, double[] bounds)
    {
        if (segments is null || segments.Count == 0 || bounds is not { Length: 4 })
        {
            return null;
        }

        var width = bounds[2] - bounds[0];
        var height = bounds[3] - bounds[1];

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var scale = Math.Min(MaxEdge / width, MaxEdge / height);
        var pixelWidth = (int)Math.Round(width * scale) + (Margin * 2);
        var pixelHeight = (int)Math.Round(height * scale) + (Margin * 2);

        if (pixelWidth < MinEdge || pixelHeight < MinEdge)
        {
            return null;
        }

        // One byte per pixel, white, plus the PNG per-row filter byte.
        var stride = pixelWidth + 1;
        var raster = new byte[stride * pixelHeight];

        for (var y = 0; y < pixelHeight; y++)
        {
            raster[y * stride] = 0;

            for (var x = 0; x < pixelWidth; x++)
            {
                raster[(y * stride) + 1 + x] = 0xFF;
            }
        }

        foreach (var segment in segments)
        {
            DrawLine(
                raster,
                stride,
                pixelWidth,
                pixelHeight,
                ToPixelX(segment.X1, bounds[0], scale),
                ToPixelY(segment.Y1, bounds[3], scale),
                ToPixelX(segment.X2, bounds[0], scale),
                ToPixelY(segment.Y2, bounds[3], scale));
        }

        return Encode(raster, pixelWidth, pixelHeight);
    }

    private static int ToPixelX(double x, double left, double scale)
    {
        return Margin + (int)Math.Round((x - left) * scale);
    }

    private static int ToPixelY(double y, double top, double scale)
    {
        // PDF user space grows upward and an image grows downward.
        return Margin + (int)Math.Round((top - y) * scale);
    }

    /// <summary>
    /// Draws one black line, the integer way.
    /// </summary>
    private static void DrawLine(byte[] raster, int stride, int width, int height, int x0, int y0, int x1, int y1)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var stepX = x0 < x1 ? 1 : -1;
        var stepY = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                raster[(y0 * stride) + 1 + x0] = 0;
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var doubled = error * 2;

            if (doubled >= dy)
            {
                if (x0 == x1)
                {
                    break;
                }

                error += dy;
                x0 += stepX;
            }

            if (doubled <= dx)
            {
                if (y0 == y1)
                {
                    break;
                }

                error += dx;
                y0 += stepY;
            }
        }
    }

    private static byte[] Encode(byte[] raster, int width, int height)
    {
        using var output = new MemoryStream();

        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];

        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 0;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;

        WriteChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();

        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raster);
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();
    }

    private static void WriteChunk(MemoryStream output, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = 0xFFFFFFFFu;

        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFFu;

        Span<byte> checksum = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc);
        output.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
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
