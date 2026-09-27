using System.Buffers.Binary;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Reads the kind and pixel size of a picture from its header, so it can be placed on a slide at its own
/// proportions without decoding it.
/// </summary>
public static class PresentationImageInfo
{
    /// <summary>
    /// The picture formats a slide can hold and a browser can draw.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/bmp",
    };

    /// <summary>
    /// Reads a picture's header.
    /// </summary>
    /// <param name="data">The encoded picture.</param>
    /// <param name="contentType">The media type the header identifies.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns><see langword="true"/> when the header is a PNG, JPEG, GIF or BMP header with a size.</returns>
    public static bool TryRead(ReadOnlySpan<byte> data, out string contentType, out int width, out int height)
    {
        contentType = null;
        width = 0;
        height = 0;

        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            contentType = "image/png";
            width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));

            return width > 0 && height > 0;
        }

        if (data.Length >= 10 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
        {
            contentType = "image/gif";
            width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));

            return width > 0 && height > 0;
        }

        if (data.Length >= 26 && data[0] == (byte)'B' && data[1] == (byte)'M')
        {
            contentType = "image/bmp";
            width = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18, 4)));
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4)));

            return width > 0 && height > 0;
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            contentType = "image/jpeg";

            return TryReadJpegSize(data, out width, out height);
        }

        return false;
    }

    private static bool TryReadJpegSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        var offset = 2;

        while (offset + 9 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = data[offset + 1];

            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                offset += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 2, 2));

            // The start-of-frame markers carry the size; every other segment is skipped by its length.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 7, 2));

                return width > 0 && height > 0;
            }

            if (length < 2)
            {
                return false;
            }

            offset += 2 + length;
        }

        return false;
    }
}
