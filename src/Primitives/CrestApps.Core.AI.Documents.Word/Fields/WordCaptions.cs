using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
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
    /// <param name="package">The document.</param>
    /// <returns>How many captions each label has.</returns>
    public static Dictionary<string, int> Renumber(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in WordFieldScanner.Scan(package.Body).Where(field => field.Type == "SEQ"))
        {
            var name = WordFieldScanner.ArgumentOf(field.Instruction);

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // A field that repeats the current number (\c) or resets it (\r n) keeps Word's meaning.
            var instruction = field.Instruction;
            var current = counters.GetValueOrDefault(name);
            int value;

            if (instruction.Contains("\\c", StringComparison.OrdinalIgnoreCase))
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

            WordFieldScanner.SetResult(field, value.ToString(CultureInfo.InvariantCulture));
        }

        return counters;
    }

    private static bool TryReadReset(string instruction, out int value)
    {
        value = 0;

        var index = instruction.IndexOf("\\r", StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return false;
        }

        var rest = instruction[(index + 2)..].TrimStart();
        var end = 0;

        while (end < rest.Length && char.IsAsciiDigit(rest[end]))
        {
            end++;
        }

        return end > 0 && int.TryParse(rest.AsSpan(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string Quote(string label)
    {
        return label.Contains(' ', StringComparison.Ordinal) ? "\"" + label + "\"" : label;
    }
}
