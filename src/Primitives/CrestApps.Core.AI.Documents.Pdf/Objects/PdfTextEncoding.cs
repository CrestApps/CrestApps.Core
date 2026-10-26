using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// Converts between text and the bytes of a PDF text string, and between dates and the PDF date format.
/// </summary>
internal static class PdfTextEncoding
{
    // PDFDocEncoding is Latin-1 except for these codes (PDF 32000-1, Annex D.2); the rest of 0x80–0x9F, 0x7F
    // and 0xAD are undefined and read as the replacement character.
    private static readonly Dictionary<byte, char> _special = new()
    {
        [0x18] = '˘',
        [0x19] = 'ˇ',
        [0x1A] = 'ˆ',
        [0x1B] = '˙',
        [0x1C] = '˝',
        [0x1D] = '˛',
        [0x1E] = '˚',
        [0x1F] = '˜',
        [0x80] = '•',
        [0x81] = '†',
        [0x82] = '‡',
        [0x83] = '…',
        [0x84] = '—',
        [0x85] = '–',
        [0x86] = 'ƒ',
        [0x87] = '⁄',
        [0x88] = '‹',
        [0x89] = '›',
        [0x8A] = '−',
        [0x8B] = '‰',
        [0x8C] = '„',
        [0x8D] = '“',
        [0x8E] = '”',
        [0x8F] = '‘',
        [0x90] = '’',
        [0x91] = '‚',
        [0x92] = '™',
        [0x93] = 'ﬁ',
        [0x94] = 'ﬂ',
        [0x95] = 'Ł',
        [0x96] = 'Œ',
        [0x97] = 'Š',
        [0x98] = 'Ÿ',
        [0x99] = 'Ž',
        [0x9A] = 'ı',
        [0x9B] = 'ł',
        [0x9C] = 'œ',
        [0x9D] = 'š',
        [0x9E] = 'ž',
        [0xA0] = '€',
    };

    /// <summary>
    /// Reads the bytes of a text string.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    public static string Decode(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return string.Empty;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, (bytes.Length - 2) & ~1);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, (bytes.Length - 2) & ~1);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        var characters = new char[bytes.Length];

        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];

            characters[index] = _special.TryGetValue(value, out var special)
                ? special
                : value is 0x7F or 0xAD or (>= 0x80 and <= 0x9F)
                    ? '�'
                    : (char)value;
        }

        return new string(characters);
    }

    /// <summary>
    /// Writes text as the bytes of a text string: one byte per character when every character is ASCII or
    /// Latin-1, and in UTF-16 otherwise.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="forceUnicode">Whether to write UTF-16 whatever the text holds.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Encode(string text, bool forceUnicode)
    {
        text ??= string.Empty;

        if (!forceUnicode && TryEncodeSingleByte(text, out var bytes))
        {
            return bytes;
        }

        var unicode = new byte[2 + (text.Length * 2)];

        unicode[0] = 0xFE;
        unicode[1] = 0xFF;
        Encoding.BigEndianUnicode.GetBytes(text, 0, text.Length, unicode, 2);

        return unicode;
    }

    /// <summary>
    /// Formats a date the way PDF stores it, as <c>D:20261026143000+02'00'</c>.
    /// </summary>
    /// <param name="value">The date.</param>
    /// <returns>The date string.</returns>
    public static string FormatDate(DateTimeOffset value)
    {
        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var absolute = offset.Duration();

        return offset == TimeSpan.Zero
            ? value.ToString("'D:'yyyyMMddHHmmss'Z'", CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"D:{value:yyyyMMddHHmmss}{sign}{absolute.Hours:00}'{absolute.Minutes:00}'");
    }

    /// <summary>
    /// Reads a PDF date string, accepting the partial and loosely written forms files contain.
    /// </summary>
    /// <param name="text">The date string.</param>
    /// <param name="value">The date.</param>
    /// <returns><see langword="true"/> when the text is a date.</returns>
    public static bool TryParseDate(string text, out DateTimeOffset value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var raw = text.Trim();

        if (raw.StartsWith("D:", StringComparison.Ordinal))
        {
            raw = raw[2..];
        }

        var digits = 0;

        while (digits < raw.Length && digits < 14 && char.IsAsciiDigit(raw[digits]))
        {
            digits++;
        }

        if (digits < 4)
        {
            return false;
        }

        int Part(int start, int length, int fallback)
        {
            return start + length <= digits
                ? int.Parse(raw.AsSpan(start, length), CultureInfo.InvariantCulture)
                : fallback;
        }

        var offset = TimeSpan.Zero;
        var rest = raw[digits..].Replace("'", string.Empty, StringComparison.Ordinal);

        if (rest.Length > 0 && rest[0] is '+' or '-')
        {
            var hours = Digits(rest, 1);
            var minutes = Digits(rest, 3);

            offset = new TimeSpan(Math.Min(hours, 23), Math.Min(minutes, 59), 0);

            if (rest[0] == '-')
            {
                offset = -offset;
            }
        }

        try
        {
            value = new DateTimeOffset(
                Part(0, 4, 1),
                Math.Clamp(Part(4, 2, 1), 1, 12),
                Math.Clamp(Part(6, 2, 1), 1, 31),
                Math.Clamp(Part(8, 2, 0), 0, 23),
                Math.Clamp(Part(10, 2, 0), 0, 59),
                Math.Clamp(Part(12, 2, 0), 0, 59),
                offset);

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int Digits(string text, int start)
    {
        return text.Length >= start + 2 && int.TryParse(text.AsSpan(start, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static bool TryEncodeSingleByte(string text, out byte[] bytes)
    {
        bytes = new byte[text.Length];

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            // Only the codes PDFDocEncoding shares with Latin-1 are written as one byte: readers disagree on
            // the codes it gives typographic characters, such as dashes and curly quotes, so those go in UTF-16.
            if (character is not ((>= ' ' and <= '~') or '\t' or '\n' or '\r' or (>= '¡' and <= 'ÿ')) || character == '­')
            {
                bytes = null;

                return false;
            }

            bytes[index] = (byte)character;
        }

        return true;
    }
}
