using System.Globalization;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Rendering;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Word.Fields;

/// <summary>
/// Writes and refreshes tables of contents: a TOC field inside a "Table of Contents" content control, as Word
/// writes it, with one hyperlinked entry per heading and the heading's page number.
/// </summary>
/// <remarks>
/// The entries are written out, not left for Word to generate, so the table reads correctly in a preview, in
/// another word processor, and in a PDF made from this host's layout. Only tables built from heading levels are
/// rebuilt; a table of figures, or one built from TC entries or custom styles, keeps the entries Word last wrote,
/// and the document then asks Word to update its fields when it opens.
/// </remarks>
internal static partial class WordTableOfContents
{
    /// <summary>
    /// The gallery name of a table of contents content control.
    /// </summary>
    public const string Gallery = "Table of Contents";

    /// <summary>
    /// Builds a table of contents for the document's headings.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="writer">The block writer, for styles and the text width.</param>
    /// <param name="title">The heading over the table, or <see langword="null"/> for none.</param>
    /// <param name="fromLevel">The highest heading level listed.</param>
    /// <param name="toLevel">The lowest heading level listed.</param>
    /// <param name="pageNumbers">Whether entries show page numbers.</param>
    /// <returns>The content control holding the table.</returns>
    public static SdtBlock Create(WordPackage package, WordBlockWriter writer, string title, int fromLevel, int toLevel, bool pageNumbers)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(writer);

        var instruction = string.Create(CultureInfo.InvariantCulture, $"TOC \\o \"{fromLevel}-{toLevel}\" \\h \\z \\u") + (pageNumbers ? string.Empty : " \\n");
        var content = new SdtContentBlock();

        if (!string.IsNullOrWhiteSpace(title))
        {
            content.Append(writer.Paragraph(title.Trim(), writer.Style(WordStyleSheet.TocHeading)));
        }

        foreach (var paragraph in BuildEntries(package, writer, instruction, exclude: null))
        {
            content.Append(paragraph);
        }

        return new SdtBlock(
            new SdtProperties(new SdtContentDocPartObject(new DocPartGallery { Val = Gallery }, new DocPartUnique())),
            content);
    }

    /// <summary>
    /// Rebuilds every heading-based table of contents in a document from its current headings, and fills in its
    /// page numbers and those of every page reference from this host's layout. Other tables built by a TOC field
    /// — a table of figures, one built from TC entries or from custom styles — keep the results Word last wrote.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="services">The request services, for the layout limits, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when every table of contents and page reference was brought up to date;
    /// <see langword="false"/> when one was left for Word to update, because it is not built from headings or its
    /// page is past the end of this host's layout.
    /// </returns>
    public static bool RefreshAll(WordPackage package, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(package);

        var rebuilt = false;
        var complete = true;
        var kept = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);

        foreach (var field in WordFieldScanner.Scan(package.Body).Where(field => field.Type == "TOC").ToList())
        {
            if (IsHeadingTable(field.Instruction) && Rebuild(package, field))
            {
                rebuilt = true;

                continue;
            }

            // A table of figures or a table built from TC entries or custom styles keeps its entries, and its page
            // numbers are Word's to update with the rest of it.
            kept.UnionWith(ParagraphsOf(field) ?? []);
            complete = false;
        }

        var pageReferences = WordFieldScanner.Scan(package.Body)
            .Where(field => field.Type == "PAGEREF" && (field.Paragraph is null || !kept.Contains(field.Paragraph)))
            .ToList();

        if (!rebuilt && pageReferences.Count == 0)
        {
            return complete;
        }

        var preview = services?.GetService<IOptions<WordPreviewOptions>>()?.Value ?? new WordPreviewOptions();
        var layout = WordLayoutEngine.Layout(package, new WordLayoutOptions
        {
            MaxPages = Math.Max(1, preview.MaxLayoutPages),
            IncludePictures = false,
        });

        var targets = package.Body.Descendants<BookmarkStart>()
            .Where(start => start.Name?.Value is not null)
            .GroupBy(start => start.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (OpenXmlElement)group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var field in pageReferences)
        {
            var name = WordFieldScanner.ArgumentOf(field.Instruction);

            // \p writes "on page 5" or "above" rather than the number, and a \* or \# format rewrites it; Word's
            // result is kept, and Word asked to update it.
            if (WordFieldScanner.HasSwitch(field.Instruction, 'p') || WordFieldScanner.HasResultFormat(field.Instruction))
            {
                complete = false;

                continue;
            }

            if (targets.TryGetValue(name, out var target))
            {
                var page = layout.DisplayNumberOf(target.Parent is Paragraph ? target.Parent : target.NextSibling() ?? target);

                if (!string.IsNullOrEmpty(page))
                {
                    WordFieldScanner.SetResult(field, page);

                    continue;
                }
                else if (layout.Truncated && string.Concat(field.ResultRuns.Select(run => run.InnerText)) == "0")
                {
                    // A heading past the pages the layout reached has no page number to give; the entry shows
                    // none rather than the placeholder 0, and Word fills it in when it updates the fields.
                    WordFieldScanner.SetResult(field, string.Empty);
                }
            }

            complete = false;
        }

        return complete;
    }

    /// <summary>
    /// Returns whether a TOC field lists headings — by heading level (<c>\o</c>) or outline level (<c>\u</c>) — so
    /// it can be rebuilt from the document's headings. A table of figures (<c>\c</c>, <c>\a</c>), one built from TC
    /// entries (<c>\f</c>, <c>\l</c>), from custom styles (<c>\t</c>), from part of the document (<c>\b</c>) or
    /// with chapter-page numbers (<c>\s</c>) is not.
    /// </summary>
    /// <param name="instruction">The field code.</param>
    /// <returns><see langword="true"/> when the table lists headings only.</returns>
    public static bool IsHeadingTable(string instruction)
    {
        if (WordFieldScanner.TypeOf(instruction) != "TOC")
        {
            return false;
        }

        var switches = WordFieldScanner.Tokenize(instruction)
            .Where(token => token.IsSwitch)
            .Select(token => char.ToLowerInvariant(token.Text[0]))
            .ToHashSet();

        return (switches.Contains('o') || switches.Contains('u')) && !switches.Overlaps(['c', 'a', 'f', 'l', 't', 'b', 's']);
    }

    /// <summary>
    /// Returns whether a TOC field builds a table of figures, tables or equations from captions (<c>\c</c>) or
    /// from their text without the label (<c>\a</c>), rather than a table of contents.
    /// </summary>
    /// <param name="instruction">The field code.</param>
    /// <returns><see langword="true"/> for a table of figures.</returns>
    public static bool IsTableOfFigures(string instruction)
    {
        return WordFieldScanner.HasSwitch(instruction, 'c') || WordFieldScanner.HasSwitch(instruction, 'a');
    }

    /// <summary>
    /// Reads the heading levels a TOC field lists: those of its <c>\o</c> switch, every level when it lists
    /// outline levels (<c>\u</c>) alone, and 1 to 3 otherwise.
    /// </summary>
    /// <param name="instruction">The field code.</param>
    /// <returns>The highest and lowest levels.</returns>
    public static (int From, int To) ReadLevels(string instruction)
    {
        if (WordFieldScanner.SwitchArguments(instruction, 'o').FirstOrDefault() is { } levels && TryReadRange(levels, out var range))
        {
            return range;
        }

        return WordFieldScanner.HasSwitch(instruction, 'u') && !WordFieldScanner.HasSwitch(instruction, 'o') ? (1, 9) : (1, 3);
    }

    private static bool TryReadRange(string text, out (int From, int To) range)
    {
        range = default;

        var match = RangePattern().Match(text ?? string.Empty);

        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) ||
            !int.TryParse(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var to))
        {
            return false;
        }

        range = (Math.Clamp(Math.Min(from, to), 1, 9), Math.Clamp(Math.Max(from, to), 1, 9));

        return true;
    }

    /// <summary>
    /// Returns the paragraphs a field spans, from the one it begins in to the one it ends in.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The paragraphs, or <see langword="null"/> when the field does not begin and end among siblings.</returns>
    internal static List<Paragraph> ParagraphsOf(WordField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var first = field.BeginRun?.Ancestors<Paragraph>().FirstOrDefault() ?? field.SimpleField?.Ancestors<Paragraph>().FirstOrDefault();
        var last = field.EndRun?.Ancestors<Paragraph>().FirstOrDefault() ?? first;

        if (first?.Parent is null || last?.Parent is null || !ReferenceEquals(first.Parent, last.Parent))
        {
            return null;
        }

        var paragraphs = new List<Paragraph>();

        for (var current = (OpenXmlElement)first; current is not null; current = current.NextSibling())
        {
            if (current is Paragraph paragraph)
            {
                paragraphs.Add(paragraph);
            }

            if (ReferenceEquals(current, last))
            {
                return paragraphs;
            }
        }

        return null;
    }

    private static bool Rebuild(WordPackage package, WordField field)
    {
        var paragraphs = ParagraphsOf(field);

        if (paragraphs is null || field.BeginRun is null)
        {
            return false;
        }

        var first = paragraphs[0];
        var last = paragraphs[^1];

        var section = WordSections.SectionOf(package, first);
        var writer = new WordBlockWriter(package.MainPart, WordDesignReader.Infer(package.MainPart), WordSections.TextWidthTwips(section));

        // A paragraph that ends a section keeps its section properties on the last new paragraph.
        var sectionProperties = last.ParagraphProperties?.SectionProperties;

        // The table's own paragraphs — and, in a content control, its title — are not entries of it. A table that
        // sits directly in the body must not exclude the body, or every heading would be left out.
        var exclude = new HashSet<OpenXmlElement>(paragraphs, ReferenceEqualityComparer.Instance);

        if (first.Parent is SdtContentBlock content)
        {
            exclude.Add(content);
        }

        var entries = BuildEntries(package, writer, field.Instruction, exclude);

        foreach (var entry in entries)
        {
            package.Ids.Assign(entry);
            first.InsertBeforeSelf(entry);
        }

        if (sectionProperties is not null)
        {
            entries[^1].ParagraphProperties ??= new ParagraphProperties();
            entries[^1].ParagraphProperties.SectionProperties = (SectionProperties)sectionProperties.CloneNode(true);
        }

        foreach (var paragraph in paragraphs)
        {
            paragraph.Remove();
        }

        return true;
    }

    private static List<Paragraph> BuildEntries(WordPackage package, WordBlockWriter writer, string instruction, HashSet<OpenXmlElement> exclude)
    {
        var (from, to) = ReadLevels(instruction);
        var hyperlinks = WordFieldScanner.HasSwitch(instruction, 'h');
        var outlineLevels = WordFieldScanner.HasSwitch(instruction, 'u');
        var styles = new WordStyleIndex(package.MainPart);
        var bookmarks = WordBookmarks.For(package);
        var tabPosition = writer.TextWidthTwips;
        var entries = new List<Paragraph>();

        // \n leaves out the page numbers of every level, or of the levels it names, such as \n "1-1".
        (int From, int To)? withoutPages = null;

        if (WordFieldScanner.HasSwitch(instruction, 'n'))
        {
            withoutPages = WordFieldScanner.SwitchArguments(instruction, 'n').FirstOrDefault() is { } levels && TryReadRange(levels, out var range) ? range : (1, 9);
        }

        var headings = WordBlockReader.Read(package)
            .Where(block => block.Kind == WordBlockKind.Heading && block.Level >= from && block.Level <= to && !string.IsNullOrWhiteSpace(block.Text))
            .Where(block => exclude is null || (!exclude.Contains(block.Element) && !block.Element.Ancestors().Any(exclude.Contains)))
            .Where(block => outlineLevels || block.Element is not Paragraph { ParagraphProperties.OutlineLevel: not null } paragraph || HasHeadingStyle(styles, paragraph))
            .ToList();

        foreach (var heading in headings)
        {
            var pageNumbers = withoutPages is not { } omitted || heading.Level < omitted.From || heading.Level > omitted.To;
            var paragraph = (Paragraph)heading.Element;
            var bookmark = WordBookmarks.FindOn(paragraph, "_Toc");

            if (bookmark is null)
            {
                bookmark = bookmarks.HiddenName("_Toc");
                bookmarks.Wrap(paragraph, bookmark);
            }

            var entry = new Paragraph(new ParagraphProperties
            {
                ParagraphStyleId = new ParagraphStyleId { Val = writer.Style(WordStyleSheet.Toc(heading.Level)) },
                Tabs = new Tabs(new TabStop { Val = TabStopValues.Right, Leader = TabStopLeaderCharValues.Dot, Position = tabPosition }),
            });

            OpenXmlCompositeElement target = entry;

            if (hyperlinks)
            {
                var link = new Hyperlink { Anchor = bookmark, History = true };

                entry.Append(link);
                target = link;
            }

            target.Append(WordInlineWriter.CreateRun(WordText.Clip(heading.Text, 200)));

            if (pageNumbers)
            {
                target.Append(new Run(new TabChar()));

                foreach (var run in WordFieldWriter.CreateRuns("PAGEREF " + bookmark + " \\h", "0"))
                {
                    target.Append(run);
                }
            }

            entries.Add(entry);
        }

        if (entries.Count == 0)
        {
            entries.Add(new Paragraph(new Run(new Text("No table of contents entries found. Add headings, then refresh the table."))));
        }

        // The TOC field starts in the first entry and ends in a paragraph of its own after the last.
        var begin = WordFieldWriter.CreateRuns(instruction, string.Empty);

        entries[0].InsertAt(begin[2], entries[0].ParagraphProperties is null ? 0 : 1);
        entries[0].InsertAt(begin[1], entries[0].ParagraphProperties is null ? 0 : 1);
        entries[0].InsertAt(begin[0], entries[0].ParagraphProperties is null ? 0 : 1);
        entries.Add(new Paragraph(begin[4]));

        return entries;
    }

    private static bool HasHeadingStyle(WordStyleIndex styles, Paragraph paragraph)
    {
        // Without \u a table lists paragraphs by their heading style; an outline level set on the paragraph alone
        // does not make it an entry.
        foreach (var style in styles.Chain(styles.StyleOf(paragraph)))
        {
            if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is { } level)
            {
                return level is >= 0 and < 9;
            }

            if (style.StyleName?.Val?.Value is { } name && name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex("^\\s*(\\d)\\s*(?:-\\s*(\\d))?\\s*$")]
    private static partial Regex RangePattern();
}
