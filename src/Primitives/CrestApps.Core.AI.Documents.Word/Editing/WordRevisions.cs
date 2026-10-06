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

        // New ids continue past every id the body uses — revisions, comments, bookmarks — so none can collide.
        var largest = package.MainPart.Document.Descendants()
            .Select(element => element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "id" && attribute.NamespaceUri == "http://schemas.openxmlformats.org/wordprocessingml/2006/main"))
            .Where(attribute => attribute.Value is not null)
            .Select(attribute => int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max();

        return new WordRevisions(largest + 1, author, date);
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
