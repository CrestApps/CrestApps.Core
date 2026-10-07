using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// Counts list items as Word does and writes their markers: <c>1.</c>, <c>a)</c>, <c>iv.</c>, <c>1.2.3</c> or a
/// bullet, restarting deeper levels when a shallower one advances.
/// </summary>
internal sealed class WordListCounter
{
    private const int MaxLetterRepeats = 10;

    private readonly Dictionary<int, NumberingInstance> _instances = [];
    private readonly Dictionary<int, AbstractNum> _definitions = [];
    private readonly Dictionary<string, int[]> _counters = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="WordListCounter"/> class.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    public WordListCounter(MainDocumentPart mainPart)
    {
        var numbering = mainPart?.NumberingDefinitionsPart?.Numbering;

        if (numbering is null)
        {
            return;
        }

        foreach (var definition in numbering.Elements<AbstractNum>())
        {
            if (definition.AbstractNumberId?.Value is { } id)
            {
                _definitions.TryAdd(id, definition);
            }
        }

        foreach (var instance in numbering.Elements<NumberingInstance>())
        {
            if (instance.NumberID?.Value is { } id)
            {
                _instances.TryAdd(id, instance);
            }
        }
    }

    /// <summary>
    /// Advances a list and returns the marker of its next item.
    /// </summary>
    /// <param name="numberId">The numbering instance.</param>
    /// <param name="level">The level from 0.</param>
    /// <param name="definition">The level definition, for the marker's formatting.</param>
    /// <returns>The marker text, or <see langword="null"/> when the list is undefined or has no marker.</returns>
    public string Next(int numberId, int level, out Level definition)
    {
        definition = null;

        if (!_instances.TryGetValue(numberId, out var instance) ||
            instance.AbstractNumId?.Val?.Value is not { } abstractId ||
            !_definitions.TryGetValue(abstractId, out var abstractNum))
        {
            return null;
        }

        level = Math.Clamp(level, 0, 8);

        // A malformed numbering part can repeat a level; the first one wins, as it does in Word.
        var levels = new Dictionary<int, Level>();
        var overrides = new Dictionary<int, LevelOverride>();

        foreach (var item in abstractNum.Elements<Level>())
        {
            levels.TryAdd(item.LevelIndex?.Value ?? 0, item);
        }

        foreach (var item in instance.Elements<LevelOverride>())
        {
            overrides.TryAdd(item.LevelIndex?.Value ?? 0, item);
        }

        if (overrides.TryGetValue(level, out var levelOverride) && levelOverride.Level is { } replacement)
        {
            levels[level] = replacement;
        }

        if (!levels.TryGetValue(level, out definition))
        {
            return null;
        }

        // Instances that restart their numbering keep their own count; the others continue the definition's.
        var key = overrides.Count > 0 ? "num:" + numberId.ToString(CultureInfo.InvariantCulture) : "abs:" + abstractId.ToString(CultureInfo.InvariantCulture);

        if (!_counters.TryGetValue(key, out var counters))
        {
            counters = new int[9];

            for (var index = 0; index < 9; index++)
            {
                counters[index] = StartOf(levels, overrides, index) - 1;
            }

            _counters[key] = counters;
        }

        counters[level]++;

        for (var deeper = level + 1; deeper < 9; deeper++)
        {
            counters[deeper] = StartOf(levels, overrides, deeper) - 1;
        }

        var format = definition.NumberingFormat?.Val?.InnerText ?? "decimal";
        var text = definition.LevelText?.Val?.Value ?? string.Empty;

        if (format == "bullet")
        {
            return MapSymbol(text);
        }

        if (format == "none")
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '%' && index + 1 < text.Length && char.IsAsciiDigit(text[index + 1]))
            {
                var referenced = text[index + 1] - '1';

                if (referenced >= 0 && referenced <= level)
                {
                    var referencedFormat = levels.TryGetValue(referenced, out var other) ? other.NumberingFormat?.Val?.InnerText ?? "decimal" : "decimal";

                    builder.Append(FormatNumber(Math.Max(counters[referenced], 0), referenced == level ? format : referencedFormat));
                }

                index++;

                continue;
            }

            builder.Append(text[index]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Records where every list stands, so content laid out a second time — a repeated header row, a text box
    /// measured before it is drawn — does not number its items again.
    /// </summary>
    /// <returns>The state, for <see cref="Restore"/>.</returns>
    public Dictionary<string, int[]> Snapshot()
    {
        return _counters.ToDictionary(pair => pair.Key, pair => (int[])pair.Value.Clone(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Puts every list back where a <see cref="Snapshot"/> recorded it.
    /// </summary>
    /// <param name="snapshot">The state.</param>
    public void Restore(Dictionary<string, int[]> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _counters.Clear();

        foreach (var (key, value) in snapshot)
        {
            _counters[key] = (int[])value.Clone();
        }
    }

    /// <summary>
    /// Formats a list number.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <param name="format">The numbering format, such as <c>decimal</c>, <c>lowerLetter</c> or <c>upperRoman</c>.</param>
    /// <returns>The number as text.</returns>
    public static string FormatNumber(int number, string format)
    {
        return format switch
        {
            "lowerLetter" => Letters(number).ToLowerInvariant(),
            "upperLetter" => Letters(number),
            "lowerRoman" => Roman(number).ToLowerInvariant(),
            "upperRoman" => Roman(number),
            "decimalZero" => number.ToString("00", CultureInfo.InvariantCulture),
            "ordinal" => number.ToString(CultureInfo.InvariantCulture) + Suffix(number),
            _ => number.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static int StartOf(Dictionary<int, Level> levels, Dictionary<int, LevelOverride> overrides, int level)
    {
        if (overrides.TryGetValue(level, out var levelOverride) && levelOverride.StartOverrideNumberingValue?.Val?.Value is { } start)
        {
            return start;
        }

        return levels.TryGetValue(level, out var definition) ? definition.StartNumberingValue?.Val?.Value ?? 1 : 1;
    }

    private static string MapSymbol(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "•";
        }

        // Bullets stored as Symbol and Wingdings characters in the private-use area are shown as the shape they
        // draw in those fonts.
        return text[0] switch
        {
            '' or '' or '' => "•",
            '' or '' => "▪",
            '' or '' => "►",
            '' => "✓",
            'o' => "◦",
            _ => text,
        };
    }

    private static string Letters(int number)
    {
        if (number <= 0)
        {
            return string.Empty;
        }

        // Word repeats the letter past z: aa, bb, cc. A list started at a huge number would repeat a letter
        // millions of times, so past a few repeats the number is written in digits instead.
        if (number > 26 * MaxLetterRepeats)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var letter = (char)('A' + ((number - 1) % 26));

        return new string(letter, ((number - 1) / 26) + 1);
    }

    private static string Roman(int number)
    {
        if (number <= 0 || number >= 4000)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var builder = new StringBuilder();

        for (var index = 0; index < values.Length; index++)
        {
            while (number >= values[index])
            {
                builder.Append(symbols[index]);
                number -= values[index];
            }
        }

        return builder.ToString();
    }

    private static string Suffix(int number)
    {
        var lastTwo = number % 100;

        if (lastTwo is 11 or 12 or 13)
        {
            return "th";
        }

        return (number % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }
}
