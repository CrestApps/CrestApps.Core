using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Reading;

/// <summary>
/// Looks up a document's styles and list definitions: a style's name, the outline level it inherits, and
/// whether a paragraph is a bulleted or numbered list item.
/// </summary>
internal sealed class WordStyleIndex
{
    private readonly Dictionary<string, Style> _styles = new(StringComparer.Ordinal);
    private readonly Dictionary<int, AbstractNum> _abstractByNumber = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="WordStyleIndex"/> class.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    public WordStyleIndex(MainDocumentPart mainPart)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        foreach (var style in mainPart.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is { } id)
            {
                _styles.TryAdd(id, style);
            }
        }

        DefaultParagraphStyleId = _styles.Values
            .FirstOrDefault(style => style.Type?.Value == StyleValues.Paragraph && style.Default?.Value == true)?.StyleId?.Value
            ?? WordStyleSheet.Normal;

        var numbering = mainPart.NumberingDefinitionsPart?.Numbering;

        if (numbering is null)
        {
            return;
        }

        var abstracts = numbering.Elements<AbstractNum>()
            .Where(definition => definition.AbstractNumberId is not null)
            .GroupBy(definition => definition.AbstractNumberId.Value)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var instance in numbering.Elements<NumberingInstance>())
        {
            if (instance.NumberID?.Value is { } numberId &&
                instance.AbstractNumId?.Val?.Value is { } abstractId &&
                abstracts.TryGetValue(abstractId, out var definition))
            {
                _abstractByNumber.TryAdd(numberId, definition);
            }
        }
    }

    /// <summary>
    /// Gets the id of the style a paragraph has when it names none.
    /// </summary>
    public string DefaultParagraphStyleId { get; }

    /// <summary>
    /// Finds a style by id.
    /// </summary>
    /// <param name="styleId">The id.</param>
    /// <returns>The style, or <see langword="null"/>.</returns>
    public Style Find(string styleId)
    {
        return styleId is not null && _styles.TryGetValue(styleId, out var style) ? style : null;
    }

    /// <summary>
    /// Gets every style.
    /// </summary>
    public IEnumerable<Style> All => _styles.Values;

    /// <summary>
    /// Returns a style's display name.
    /// </summary>
    /// <param name="styleId">The id.</param>
    /// <returns>The name, or the id when the style has no name.</returns>
    public string NameOf(string styleId)
    {
        return Find(styleId)?.StyleName?.Val?.Value ?? styleId;
    }

    /// <summary>
    /// Returns the effective paragraph style id of a paragraph.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <returns>The style id.</returns>
    public string StyleOf(Paragraph paragraph)
    {
        return paragraph?.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? DefaultParagraphStyleId;
    }

    /// <summary>
    /// Returns a paragraph's outline level: its own, or the one its style or a style it is based on gives it.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <returns>The outline level from 0 (a top-level heading), or <see langword="null"/> for body text.</returns>
    public int? OutlineLevelOf(Paragraph paragraph)
    {
        if (paragraph?.ParagraphProperties?.OutlineLevel?.Val?.Value is { } direct)
        {
            return direct is < 0 or >= 9 ? null : direct;
        }

        foreach (var style in Chain(StyleOf(paragraph)))
        {
            if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is { } level)
            {
                return level is < 0 or >= 9 ? null : level;
            }

            if (style.StyleName?.Val?.Value is { } name &&
                name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name.AsSpan(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                number is >= 1 and <= 9)
            {
                return number - 1;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether a paragraph is in, or inherits, a style with a given built-in name.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="names">The style names to look for.</param>
    /// <returns><see langword="true"/> when the paragraph's style or one it is based on has one of the names.</returns>
    public bool HasStyle(Paragraph paragraph, params string[] names)
    {
        foreach (var style in Chain(StyleOf(paragraph)))
        {
            foreach (var name in names)
            {
                if (WordStyleSheet.NamesMatch(style.StyleName?.Val?.Value, name) || WordStyleSheet.NamesMatch(style.StyleId?.Value, name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a paragraph's list membership: its numbering instance and level, from the paragraph or its style.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="numberId">The numbering instance.</param>
    /// <param name="level">The level from 0.</param>
    /// <returns><see langword="true"/> when the paragraph is a list item.</returns>
    public bool TryGetList(Paragraph paragraph, out int numberId, out int level)
    {
        numberId = 0;
        level = 0;

        var properties = paragraph?.ParagraphProperties?.NumberingProperties;
        int? number = properties?.NumberingId?.Val?.Value;
        int? itemLevel = properties?.NumberingLevelReference?.Val?.Value;

        if (number is null)
        {
            foreach (var style in Chain(StyleOf(paragraph)))
            {
                if (style.StyleParagraphProperties?.NumberingProperties is { } styled)
                {
                    number = styled.NumberingId?.Val?.Value;
                    itemLevel ??= styled.NumberingLevelReference?.Val?.Value;

                    break;
                }
            }
        }

        // Numbering instance 0 is how a paragraph says it is not in a list.
        if (number is null or 0)
        {
            return false;
        }

        numberId = number.Value;
        level = itemLevel ?? 0;

        return true;
    }

    /// <summary>
    /// Returns the definition of one level of a list.
    /// </summary>
    /// <param name="numberId">The numbering instance.</param>
    /// <param name="level">The level from 0.</param>
    /// <returns>The level definition, or <see langword="null"/>.</returns>
    public Level LevelOf(int numberId, int level)
    {
        return _abstractByNumber.TryGetValue(numberId, out var definition)
            ? definition.Elements<Level>().FirstOrDefault(candidate => candidate.LevelIndex?.Value == level)
            : null;
    }

    /// <summary>
    /// Returns whether one level of a list is bulleted.
    /// </summary>
    /// <param name="numberId">The numbering instance.</param>
    /// <param name="level">The level from 0.</param>
    /// <returns><see langword="true"/> for a bulleted level.</returns>
    public bool IsBullet(int numberId, int level)
    {
        var format = LevelOf(numberId, level)?.NumberingFormat?.Val;

        return format is null || format.Value == NumberFormatValues.Bullet;
    }

    /// <summary>
    /// Returns a style and the styles it is based on, nearest first.
    /// </summary>
    /// <param name="styleId">The style id.</param>
    /// <returns>The chain, stopping at a loop.</returns>
    public IEnumerable<Style> Chain(string styleId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = Find(styleId);

        while (current is not null && seen.Add(current.StyleId?.Value ?? string.Empty))
        {
            yield return current;

            current = Find(current.BasedOn?.Val?.Value);
        }
    }
}
