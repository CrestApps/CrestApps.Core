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
/// another word processor, and in a PDF made from this host's layout. Word is also asked to update fields when
/// it opens the file, so its own pagination has the last word.
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

        foreach (var paragraph in BuildEntries(package, writer, instruction, content))
        {
            content.Append(paragraph);
        }

        return new SdtBlock(
            new SdtProperties(new SdtContentDocPartObject(new DocPartGallery { Val = Gallery }, new DocPartUnique())),
            content);
    }

    /// <summary>
    /// Rebuilds every table of contents in a document from its current headings, and fills in its page numbers
    /// and those of every page reference from this host's layout.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="services">The request services, for the layout limits, or <see langword="null"/>.</param>
    public static void RefreshAll(WordPackage package, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(package);

        var rebuilt = false;

        foreach (var field in WordFieldScanner.Scan(package.Body).Where(field => field.Type == "TOC").ToList())
        {
            Rebuild(package, field);
            rebuilt = true;
        }

        var pageReferences = WordFieldScanner.Scan(package.Body).Where(field => field.Type == "PAGEREF").ToList();

        if (!rebuilt && pageReferences.Count == 0)
        {
            return;
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

            if (targets.TryGetValue(name, out var target))
            {
                var page = layout.DisplayNumberOf(target.Parent is Paragraph ? target.Parent : target.NextSibling() ?? target);

                if (!string.IsNullOrEmpty(page))
                {
                    WordFieldScanner.SetResult(field, page);
                }
                else if (layout.Truncated && string.Concat(field.ResultRuns.Select(run => run.InnerText)) == "0")
                {
                    // A heading past the pages the layout reached has no page number to give; the entry shows
                    // none rather than the placeholder 0, and Word fills it in when it updates the fields.
                    WordFieldScanner.SetResult(field, string.Empty);
                }
            }
        }
    }

    /// <summary>
    /// Reads the heading levels a TOC field lists.
    /// </summary>
    /// <param name="instruction">The field code.</param>
    /// <returns>The highest and lowest levels.</returns>
    public static (int From, int To) ReadLevels(string instruction)
    {
        var match = LevelsPattern().Match(instruction ?? string.Empty);

        if (match.Success &&
            int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) &&
            int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var to))
        {
            return (Math.Clamp(Math.Min(from, to), 1, 9), Math.Clamp(Math.Max(from, to), 1, 9));
        }

        return (1, 3);
    }

    private static void Rebuild(WordPackage package, WordField field)
    {
        var first = field.BeginRun?.Ancestors<Paragraph>().FirstOrDefault();
        var last = field.EndRun?.Ancestors<Paragraph>().FirstOrDefault();

        if (first?.Parent is null || last?.Parent is null || !ReferenceEquals(first.Parent, last.Parent))
        {
            return;
        }

        var container = first.Parent;
        var paragraphs = new List<Paragraph>();

        for (var current = (OpenXmlElement)first; current is not null; current = current.NextSibling())
        {
            if (current is Paragraph paragraph)
            {
                paragraphs.Add(paragraph);
            }

            if (ReferenceEquals(current, last))
            {
                break;
            }
        }

        var section = WordSections.SectionOf(package, first);
        var writer = new WordBlockWriter(package.MainPart, WordDesignReader.Infer(package.MainPart), WordSections.TextWidthTwips(section));

        // A paragraph that ends a section keeps its section properties on the last new paragraph.
        var sectionProperties = last.ParagraphProperties?.SectionProperties;

        var entries = BuildEntries(package, writer, field.Instruction, container);

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
    }

    private static List<Paragraph> BuildEntries(WordPackage package, WordBlockWriter writer, string instruction, OpenXmlElement exclude)
    {
        var (from, to) = ReadLevels(instruction);
        var hyperlinks = instruction.Contains("\\h", StringComparison.OrdinalIgnoreCase);
        var pageNumbers = !instruction.Contains("\\n", StringComparison.OrdinalIgnoreCase);
        var bookmarks = WordBookmarks.For(package);
        var tabPosition = writer.TextWidthTwips;
        var entries = new List<Paragraph>();

        var headings = WordBlockReader.Read(package)
            .Where(block => block.Kind == WordBlockKind.Heading && block.Level >= from && block.Level <= to && !string.IsNullOrWhiteSpace(block.Text))
            .Where(block => exclude is null || !block.Element.Ancestors().Contains(exclude))
            .ToList();

        foreach (var heading in headings)
        {
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

    [GeneratedRegex("\\\\o\\s+\"?(\\d)\\s*-\\s*(\\d)\"?", RegexOptions.IgnoreCase)]
    private static partial Regex LevelsPattern();
}
