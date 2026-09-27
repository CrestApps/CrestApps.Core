using System.Globalization;
using PdfSharp.Drawing;
using MigraColor = MigraDoc.DocumentObjectModel.Color;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// An opaque RGB colour read from the way people and models write colours: <c>#1F4E79</c>, <c>1F4E79</c>,
/// <c>#abc</c>, an ARGB workbook value such as <c>FF1F4E79</c>, <c>rgb(31, 78, 121)</c>, or a common name.
/// </summary>
internal readonly record struct PdfColor(byte Red, byte Green, byte Blue)
{
    private static readonly Dictionary<string, PdfColor> _named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(0, 0, 0),
        ["white"] = new(255, 255, 255),
        ["red"] = new(0xC0, 0x00, 0x00),
        ["darkred"] = new(0x8B, 0x00, 0x00),
        ["maroon"] = new(0x80, 0x00, 0x00),
        ["green"] = new(0x2E, 0x7D, 0x32),
        ["darkgreen"] = new(0x1B, 0x5E, 0x20),
        ["lime"] = new(0x7C, 0xB3, 0x42),
        ["blue"] = new(0x1F, 0x4E, 0x79),
        ["navy"] = new(0x0B, 0x25, 0x45),
        ["darkblue"] = new(0x0B, 0x25, 0x45),
        ["lightblue"] = new(0xDD, 0xEB, 0xF7),
        ["skyblue"] = new(0x5B, 0x9B, 0xD5),
        ["teal"] = new(0x00, 0x80, 0x80),
        ["cyan"] = new(0x00, 0xAC, 0xC1),
        ["purple"] = new(0x70, 0x30, 0xA0),
        ["violet"] = new(0x8E, 0x44, 0xAD),
        ["magenta"] = new(0xC2, 0x18, 0x5B),
        ["pink"] = new(0xF4, 0x8F, 0xB1),
        ["orange"] = new(0xED, 0x7D, 0x31),
        ["amber"] = new(0xFF, 0xC0, 0x00),
        ["yellow"] = new(0xFF, 0xD9, 0x66),
        ["gold"] = new(0xBF, 0x90, 0x00),
        ["brown"] = new(0x79, 0x55, 0x48),
        ["gray"] = new(0x80, 0x80, 0x80),
        ["grey"] = new(0x80, 0x80, 0x80),
        ["darkgray"] = new(0x44, 0x44, 0x44),
        ["darkgrey"] = new(0x44, 0x44, 0x44),
        ["lightgray"] = new(0xD9, 0xD9, 0xD9),
        ["lightgrey"] = new(0xD9, 0xD9, 0xD9),
        ["silver"] = new(0xC0, 0xC0, 0xC0),
        ["charcoal"] = new(0x33, 0x3F, 0x48),
    };

    /// <summary>
    /// Reads a colour.
    /// </summary>
    /// <param name="value">The colour as written.</param>
    /// <param name="color">The colour, when it could be read.</param>
    /// <returns><see langword="true"/> when the value is a colour.</returns>
    public static bool TryParse(string value, out PdfColor color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (_named.TryGetValue(text.Replace(" ", string.Empty, StringComparison.Ordinal), out color))
        {
            return true;
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseFunction(text, out color);
        }

        var hex = text.TrimStart('#');

        // A workbook writes ARGB, and the alpha a fill carries there is always opaque.
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        if (hex.Length != 6 ||
            !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        color = new PdfColor((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));

        return true;
    }

    /// <summary>
    /// Reads a colour, falling back to another when the value is missing or is not a colour.
    /// </summary>
    /// <param name="value">The colour as written.</param>
    /// <param name="fallback">The colour used when <paramref name="value"/> cannot be read.</param>
    /// <returns>The colour.</returns>
    public static PdfColor Parse(string value, PdfColor fallback)
    {
        return TryParse(value, out var color)
            ? color
            : fallback;
    }

    /// <summary>
    /// Blends this colour towards another.
    /// </summary>
    /// <param name="other">The colour blended towards.</param>
    /// <param name="amount">How far to blend, from 0 (this colour) to 1 (the other).</param>
    /// <returns>The blended colour.</returns>
    public PdfColor Blend(PdfColor other, double amount)
    {
        var weight = Math.Clamp(amount, 0, 1);

        return new PdfColor(
            (byte)Math.Round(Red + ((other.Red - Red) * weight)),
            (byte)Math.Round(Green + ((other.Green - Green) * weight)),
            (byte)Math.Round(Blue + ((other.Blue - Blue) * weight)));
    }

    /// <summary>
    /// Gets a value indicating whether dark text reads better on this colour than white text does.
    /// </summary>
    public bool IsLight => ((0.299 * Red) + (0.587 * Green) + (0.114 * Blue)) > 160;

    /// <summary>
    /// Converts the colour to the MigraDoc representation.
    /// </summary>
    /// <returns>The MigraDoc colour.</returns>
    public MigraColor ToMigraDoc()
    {
        return new MigraColor(Red, Green, Blue);
    }

    /// <summary>
    /// Converts the colour to the PDFsharp representation.
    /// </summary>
    /// <param name="opacity">The opacity, from 0 to 1.</param>
    /// <returns>The PDFsharp colour.</returns>
    public XColor ToXColor(double opacity = 1)
    {
        return XColor.FromArgb((int)Math.Round(Math.Clamp(opacity, 0, 1) * 255), Red, Green, Blue);
    }

    /// <summary>
    /// Writes the colour as <c>#RRGGBB</c>.
    /// </summary>
    /// <returns>The hex form.</returns>
    public string ToHex()
    {
        return string.Create(CultureInfo.InvariantCulture, $"#{Red:X2}{Green:X2}{Blue:X2}");
    }

    private static bool TryParseFunction(string text, out PdfColor color)
    {
        color = default;

        var open = text.IndexOf('(', StringComparison.Ordinal);
        var close = text.LastIndexOf(')');

        if (open < 0 || close <= open)
        {
            return false;
        }

        var parts = text[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length < 3)
        {
            return false;
        }

        var channels = new byte[3];

        for (var index = 0; index < 3; index++)
        {
            if (!double.TryParse(parts[index].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var channel))
            {
                return false;
            }

            if (parts[index].EndsWith('%'))
            {
                channel = channel * 255 / 100;
            }

            channels[index] = (byte)Math.Clamp(Math.Round(channel), 0, 255);
        }

        color = new PdfColor(channels[0], channels[1], channels[2]);

        return true;
    }
}
