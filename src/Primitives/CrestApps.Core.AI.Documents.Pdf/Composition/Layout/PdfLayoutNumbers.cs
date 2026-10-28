using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// Writes page numbers the way a field's format asks: digits, Roman numerals or letters.
/// </summary>
internal static class PdfLayoutNumbers
{
    private static readonly (int Value, string Numeral)[] _roman =
    [
        (1000, "m"),
        (900, "cm"),
        (500, "d"),
        (400, "cd"),
        (100, "c"),
        (90, "xc"),
        (50, "l"),
        (40, "xl"),
        (10, "x"),
        (9, "ix"),
        (5, "v"),
        (4, "iv"),
        (1, "i"),
    ];

    /// <summary>
    /// Writes a number.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="format"><c>roman</c>, <c>ROMAN</c>, <c>alphabetic</c>, <c>ALPHABETIC</c>, or <see langword="null"/> for digits.</param>
    /// <returns>The text.</returns>
    public static string Format(int value, string format)
    {
        if (value <= 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return format switch
        {
            "roman" => Roman(value),
            "ROMAN" => Roman(value).ToUpperInvariant(),
            "alphabetic" => Alphabetic(value),
            "ALPHABETIC" => Alphabetic(value).ToUpperInvariant(),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string Roman(int value)
    {
        var builder = new StringBuilder();
        var remaining = Math.Min(value, 3999);

        foreach (var (amount, numeral) in _roman)
        {
            while (remaining >= amount)
            {
                builder.Append(numeral);
                remaining -= amount;
            }
        }

        return builder.ToString();
    }

    private static string Alphabetic(int value)
    {
        // a to z, then aa to zz, as word processors letter pages.
        var letter = (char)('a' + ((value - 1) % 26));

        return new string(letter, ((value - 1) / 26) + 1);
    }
}
