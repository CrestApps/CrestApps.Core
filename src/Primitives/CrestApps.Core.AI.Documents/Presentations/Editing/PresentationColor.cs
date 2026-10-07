using System.Globalization;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A colour a caller asked for, read into either a fixed RGB value or a theme colour slot.
/// </summary>
/// <remarks>
/// A deck built on theme colours restyles itself when its theme changes, so a caller asking for
/// <c>accent1</c> is given a theme reference rather than whatever <c>accent1</c> happens to be today. A
/// caller asking for <c>#1F4E79</c> gets exactly that. Both are read here, once, for every tool.
/// </remarks>
public sealed partial class PresentationColor
{
    private static readonly Dictionary<string, string> _themeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dk1"] = "dk1",
        ["dark1"] = "dk1",
        ["text1"] = "dk1",
        ["tx1"] = "dk1",
        ["text"] = "dk1",
        ["lt1"] = "lt1",
        ["light1"] = "lt1",
        ["background1"] = "lt1",
        ["bg1"] = "lt1",
        ["background"] = "lt1",
        ["dk2"] = "dk2",
        ["dark2"] = "dk2",
        ["text2"] = "dk2",
        ["tx2"] = "dk2",
        ["lt2"] = "lt2",
        ["light2"] = "lt2",
        ["background2"] = "lt2",
        ["bg2"] = "lt2",
        ["accent1"] = "accent1",
        ["accent2"] = "accent2",
        ["accent3"] = "accent3",
        ["accent4"] = "accent4",
        ["accent5"] = "accent5",
        ["accent6"] = "accent6",
        ["accent"] = "accent1",
        ["primary"] = "accent1",
        ["secondary"] = "accent2",
        ["hlink"] = "hlink",
        ["hyperlink"] = "hlink",
        ["folhlink"] = "folHlink",
        ["followedhyperlink"] = "folHlink",
    };

    private static readonly Dictionary<string, string> _namedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "000000",
        ["white"] = "FFFFFF",
        ["red"] = "FF0000",
        ["darkred"] = "8B0000",
        ["crimson"] = "DC143C",
        ["green"] = "008000",
        ["darkgreen"] = "006400",
        ["lime"] = "00FF00",
        ["blue"] = "0000FF",
        ["darkblue"] = "00008B",
        ["navy"] = "000080",
        ["royalblue"] = "4169E1",
        ["steelblue"] = "4682B4",
        ["skyblue"] = "87CEEB",
        ["lightblue"] = "ADD8E6",
        ["teal"] = "008080",
        ["cyan"] = "00FFFF",
        ["aqua"] = "00FFFF",
        ["turquoise"] = "40E0D0",
        ["yellow"] = "FFFF00",
        ["gold"] = "FFD700",
        ["orange"] = "FFA500",
        ["darkorange"] = "FF8C00",
        ["coral"] = "FF7F50",
        ["tomato"] = "FF6347",
        ["pink"] = "FFC0CB",
        ["hotpink"] = "FF69B4",
        ["magenta"] = "FF00FF",
        ["fuchsia"] = "FF00FF",
        ["purple"] = "800080",
        ["violet"] = "EE82EE",
        ["indigo"] = "4B0082",
        ["lavender"] = "E6E6FA",
        ["brown"] = "A52A2A",
        ["maroon"] = "800000",
        ["olive"] = "808000",
        ["beige"] = "F5F5DC",
        ["ivory"] = "FFFFF0",
        ["silver"] = "C0C0C0",
        ["gray"] = "808080",
        ["grey"] = "808080",
        ["darkgray"] = "404040",
        ["darkgrey"] = "404040",
        ["lightgray"] = "D3D3D3",
        ["lightgrey"] = "D3D3D3",
        ["charcoal"] = "36454F",
        ["slate"] = "708090",
        ["slategray"] = "708090",
        ["mint"] = "98FF98",
        ["salmon"] = "FA8072",
        ["tan"] = "D2B48C",
    };

    private PresentationColor()
    {
    }

    /// <summary>
    /// Gets the fixed colour as six upper-case hexadecimal digits, or <see langword="null"/> for a theme
    /// colour.
    /// </summary>
    public string Hex { get; private init; }

    /// <summary>
    /// Gets the theme slot, such as <c>accent1</c>, or <see langword="null"/> for a fixed colour.
    /// </summary>
    public string ThemeSlot { get; private init; }

    /// <summary>
    /// Gets how much lighter (positive) or darker (negative) than the base colour to draw it, as a fraction
    /// from -1 to 1.
    /// </summary>
    public double Brightness { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the caller asked for no colour at all.
    /// </summary>
    public bool IsNone { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the colour refers to a theme slot.
    /// </summary>
    public bool IsTheme => ThemeSlot is not null;

    /// <summary>
    /// Reads a colour written as <c>#RRGGBB</c>, <c>RRGGBB</c>, <c>#RGB</c>, a common colour name such as
    /// <c>navy</c>, or a theme slot such as <c>accent1</c>, <c>text1</c> or <c>background2</c>. A theme
    /// colour may be followed by a shade, as in <c>accent1 lighter 40%</c> or <c>accent2 darker 25%</c>.
    /// <c>none</c> and <c>transparent</c> read as no colour.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="color">The colour, when the text is one.</param>
    /// <returns><see langword="true"/> when the text is a colour.</returns>
    public static bool TryParse(string text, out PresentationColor color)
    {
        color = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("no fill", StringComparison.OrdinalIgnoreCase))
        {
            color = new PresentationColor { IsNone = true };

            return true;
        }

        var brightness = 0d;
        var match = ShadeExpression().Match(trimmed);

        if (match.Success)
        {
            trimmed = match.Groups["base"].Value.Trim();
            var amount = double.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture) / 100d;
            brightness = match.Groups["direction"].Value.StartsWith("light", StringComparison.OrdinalIgnoreCase)
                ? Math.Clamp(amount, 0, 1)
                : -Math.Clamp(amount, 0, 1);
        }

        var compact = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);

        if (_themeAliases.TryGetValue(compact, out var slot))
        {
            color = new PresentationColor
            {
                ThemeSlot = slot,
                Brightness = brightness,
            };

            return true;
        }

        var hex = NormalizeHex(compact) ?? (_namedColors.TryGetValue(compact, out var named) ? named : null);

        if (hex is null)
        {
            return false;
        }

        color = new PresentationColor
        {
            Hex = brightness == 0 ? hex : Shade(hex, brightness),
        };

        return true;
    }

    /// <summary>
    /// Reads a theme slot name or one of its aliases, such as <c>text1</c> for <c>dk1</c> or
    /// <c>background2</c> for <c>lt2</c>.
    /// </summary>
    /// <param name="name">The name to read.</param>
    /// <param name="slot">The slot, when the name is one.</param>
    /// <returns><see langword="true"/> when the name is a theme slot.</returns>
    public static bool TryNormalizeThemeSlot(string name, out string slot)
    {
        slot = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var compact = name.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);

        return _themeAliases.TryGetValue(compact, out slot);
    }

    /// <summary>
    /// Resolves the colour to a fixed value against a theme.
    /// </summary>
    /// <param name="themeColors">The theme colours keyed by slot.</param>
    /// <returns>The colour as six hexadecimal digits, or <see langword="null"/> when it is no colour.</returns>
    public string Resolve(IDictionary<string, string> themeColors)
    {
        if (IsNone)
        {
            return null;
        }

        if (!IsTheme)
        {
            return Hex;
        }

        if (themeColors is null || !themeColors.TryGetValue(ThemeSlot, out var baseColor) || string.IsNullOrEmpty(baseColor))
        {
            baseColor = "000000";
        }

        return Brightness == 0 ? baseColor : Shade(baseColor, Brightness);
    }

    /// <summary>
    /// Normalizes a hexadecimal colour to six upper-case digits.
    /// </summary>
    /// <param name="value">The colour, with or without a hash, in three, six or eight (ARGB) digits.</param>
    /// <returns>The normalized colour, or <see langword="null"/> when the value is not hexadecimal.</returns>
    public static string NormalizeHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = value.Trim().TrimStart('#');

        if (digits.Length == 8)
        {
            digits = digits[2..];
        }

        if (digits.Length == 3)
        {
            digits = string.Concat(digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]);
        }

        if (digits.Length != 6)
        {
            return null;
        }

        foreach (var character in digits)
        {
            if (!Uri.IsHexDigit(character))
            {
                return null;
            }
        }

        return digits.ToUpperInvariant();
    }

    /// <summary>
    /// Lightens (positive amount) or darkens (negative amount) a colour the way PowerPoint's theme shades do,
    /// by moving its luminance towards white or black.
    /// </summary>
    /// <param name="hex">The colour as six hexadecimal digits.</param>
    /// <param name="amount">The fraction to move, from -1 (black) to 1 (white).</param>
    /// <returns>The shaded colour.</returns>
    public static string Shade(string hex, double amount)
    {
        var normalized = NormalizeHex(hex) ?? "000000";
        var (red, green, blue) = ToRgb(normalized);
        var (hue, saturation, luminance) = ToHsl(red, green, blue);

        luminance = amount >= 0
            ? luminance + ((1 - luminance) * amount)
            : luminance * (1 + amount);

        var (r, g, b) = FromHsl(hue, saturation, Math.Clamp(luminance, 0, 1));

        return string.Create(CultureInfo.InvariantCulture, $"{r:X2}{g:X2}{b:X2}");
    }

    /// <summary>
    /// Applies a luminance modulation and offset, as a theme colour's <c>lumMod</c> and <c>lumOff</c> do.
    /// </summary>
    /// <param name="hex">The colour as six hexadecimal digits.</param>
    /// <param name="modulation">The factor applied to the luminance, where 1 leaves it unchanged.</param>
    /// <param name="offset">The amount added to the luminance afterwards, from -1 to 1.</param>
    /// <returns>The adjusted colour.</returns>
    public static string ModulateLuminance(string hex, double modulation, double offset)
    {
        var normalized = NormalizeHex(hex) ?? "000000";
        var (red, green, blue) = ToRgb(normalized);
        var (hue, saturation, luminance) = ToHsl(red, green, blue);
        var (r, g, b) = FromHsl(hue, saturation, Math.Clamp((luminance * modulation) + offset, 0, 1));

        return string.Create(CultureInfo.InvariantCulture, $"{r:X2}{g:X2}{b:X2}");
    }

    /// <summary>
    /// Mixes a colour towards white, as a theme colour's <c>tint</c> does.
    /// </summary>
    /// <param name="hex">The colour as six hexadecimal digits.</param>
    /// <param name="tint">The fraction of the original colour kept, from 0 (white) to 1 (unchanged).</param>
    /// <returns>The tinted colour.</returns>
    public static string Tint(string hex, double tint)
    {
        var (red, green, blue) = ToRgb(NormalizeHex(hex) ?? "000000");
        var factor = Math.Clamp(tint, 0, 1);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)Math.Round(255 - ((255 - red) * factor)):X2}{(int)Math.Round(255 - ((255 - green) * factor)):X2}{(int)Math.Round(255 - ((255 - blue) * factor)):X2}");
    }

    /// <summary>
    /// Mixes a colour towards black, as a theme colour's <c>shade</c> does.
    /// </summary>
    /// <param name="hex">The colour as six hexadecimal digits.</param>
    /// <param name="shade">The fraction of the original colour kept, from 0 (black) to 1 (unchanged).</param>
    /// <returns>The shaded colour.</returns>
    public static string Darken(string hex, double shade)
    {
        var (red, green, blue) = ToRgb(NormalizeHex(hex) ?? "000000");
        var factor = Math.Clamp(shade, 0, 1);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)Math.Round(red * factor):X2}{(int)Math.Round(green * factor):X2}{(int)Math.Round(blue * factor):X2}");
    }

    /// <summary>
    /// Computes the relative luminance of a colour as the WCAG contrast formula defines it.
    /// </summary>
    /// <param name="hex">The colour as six hexadecimal digits.</param>
    /// <returns>The relative luminance, from 0 (black) to 1 (white).</returns>
    public static double RelativeLuminance(string hex)
    {
        var (red, green, blue) = ToRgb(NormalizeHex(hex) ?? "000000");

        return (0.2126 * Linearize(red)) + (0.7152 * Linearize(green)) + (0.0722 * Linearize(blue));
    }

    /// <summary>
    /// Computes the WCAG contrast ratio between two colours.
    /// </summary>
    /// <param name="foreground">The text colour as six hexadecimal digits.</param>
    /// <param name="background">The background colour as six hexadecimal digits.</param>
    /// <returns>The contrast ratio, from 1 (none) to 21 (black on white).</returns>
    public static double ContrastRatio(string foreground, string background)
    {
        var first = RelativeLuminance(foreground);
        var second = RelativeLuminance(background);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Describes the colour for a message.
    /// </summary>
    /// <returns>The colour as the caller would write it.</returns>
    public override string ToString()
    {
        if (IsNone)
        {
            return "none";
        }

        if (!IsTheme)
        {
            return "#" + Hex;
        }

        if (Brightness == 0)
        {
            return ThemeSlot;
        }

        var percent = Math.Round(Math.Abs(Brightness) * 100).ToString(CultureInfo.InvariantCulture);

        return Brightness > 0
            ? $"{ThemeSlot} lighter {percent}%"
            : $"{ThemeSlot} darker {percent}%";
    }

    private static double Linearize(int channel)
    {
        var value = channel / 255d;

        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static (int Red, int Green, int Blue) ToRgb(string hex)
    {
        return (
            int.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static (double Hue, double Saturation, double Luminance) ToHsl(int red, int green, int blue)
    {
        var r = red / 255d;
        var g = green / 255d;
        var b = blue / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var luminance = (max + min) / 2;

        if (max == min)
        {
            return (0, 0, luminance);
        }

        var delta = max - min;
        var saturation = luminance > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;

        if (max == r)
        {
            hue = ((g - b) / delta) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            hue = ((b - r) / delta) + 2;
        }
        else
        {
            hue = ((r - g) / delta) + 4;
        }

        return (hue / 6, saturation, luminance);
    }

    private static (int Red, int Green, int Blue) FromHsl(double hue, double saturation, double luminance)
    {
        if (saturation == 0)
        {
            var gray = (int)Math.Round(luminance * 255);

            return (gray, gray, gray);
        }

        var q = luminance < 0.5 ? luminance * (1 + saturation) : luminance + saturation - (luminance * saturation);
        var p = (2 * luminance) - q;

        return (
            (int)Math.Round(HueToChannel(p, q, hue + (1d / 3)) * 255),
            (int)Math.Round(HueToChannel(p, q, hue) * 255),
            (int)Math.Round(HueToChannel(p, q, hue - (1d / 3)) * 255));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1d / 6)
        {
            return p + ((q - p) * 6 * t);
        }

        if (t < 1d / 2)
        {
            return q;
        }

        if (t < 2d / 3)
        {
            return p + ((q - p) * ((2d / 3) - t) * 6);
        }

        return p;
    }

    [GeneratedRegex(@"^(?<base>.+?)\s+(?<direction>lighter|darker|light|dark)\s+(?<amount>\d+(\.\d+)?)\s*%?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShadeExpression();
}
