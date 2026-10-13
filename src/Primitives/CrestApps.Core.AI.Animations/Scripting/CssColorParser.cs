using System.Globalization;
using System.Reflection;
using SkiaSharp;

namespace CrestApps.Core.AI.Animations.Scripting;

/// <summary>
/// Reads the CSS colours an animation script assigns to its styles: hex, <c>rgb()</c>, <c>rgba()</c>,
/// <c>hsl()</c>, <c>hsla()</c> and the CSS colour names.
/// </summary>
internal static class CssColorParser
{
    private static readonly Dictionary<string, SKColor> _namedColors = typeof(SKColors)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(SKColor))
        .ToDictionary(field => field.Name, field => (SKColor)field.GetValue(null), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a colour, or returns opaque black for anything that is not one, as a browser canvas keeps its
    /// previous colour rather than failing.
    /// </summary>
    /// <param name="text">The colour as the script wrote it.</param>
    public static SKColor Parse(string text)
    {
        var value = text?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return SKColors.Black;
        }

        if (value[0] == '#')
        {
            return ParseHex(value);
        }

        var open = value.IndexOf('(');

        if (open > 0 && value[^1] == ')')
        {
            var function = value[..open].Trim().ToLowerInvariant();
            var parts = value[(open + 1)..^1].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length >= 3)
            {
                var alpha = parts.Length >= 4 ? ParseAlpha(parts[3]) : (byte)255;

                if (function is "rgb" or "rgba")
                {
                    return new SKColor(ParseChannel(parts[0]), ParseChannel(parts[1]), ParseChannel(parts[2]), alpha);
                }

                if (function is "hsl" or "hsla")
                {
                    return SKColor.FromHsl(ParseNumber(parts[0]), ParseNumber(parts[1]), ParseNumber(parts[2]), alpha);
                }
            }

            return SKColors.Black;
        }

        if (string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            return SKColors.Transparent;
        }

        return _namedColors.TryGetValue(value, out var named) ? named : SKColors.Black;
    }

    private static SKColor ParseHex(string value)
    {
        var hex = value[1..];

        // #rgb and #rgba double each digit, as CSS does.
        if (hex.Length is 3 or 4)
        {
            hex = string.Concat(hex.Select(digit => new string(digit, 2)));
        }

        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
        {
            return new SKColor((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        return SKColors.Black;
    }

    private static byte ParseChannel(string part)
    {
        var value = part.EndsWith('%') ? ParseNumber(part) * 2.55f : ParseNumber(part);

        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }

    private static byte ParseAlpha(string part)
    {
        var value = part.EndsWith('%') ? ParseNumber(part) / 100 : ParseNumber(part);

        return (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }

    private static float ParseNumber(string part)
    {
        var trimmed = part.TrimEnd('%').Replace("deg", string.Empty, StringComparison.OrdinalIgnoreCase);

        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}
