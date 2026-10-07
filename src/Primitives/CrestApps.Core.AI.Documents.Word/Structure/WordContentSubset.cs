using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2013.Word;
using DocumentFormat.OpenXml.Office2019.Word.Cid;
using DocumentFormat.OpenXml.Office2021.Word.CommentsExt;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Structure;

/// <summary>
/// Cuts a document down to chosen content: the elements listed and the headings with everything under them.
/// Everything else in the body goes; the page layout, styles, headers and footers stay.
/// </summary>
internal static class WordContentSubset
{
    /// <summary>
    /// Keeps only the chosen top-level blocks of a document.
    /// </summary>
    /// <param name="package">The document, changed in place.</param>
    /// <param name="ids">Element ids; an element inside a table keeps the whole table.</param>
    /// <param name="headings">Heading ids; each keeps the heading and everything under it, up to the next heading of the same or a higher level.</param>
    /// <returns>The number of top-level blocks kept.</returns>
    public static int Keep(WordPackage package, IReadOnlyList<string> ids, IReadOnlyList<string> headings)
    {
        ArgumentNullException.ThrowIfNull(package);

        var body = package.Body;
        var selected = new HashSet<OpenXmlElement>();

        foreach (var id in ids ?? [])
        {
            var element = WordBlockLocator.Require(package, id);

            selected.Add(WordSections.TopLevel(body, element) ?? element);
        }

        foreach (var heading in headings ?? [])
        {
            selected.UnionWith(WordBlockSelection.HeadingWithContent(package, heading));
        }

        selected.RemoveWhere(element => element is SectionProperties || element.Parent != body);

        if (selected.Count == 0)
        {
            throw new WordToolException("Nothing was chosen to keep: pass element 'ids' or 'headings' from get_word_document.");
        }

        // The body is cut section by section, so the kept content keeps the page layout it was laid out in.
        var sections = new List<List<OpenXmlElement>>();
        var current = new List<OpenXmlElement>();

        foreach (var child in body.ChildElements.ToList())
        {
            if (child is SectionProperties)
            {
                continue;
            }

            current.Add(child);

            if (child is Paragraph paragraph && paragraph.ParagraphProperties?.SectionProperties is not null)
            {
                sections.Add(current);
                current = [];
            }
        }

        sections.Add(current);

        var lastKept = sections.FindLastIndex(section => section.Any(selected.Contains));

        for (var index = 0; index < sections.Count; index++)
        {
            var kept = sections[index].Any(selected.Contains);

            foreach (var block in sections[index])
            {
                var sectionBreak = (block as Paragraph)?.ParagraphProperties?.SectionProperties;

                if (sectionBreak is null)
                {
                    if (!selected.Contains(block))
                    {
                        block.Remove();
                    }

                    continue;
                }

                if (!kept)
                {
                    block.Remove();

                    continue;
                }

                if (index == lastKept)
                {
                    // The last section kept ends the document, so its layout becomes the body's.
                    sectionBreak.Remove();
                    WordSections.EnsureBodySection(body).Remove();
                    body.Append(sectionBreak);

                    if (!selected.Contains(block))
                    {
                        block.Remove();
                    }

                    continue;
                }

                if (!selected.Contains(block))
                {
                    // The paragraph only ends a section that keeps content; it stays, empty, to keep the section.
                    foreach (var child in block.ChildElements.Where(child => child is not ParagraphProperties).ToList())
                    {
                        child.Remove();
                    }
                }
            }
        }

        var section = WordSections.EnsureBodySection(body);

        if (section.PreviousSibling() is not Paragraph)
        {
            // A document ends with a paragraph, not a table.
            section.InsertBeforeSelf(new Paragraph());
        }

        RemoveOrphans(package);

        return selected.Count;
    }

    /// <summary>
    /// Removes what the cut left without a partner: the start or end of a bookmark or a comment range whose other
    /// half went, and the comments nothing in the body refers to any more.
    /// </summary>
    /// <param name="package">The document.</param>
    public static void RemoveOrphans(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var body = package.Body;

        RemoveUnpaired<BookmarkStart, BookmarkEnd>(body, start => start.Id?.Value, end => end.Id?.Value);
        RemoveUnpaired<CommentRangeStart, CommentRangeEnd>(body, start => start.Id?.Value, end => end.Id?.Value);

        var comments = package.MainPart.WordprocessingCommentsPart?.Comments;

        if (comments is null)
        {
            return;
        }

        var referenced = body.Descendants<CommentReference>().Select(reference => reference.Id?.Value)
            .Concat(body.Descendants<CommentRangeStart>().Select(start => start.Id?.Value))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var comment in comments.Elements<Comment>().Where(comment => !referenced.Contains(comment.Id?.Value)).ToList())
        {
            comment.Remove();
        }

        // The extra comment parts are keyed by the paragraph id of each comment's last paragraph.
        var paragraphIds = comments.Elements<Comment>()
            .Select(comment => WordParagraphIds.Of(comment.Elements<Paragraph>().LastOrDefault()))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extended = package.MainPart.WordprocessingCommentsExPart?.CommentsEx;

        foreach (var entry in extended?.Elements<CommentEx>().Where(entry => !paragraphIds.Contains(entry.ParaId?.Value ?? string.Empty)).ToList() ?? [])
        {
            entry.Remove();
        }

        var removedDurableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commentIds = package.MainPart.WordprocessingCommentsIdsPart?.CommentsIds;

        foreach (var entry in commentIds?.Elements<CommentId>().Where(entry => !paragraphIds.Contains(entry.ParaId?.Value ?? string.Empty)).ToList() ?? [])
        {
            if (entry.DurableId?.Value is { } durableId)
            {
                removedDurableIds.Add(durableId);
            }

            entry.Remove();
        }

        var extensible = package.MainPart.WordCommentsExtensiblePart?.CommentsExtensible;

        foreach (var entry in extensible?.Elements<CommentExtensible>().Where(entry => removedDurableIds.Contains(entry.DurableId?.Value ?? string.Empty)).ToList() ?? [])
        {
            entry.Remove();
        }
    }

    private static void RemoveUnpaired<TStart, TEnd>(Body body, Func<TStart, string> startId, Func<TEnd, string> endId)
        where TStart : OpenXmlElement
        where TEnd : OpenXmlElement
    {
        var starts = body.Descendants<TStart>().ToList();
        var ends = body.Descendants<TEnd>().ToList();
        var startIds = starts.Select(startId).ToHashSet(StringComparer.Ordinal);
        var endIds = ends.Select(endId).ToHashSet(StringComparer.Ordinal);

        foreach (var start in starts.Where(start => !endIds.Contains(startId(start))))
        {
            start.Remove();
        }

        foreach (var end in ends.Where(end => !startIds.Contains(endId(end))))
        {
            end.Remove();
        }
    }
}
