using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Rendering;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Fields;

/// <summary>
/// Writes numbered captions — <c>Figure 2: Revenue by region</c> — with a sequence field for the number, so Word
/// renumbers them, a list of figures can collect them and a cross-reference can point at them.
/// </summary>
internal static class WordCaptions
{
    /// <summary>
    /// Normalizes a caption label a model wrote: <c>figure</c>, <c>image</c>, <c>chart</c> and <c>picture</c> are
    /// figures; <c>table</c> is a table; anything else is used as written.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <returns>The label to number by.</returns>
    public static string NormalizeLabel(string label)
    {
        var text = (label ?? string.Empty).Trim();

        return text.ToLowerInvariant() switch
        {
            "" or "figure" or "fig" or "image" or "picture" or "chart" or "graph" or "diagram" => "Figure",
            "table" or "tbl" => "Table",
            "equation" or "eq" => "Equation",
            _ => char.ToUpperInvariant(text[0]) + text[1..],
        };
    }

    /// <summary>
    /// Builds a caption paragraph.
    /// </summary>
    /// <param name="writer">The block writer, for the caption style.</param>
    /// <param name="label">The label, such as <c>Figure</c> or <c>Table</c>.</param>
    /// <param name="text">The caption text after the number, with inline Markdown; may be empty.</param>
    /// <param name="number">The number shown until fields are updated.</param>
    /// <param name="bookmarkName">A bookmark around the label and number, for cross-references, or <see langword="null"/>.</param>
    /// <param name="bookmarkId">The bookmark id.</param>
    /// <returns>The paragraph.</returns>
    public static Paragraph Create(WordBlockWriter writer, string label, string text, int number, string bookmarkName = null, int bookmarkId = 0)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var normalized = NormalizeLabel(label);
        var paragraph = new Paragraph(new ParagraphProperties
        {
            ParagraphStyleId = new ParagraphStyleId { Val = writer.Style(WordStyleSheet.Caption) },
            KeepNext = normalized == "Table" ? new KeepNext() : null,
        });

        if (bookmarkName is not null)
        {
            paragraph.Append(new BookmarkStart { Name = bookmarkName, Id = bookmarkId.ToString(CultureInfo.InvariantCulture) });
        }

        paragraph.Append(WordInlineWriter.CreateRun(normalized + " "));
        WordFieldWriter.Append(paragraph, "SEQ " + Quote(normalized) + " \\* ARABIC", number.ToString(CultureInfo.InvariantCulture));

        if (bookmarkName is not null)
        {
            paragraph.Append(new BookmarkEnd { Id = bookmarkId.ToString(CultureInfo.InvariantCulture) });
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            paragraph.Append(WordInlineWriter.CreateRun(": "));
            WordInlineWriter.AppendMarkdown(paragraph, text.Trim(), writer.Part);
        }

        return paragraph;
    }

    /// <summary>
    /// Numbers every sequence field — figures, tables, equations — in document order, so captions read 1, 2, 3
    /// however they were inserted.
    /// </summary>
    /// <remarks>
    /// The switches keep Word's meaning: <c>\c</c> repeats the current number, <c>\r n</c> resets it to n,
    /// <c>\s n</c> restarts it after each heading of level n or higher — for chapter numbers such as 2-1 — and
    /// <c>\h</c> hides it. A <c>\*</c> switch sets how the number is written: <c>ARABIC</c>, <c>ROMAN</c>,
    /// <c>roman</c>, <c>ALPHABETIC</c> or <c>alphabetic</c>; a field written another way, such as in words, is
    /// counted but keeps the result Word last wrote.
    /// </remarks>
    /// <param name="package">The document.</param>
    /// <returns>How many captions each label has.</returns>
    public static Dictionary<string, int> Renumber(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var chapters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fields = WordFieldScanner.Scan(package.Body).Where(field => field.Type == "SEQ").ToList();

        if (fields.Count == 0)
        {
            return counters;
        }

        // The headings before each field, counted by level, tell \s n which chapter the field is in.
        var styles = new WordStyleIndex(package.MainPart);
        var headings = new int[9];
        var byParagraph = fields.Where(field => field.Paragraph is not null).ToLookup(field => field.Paragraph, ReferenceEqualityComparer.Instance);
        var ordered = new List<(WordField Field, string Chapter)>();

        foreach (var paragraph in package.Body.Descendants<Paragraph>())
        {
            if (styles.OutlineLevelOf(paragraph) is { } level)
            {
                headings[level]++;
                Array.Clear(headings, level + 1, headings.Length - level - 1);
            }

            foreach (var field in byParagraph[paragraph])
            {
                ordered.Add((field, string.Join('.', headings)));
            }
        }

        foreach (var field in fields.Where(field => field.Paragraph is null))
        {
            ordered.Add((field, string.Empty));
        }

        foreach (var (field, chapter) in ordered)
        {
            var instruction = field.Instruction;
            var name = WordFieldScanner.ArgumentOf(instruction);

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (ChapterLevel(instruction) is { } chapterLevel)
            {
                var key = string.Join('.', chapter.Split('.').Take(chapterLevel));

                if (!chapters.TryGetValue(name, out var previous) || !string.Equals(previous, key, StringComparison.Ordinal))
                {
                    chapters[name] = key;
                    counters[name] = 0;
                }
            }

            var current = counters.GetValueOrDefault(name);
            int value;

            if (WordFieldScanner.HasSwitch(instruction, 'c'))
            {
                value = current;
            }
            else if (TryReadReset(instruction, out var reset))
            {
                value = reset;
                counters[name] = reset;
            }
            else
            {
                value = current + 1;
                counters[name] = value;
            }

            var formats = WordFieldScanner.SwitchArguments(instruction, '*');

            // A hidden field shows nothing unless it is given a number format.
            if (WordFieldScanner.HasSwitch(instruction, 'h') && formats.Count == 0)
            {
                continue;
            }

            if (Format(value, formats) is { } result)
            {
                WordFieldScanner.SetResult(field, result);
            }
        }

        return counters;
    }

    /// <summary>
    /// Writes a sequence number the way a field's <c>\*</c> switches ask.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="formats">The arguments of the field's <c>\*</c> switches.</param>
    /// <returns>The number as text, or <see langword="null"/> for a format this host does not write.</returns>
    public static string Format(int value, IReadOnlyList<string> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        // MERGEFORMAT and CHARFORMAT say how the result is formatted, not how the number is written.
        var format = formats.FirstOrDefault(item => !item.Equals("MERGEFORMAT", StringComparison.OrdinalIgnoreCase) && !item.Equals("CHARFORMAT", StringComparison.OrdinalIgnoreCase));

        if (format is null || format.Equals("Arabic", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // The case of the switch is the case of the number: ROMAN is XIV, roman is xiv.
        var upper = char.IsUpper(format[0]);

        if (format.Equals("Roman", StringComparison.OrdinalIgnoreCase))
        {
            return value is > 0 and < 4000 ? WordListCounter.FormatNumber(value, upper ? "upperRoman" : "lowerRoman") : value.ToString(CultureInfo.InvariantCulture);
        }

        if (format.Equals("Alphabetic", StringComparison.OrdinalIgnoreCase))
        {
            return value > 0 ? WordListCounter.FormatNumber(value, upper ? "upperLetter" : "lowerLetter") : value.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static int? ChapterLevel(string instruction)
    {
        if (!WordFieldScanner.HasSwitch(instruction, 's'))
        {
            return null;
        }

        var argument = WordFieldScanner.SwitchArguments(instruction, 's').FirstOrDefault();

        return int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? Math.Clamp(level, 1, 9) : 1;
    }

    private static bool TryReadReset(string instruction, out int value)
    {
        value = 0;

        return WordFieldScanner.SwitchArguments(instruction, 'r').FirstOrDefault() is { } argument &&
            int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string Quote(string label)
    {
        return label.Contains(' ', StringComparison.Ordinal) ? "\"" + label + "\"" : label;
    }
}
