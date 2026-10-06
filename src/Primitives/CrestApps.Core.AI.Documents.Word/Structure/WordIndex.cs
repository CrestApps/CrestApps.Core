using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Rendering;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Word.Structure;

/// <summary>
/// Writes and refreshes an index: hidden <c>XE</c> fields mark the entries where they occur, and an
/// <c>INDEX</c> field lists them alphabetically with the pages they are on.
/// </summary>
internal static class WordIndex
{
    /// <summary>
    /// Builds the hidden field that marks an index entry.
    /// </summary>
    /// <param name="term">The entry.</param>
    /// <param name="subentry">The subentry under it, or <see langword="null"/>.</param>
    /// <returns>The field's runs.</returns>
    public static List<Run> CreateEntry(string term, string subentry)
    {
        var text = Escape(term) + (string.IsNullOrWhiteSpace(subentry) ? string.Empty : ":" + Escape(subentry));
        var runs = new List<Run>
        {
            new(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new(new FieldCode(" XE \"" + text + "\" ") { Space = SpaceProcessingModeValues.Preserve }),
            new(new FieldChar { FieldCharType = FieldCharValues.End }),
        };

        // Word writes entry markers as hidden text, so they never show in the document.
        foreach (var run in runs)
        {
            run.PrependChild(new RunProperties(new Vanish()));
        }

        return runs;
    }

    /// <summary>
    /// Builds an index at the end of a list of paragraphs: the INDEX field, filled with the document's entries.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="columns">The number of columns Word sets the index in.</param>
    /// <returns>The paragraphs.</returns>
    public static List<Paragraph> Create(WordPackage package, int columns)
    {
        var instruction = "INDEX" + (columns > 1 ? " \\c \"" + columns.ToString(CultureInfo.InvariantCulture) + "\"" : string.Empty) + " \\z \"1033\"";

        return BuildParagraphs(instruction, Entries(package), pages: null);
    }

    /// <summary>
    /// Rebuilds every index in a document from its entry markers, with page numbers from this host's layout.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="services">The request services, for the layout limits, or <see langword="null"/>.</param>
    public static void RefreshAll(WordPackage package, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(package);

        var indexes = WordFieldScanner.Scan(package.Body).Where(field => field.Type == "INDEX").ToList();

        if (indexes.Count == 0)
        {
            return;
        }

        var preview = services?.GetService<IOptions<WordPreviewOptions>>()?.Value ?? new WordPreviewOptions();
        var layout = WordLayoutEngine.Layout(package, new WordLayoutOptions { MaxPages = Math.Max(1, preview.MaxLayoutPages), IncludePictures = false });
        var entries = Entries(package);
        var pages = entries.ToDictionary(entry => entry.Key, entry => entry.Value.Select(field => layout.DisplayNumberOf(field.Paragraph)).Where(page => page.Length > 0).Distinct().ToList());

        foreach (var index in indexes)
        {
            var first = index.BeginRun?.Ancestors<Paragraph>().FirstOrDefault();
            var last = index.EndRun?.Ancestors<Paragraph>().FirstOrDefault();

            if (first?.Parent is null || last?.Parent is null || !ReferenceEquals(first.Parent, last.Parent))
            {
                continue;
            }

            var old = new List<Paragraph>();

            for (OpenXmlElement current = first; current is not null; current = current.NextSibling())
            {
                if (current is Paragraph paragraph)
                {
                    old.Add(paragraph);
                }

                if (ReferenceEquals(current, last))
                {
                    break;
                }
            }

            foreach (var paragraph in BuildParagraphs(index.Instruction, entries, pages))
            {
                package.Ids.Assign(paragraph);
                first.InsertBeforeSelf(paragraph);
            }

            foreach (var paragraph in old)
            {
                paragraph.Remove();
            }
        }
    }

    /// <summary>
    /// Reads the index entries a document marks, by entry text.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>The entry markers by "term" or "term:subentry".</returns>
    public static SortedDictionary<string, List<WordField>> Entries(WordPackage package)
    {
        var entries = new SortedDictionary<string, List<WordField>>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var field in WordFieldScanner.Scan(package.Body).Where(field => field.Type == "XE"))
        {
            var start = field.Instruction.IndexOf('"', StringComparison.Ordinal);
            var end = field.Instruction.LastIndexOf('"');

            if (start < 0 || end <= start)
            {
                continue;
            }

            var key = field.Instruction[(start + 1)..end].Replace("\\:", "\u0001", StringComparison.Ordinal).Trim();

            if (!entries.TryGetValue(key, out var list))
            {
                entries[key] = list = [];
            }

            list.Add(field);
        }

        return entries;
    }

    private static List<Paragraph> BuildParagraphs(string instruction, SortedDictionary<string, List<WordField>> entries, Dictionary<string, List<string>> pages)
    {
        var paragraphs = new List<Paragraph>();
        string previousTerm = null;

        foreach (var (key, markers) in entries)
        {
            var parts = key.Split(':', 2);
            var term = parts[0].Replace("\u0001", ":", StringComparison.Ordinal);
            var subentry = parts.Length > 1 ? parts[1].Replace("\u0001", ":", StringComparison.Ordinal) : null;
            var numbers = pages is not null && pages.TryGetValue(key, out var found) ? string.Join(", ", found) : string.Empty;

            if (subentry is not null && !string.Equals(previousTerm, term, StringComparison.OrdinalIgnoreCase))
            {
                paragraphs.Add(Entry(term, string.Empty, 0));
            }

            paragraphs.Add(subentry is null ? Entry(term, numbers, 0) : Entry(subentry, numbers, 1));
            previousTerm = term;
        }

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(new Paragraph(new Run(new Text("No index entries found. Mark entries with add_word_index."))));
        }

        var runs = WordFieldWriter.CreateRuns(instruction, string.Empty);
        var properties = paragraphs[0].ParagraphProperties is null ? 0 : 1;

        paragraphs[0].InsertAt(runs[2], properties);
        paragraphs[0].InsertAt(runs[1], properties);
        paragraphs[0].InsertAt(runs[0], properties);
        paragraphs.Add(new Paragraph(runs[4]));

        return paragraphs;
    }

    private static Paragraph Entry(string text, string pages, int level)
    {
        var paragraph = new Paragraph(new ParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = "0" },
            Indentation = new Indentation { Left = (level * 360 + 360).ToString(CultureInfo.InvariantCulture), Hanging = "360" },
        });

        paragraph.Append(WordInlineWriter.CreateRun(text + (string.IsNullOrEmpty(pages) ? string.Empty : ", " + pages)));

        return paragraph;
    }

    private static string Escape(string text)
    {
        return (text ?? string.Empty).Trim().Replace("\"", "'", StringComparison.Ordinal).Replace(":", "\\:", StringComparison.Ordinal);
    }
}
