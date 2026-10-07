using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Records changes as tracked revisions: who made them, when, and with an id no other revision uses.
/// </summary>
internal sealed class WordRevisions
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private int _nextId;

    private WordRevisions(int nextId, string author, DateTime date)
    {
        _nextId = nextId;
        Author = string.IsNullOrWhiteSpace(author) ? "AI Assistant" : author;
        Date = date;
    }

    /// <summary>
    /// Gets the author revisions are signed with.
    /// </summary>
    public string Author { get; }

    /// <summary>
    /// Gets the time revisions are recorded at.
    /// </summary>
    public DateTime Date { get; }

    /// <summary>
    /// Reads the revision ids a document uses.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="author">The author new revisions are signed with.</param>
    /// <param name="date">The time new revisions are recorded at.</param>
    /// <returns>The revision source.</returns>
    public static WordRevisions For(WordPackage package, string author, DateTime date)
    {
        ArgumentNullException.ThrowIfNull(package);

        // New ids continue past every id the document uses — revisions, comments, bookmarks, in the body, the
        // headers and footers, the notes and the comments — so none can collide.
        var largest = LargestId(package, _ => true);

        return new WordRevisions(Math.Max(0, largest) + 1, author, date);
    }

    /// <summary>
    /// Returns the id a new comment takes: one past every comment and tracked change of the document, so a
    /// comment never shares its id with a revision.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>The id.</returns>
    public static int NextCommentId(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        // Bookmarks and notes number themselves separately; only comments and revisions are kept apart.
        return LargestId(package, element => element is not (BookmarkStart or BookmarkEnd or Footnote or Endnote or FootnoteReference or EndnoteReference)) + 1;
    }

    /// <summary>
    /// Lists every tracked change of a document: in the body, the headers and footers, the footnotes, the
    /// endnotes and the comments.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>The changes, in document order, part by part.</returns>
    public static List<WordTrackedChange> Changes(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var changes = new List<WordTrackedChange>();

        foreach (var (root, part) in Parts(package))
        {
            foreach (var element in root.Descendants())
            {
                if (IsChange(element))
                {
                    changes.Add(new WordTrackedChange(element, root, part));
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// Returns whether an element records a tracked change.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> for an insertion, deletion, move or formatting change of text, a paragraph
    /// mark, a table, a row, a cell or a section, and for numbering added to a paragraph.</returns>
    public static bool IsChange(OpenXmlElement element)
    {
        return element switch
        {
            InsertedRun or DeletedRun or MoveFromRun or MoveToRun => true,
            RunPropertiesChange or ParagraphPropertiesChange or ParagraphMarkRunPropertiesChange => true,
            TablePropertiesChange or TablePropertyExceptionsChange or TableGridChange or TableRowPropertiesChange or TableCellPropertiesChange => true,
            CellInsertion or CellDeletion or CellMerge or SectionPropertiesChange => true,
            Inserted => element.Parent is ParagraphMarkRunProperties or TableRowProperties or NumberingProperties,
            Deleted => element.Parent is ParagraphMarkRunProperties or TableRowProperties,
            MoveFrom or MoveTo => element.Parent is ParagraphMarkRunProperties,
            _ => !IsSupported(element),
        };
    }

    /// <summary>
    /// Returns whether a tracked change can be accepted or rejected here. Numbering changes, equation changes
    /// and changes to custom XML markup cannot.
    /// </summary>
    /// <param name="element">The change.</param>
    /// <returns><see langword="false"/> for a change only Word can apply or undo.</returns>
    public static bool IsSupported(OpenXmlElement element)
    {
        return element is not (NumberingChange or InsertedMathControl or DeletedMathControl or MoveFromMathControl or MoveToMathControl or
            CustomXmlInsRangeStart or CustomXmlDelRangeStart or CustomXmlMoveFromRangeStart or CustomXmlMoveToRangeStart);
    }

    private static IEnumerable<(OpenXmlElement Root, string Part)> Parts(WordPackage package)
    {
        var mainPart = package.MainPart;

        yield return (package.Body, "body");

        foreach (var header in mainPart.HeaderParts.Where(part => part.Header is not null))
        {
            yield return (header.Header, "header");
        }

        foreach (var footer in mainPart.FooterParts.Where(part => part.Footer is not null))
        {
            yield return (footer.Footer, "footer");
        }

        if (mainPart.FootnotesPart?.Footnotes is { } footnotes)
        {
            yield return (footnotes, "footnote");
        }

        if (mainPart.EndnotesPart?.Endnotes is { } endnotes)
        {
            yield return (endnotes, "endnote");
        }

        if (mainPart.WordprocessingCommentsPart?.Comments is { } comments)
        {
            yield return (comments, "comment");
        }
    }

    private static int LargestId(WordPackage package, Func<OpenXmlElement, bool> include)
    {
        var largest = -1;

        foreach (var (root, _) in Parts(package))
        {
            foreach (var element in root.Descendants())
            {
                if (!element.HasAttributes || !include(element))
                {
                    continue;
                }

                var attribute = element.GetAttributes().FirstOrDefault(item => item.LocalName == "id" && item.NamespaceUri == WordNamespace);

                if (attribute.Value is not null && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > largest)
                {
                    largest = id;
                }
            }
        }

        return largest;
    }

    /// <summary>
    /// Returns whether tracked changes are switched on for a document.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns><see langword="true"/> when Word records edits as revisions.</returns>
    public static bool IsTracking(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var setting = package.MainPart.DocumentSettingsPart?.Settings?.GetFirstChild<TrackRevisions>();

        return setting is not null && (setting.Val?.Value ?? true);
    }

    /// <summary>
    /// Returns the next revision id.
    /// </summary>
    /// <returns>The id, as text.</returns>
    public string NextId()
    {
        return (_nextId++).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Wraps a run as a tracked insertion.
    /// </summary>
    /// <param name="run">The run, not yet in a document.</param>
    /// <returns>The insertion.</returns>
    public InsertedRun Insert(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new InsertedRun(run) { Id = NextId(), Author = Author, Date = Date };
    }

    /// <summary>
    /// Turns a run in a document into a tracked deletion, in place: its text becomes deleted text and the run
    /// is wrapped in a deletion.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The deletion.</returns>
    public DeletedRun Delete(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        foreach (var text in run.Elements<Text>().ToList())
        {
            text.InsertAfterSelf(new DeletedText(text.Text) { Space = SpaceProcessingModeValues.Preserve });
            text.Remove();
        }

        foreach (var code in run.Elements<FieldCode>().ToList())
        {
            code.InsertAfterSelf(new DeletedFieldCode(code.Text) { Space = SpaceProcessingModeValues.Preserve });
            code.Remove();
        }

        var deletion = new DeletedRun { Id = NextId(), Author = Author, Date = Date };

        run.InsertBeforeSelf(deletion);
        run.Remove();
        deletion.Append(run);

        return deletion;
    }

    /// <summary>
    /// Marks a whole paragraph as inserted: its runs and its paragraph mark.
    /// </summary>
    /// <param name="paragraph">The paragraph, not yet in a document.</param>
    public void MarkInserted(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        foreach (var run in paragraph.Descendants<Run>().Where(run => run.Parent is not InsertedRun).ToList())
        {
            var insertion = new InsertedRun { Id = NextId(), Author = Author, Date = Date };

            run.InsertBeforeSelf(insertion);
            run.Remove();
            insertion.Append(run);
        }

        var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();

        properties.ParagraphMarkRunProperties ??= new ParagraphMarkRunProperties();
        properties.ParagraphMarkRunProperties.PrependChild(new Inserted { Id = NextId(), Author = Author, Date = Date });
    }

    /// <summary>
    /// Marks a whole paragraph in a document as deleted: its runs and its paragraph mark.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    public void MarkDeleted(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        foreach (var run in paragraph.Descendants<Run>().Where(run => run.Parent is not DeletedRun && run.Ancestors<DeletedRun>().FirstOrDefault() is null).ToList())
        {
            Delete(run);
        }

        var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();

        properties.ParagraphMarkRunProperties ??= new ParagraphMarkRunProperties();
        properties.ParagraphMarkRunProperties.PrependChild(new Deleted { Id = NextId(), Author = Author, Date = Date });
    }
}

/// <summary>
/// A tracked change and the part of the document it is in.
/// </summary>
/// <param name="Element">The element recording the change.</param>
/// <param name="Root">The root of the part the change is in: the body, a header, a footer, the notes or the comments.</param>
/// <param name="Part">The kind of part: <c>body</c>, <c>header</c>, <c>footer</c>, <c>footnote</c>, <c>endnote</c> or <c>comment</c>.</param>
internal sealed record WordTrackedChange(OpenXmlElement Element, OpenXmlElement Root, string Part);
