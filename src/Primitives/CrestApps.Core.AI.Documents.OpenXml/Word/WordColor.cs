using System.Globalization;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Reads the colors a model writes — <c>#1F4E79</c>, <c>1F4E79</c>, <c>#ABC</c>, <c>rgb(31, 78, 121)</c> or a
/// common color name — as the six-digit hexadecimal value a word-processing document stores.
/// </summary>
internal static class WordColor
{
    private static readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "000000",
        ["white"] = "FFFFFF",
        ["red"] = "C00000",
        ["darkred"] = "8B0000",
        ["green"] = "00B050",
        ["darkgreen"] = "006400",
        ["blue"] = "0070C0",
        ["darkblue"] = "002060",
        ["navy"] = "1F3864",
        ["lightblue"] = "DEEAF6",
        ["skyblue"] = "87CEEB",
        ["teal"] = "008080",
        ["cyan"] = "00B0F0",
        ["yellow"] = "FFFF00",
        ["gold"] = "FFC000",
        ["orange"] = "ED7D31",
        ["purple"] = "7030A0",
        ["violet"] = "8E44AD",
        ["pink"] = "FF66CC",
        ["magenta"] = "FF00FF",
        ["brown"] = "843C0C",
        ["maroon"] = "800000",
        ["olive"] = "808000",
        ["gray"] = "808080",
        ["grey"] = "808080",
        ["darkgray"] = "404040",
        ["darkgrey"] = "404040",
        ["lightgray"] = "D9D9D9",
        ["lightgrey"] = "D9D9D9",
        ["silver"] = "C0C0C0",
        ["beige"] = "F5F5DC",
        ["ivory"] = "FFFFF0",
        ["charcoal"] = "333333",
        ["slate"] = "44546A",
        ["indigo"] = "3F51B5",
        ["coral"] = "FF7F50",
        ["salmon"] = "FA8072",
        ["lime"] = "92D050",
        ["mint"] = "C6EFCE",
    };

    /// <summary>
    /// Reads a color.
    /// </summary>
    /// <param name="value">The color as written.</param>
    /// <param name="hex">The six-digit uppercase hexadecimal value, without a leading <c>#</c>.</param>
    /// <returns><see langword="true"/> when the value is a color.</returns>
    public static bool TryParse(string value, out string hex)
    {
        hex = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (_names.TryGetValue(text.Replace(" ", string.Empty, StringComparison.Ordinal), out var named))
        {
            hex = named;

            return true;
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = text.IndexOf('(', StringComparison.Ordinal);
            var close = text.IndexOf(')', StringComparison.Ordinal);

            if (open < 0 || close <= open)
            {
                return false;
            }

            var parts = text[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);

            if (parts.Length < 3)
            {
                return false;
            }

            var channels = new int[3];

            for (var index = 0; index < 3; index++)
            {
                if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[index]))
                {
                    return false;
                }

                channels[index] = Math.Clamp(channels[index], 0, 255);
            }

            hex = string.Create(CultureInfo.InvariantCulture, $"{channels[0]:X2}{channels[1]:X2}{channels[2]:X2}");

            return true;
        }

        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length == 3 && text.All(char.IsAsciiHexDigit))
        {
            hex = string.Concat(text[0], text[0], text[1], text[1], text[2], text[2]).ToUpperInvariant();

            return true;
        }

        if (text.Length == 6 && text.All(char.IsAsciiHexDigit))
        {
            hex = text.ToUpperInvariant();

            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a color, falling back when the value is not one.
    /// </summary>
    /// <param name="value">The color as written.</param>
    /// <param name="fallback">The value used when <paramref name="value"/> is not a color.</param>
    /// <returns>The six-digit hexadecimal value.</returns>
    public static string ParseOrDefault(string value, string fallback)
    {
        return TryParse(value, out var hex) ? hex : fallback;
    }

    /// <summary>
    /// Mixes a color with white, for the light tints a table band or a callout is shaded with.
    /// </summary>
    /// <param name="hex">The six-digit hexadecimal color.</param>
    /// <param name="amount">How much white is mixed in, from 0 (none) to 1 (white).</param>
    /// <returns>The tint.</returns>
    public static string Tint(string hex, double amount)
    {
        if (!TryParse(hex, out var color))
        {
            return "FFFFFF";
        }

        var red = int.Parse(color.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = int.Parse(color.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = int.Parse(color.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        static int Mix(int channel, double amount) => (int)Math.Round(channel + ((255 - channel) * Math.Clamp(amount, 0, 1)));

        return string.Create(CultureInfo.InvariantCulture, $"{Mix(red, amount):X2}{Mix(green, amount):X2}{Mix(blue, amount):X2}");
    }

    /// <summary>
    /// Returns whether text drawn in white reads better on a fill than text drawn in black.
    /// </summary>
    /// <param name="hex">The fill color.</param>
    /// <returns><see langword="true"/> for a dark fill.</returns>
    public static bool IsDark(string hex)
    {
        if (!TryParse(hex, out var color))
        {
            return false;
        }

        var red = int.Parse(color.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = int.Parse(color.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = int.Parse(color.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return (0.299 * red) + (0.587 * green) + (0.114 * blue) < 150;
    }
}
