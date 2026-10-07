using System.Buffers.Binary;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Reads the format and pixel size of an image from its header, without decoding it.
/// </summary>
/// <remarks>
/// The renderer needs a size for every picture before it can lay one out, and a picture placed at its pixel
/// count in points is usually far too big. Knowing the natural size lets a picture with no size given be
/// placed at a sensible one, and lets an unsupported format be reported instead of failing the document.
/// </remarks>
internal static class PdfImageInfo
{
    /// <summary>
    /// Reads an image header.
    /// </summary>
    /// <param name="bytes">The encoded image.</param>
    /// <param name="mediaType">The media type the header identifies.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns><see langword="true"/> when the image is a JPEG, PNG, GIF or BMP.</returns>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out string mediaType, out int width, out int height)
    {
        mediaType = null;
        width = 0;
        height = 0;

        if (bytes.Length < 26)
        {
            return false;
        }

        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            mediaType = "image/png";
            width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
            height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);

            return width > 0 && height > 0;
        }

        if (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
        {
            mediaType = "image/gif";
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..8]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..10]);

            return width > 0 && height > 0;
        }

        if (bytes[0] == 'B' && bytes[1] == 'M')
        {
            mediaType = "image/bmp";
            width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..22]);
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[22..26]));

            return width > 0 && height > 0;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            mediaType = "image/jpeg";

            return TryReadJpegSize(bytes, out width, out height);
        }

        return false;
    }

    private static bool TryReadJpegSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        var position = 2;

        while (position + 9 < bytes.Length)
        {
            if (bytes[position] != 0xFF)
            {
                position++;

                continue;
            }

            var marker = bytes[position + 1];

            // Start-of-frame markers carry the dimensions; C4, C8 and CC are tables that share the range.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(position + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(position + 7, 2));

                return width > 0 && height > 0;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                position += 2;

                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(position + 2, 2));

            if (length < 2)
            {
                return false;
            }

            position += 2 + length;
        }

        return false;
    }
}
