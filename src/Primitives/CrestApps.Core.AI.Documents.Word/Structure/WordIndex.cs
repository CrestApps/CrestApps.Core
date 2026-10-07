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
    /// <returns>
    /// <see langword="true"/> when every index was rebuilt with all its page numbers; <see langword="false"/> when an
    /// entry's page is past the end of this host's layout, so Word has to fill it in.
    /// </returns>
    public static bool RefreshAll(WordPackage package, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(package);

        var indexes = WordFieldScanner.Scan(package.Body).Where(field => field.Type == "INDEX").ToList();

        if (indexes.Count == 0)
        {
            return true;
        }

        var preview = services?.GetService<IOptions<WordPreviewOptions>>()?.Value ?? new WordPreviewOptions();
        var layout = WordLayoutEngine.Layout(package, new WordLayoutOptions { MaxPages = Math.Max(1, preview.MaxLayoutPages), IncludePictures = false });
        var entries = Entries(package);
        var complete = true;
        var pages = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (key, markers) in entries)
        {
            var numbers = markers.Select(field => layout.DisplayNumberOf(field.Paragraph)).ToList();

            complete &= numbers.All(page => page.Length > 0);
            pages[key] = [.. numbers.Where(page => page.Length > 0).Distinct()];
        }

        foreach (var index in indexes)
        {
            var old = WordTableOfContents.ParagraphsOf(index);

            if (old is null || index.BeginRun is null)
            {
                complete = false;

                continue;
            }

            var built = BuildParagraphs(index.Instruction, entries, pages);

            foreach (var paragraph in built)
            {
                package.Ids.Assign(paragraph);
                old[0].InsertBeforeSelf(paragraph);
            }

            // Word ends an index set in columns with a continuous section break on its last paragraph; the new
            // last paragraph carries it, so the columns and the section survive the rebuild.
            if (old[^1].ParagraphProperties?.SectionProperties is { } sectionProperties)
            {
                built[^1].ParagraphProperties ??= new ParagraphProperties();
                built[^1].ParagraphProperties.SectionProperties = (SectionProperties)sectionProperties.CloneNode(true);
            }

            foreach (var paragraph in old)
            {
                paragraph.Remove();
            }
        }

        return complete;
    }

    /// <summary>
    /// Reads the index entries a document marks, by entry text, in an order that does not depend on the culture
    /// the host runs under.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>The entry markers by "term" or "term:subentry".</returns>
    public static SortedDictionary<string, List<WordField>> Entries(WordPackage package)
    {
        var entries = new SortedDictionary<string, List<WordField>>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var field in WordFieldScanner.Scan(package.Body).Where(field => field.Type == "XE"))
        {
            // The entry is the first argument; switches such as \t "See also" or \b come after it.
            var tokens = WordFieldScanner.Tokenize(field.Instruction.Replace("\\:", "\u0001", StringComparison.Ordinal));

            if (tokens.Count < 2 || tokens[1].IsSwitch)
            {
                continue;
            }

            var key = tokens[1].Text.Trim();

            if (key.Length == 0)
            {
                continue;
            }

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
