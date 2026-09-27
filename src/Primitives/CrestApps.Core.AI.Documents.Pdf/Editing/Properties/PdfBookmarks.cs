using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using UglyToad.PdfPig.Outline;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Reads a PDF's bookmarks (its outline) and writes new ones.
/// </summary>
internal static class PdfBookmarks
{
    /// <summary>
    /// Removes a document's outline. Call it before anything reads <see cref="PdfDocument.Outlines"/>, so a
    /// fresh outline is started when one is written afterwards.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <returns><see langword="true"/> when the document had an outline.</returns>
    public static bool Clear(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var catalog = document.Internals.Catalog;
        var had = catalog.Elements.ContainsKey("/Outlines");

        catalog.Elements.Remove("/Outlines");

        if (string.Equals(PdfObjects.GetName(catalog, "/PageMode"), "UseOutlines", StringComparison.Ordinal))
        {
            catalog.Elements.SetName("/PageMode", "/UseNone");
        }

        return had;
    }

    /// <summary>
    /// Writes bookmarks at the end of a document's outline.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <param name="nodes">The bookmarks.</param>
    /// <returns>The number of bookmarks written, nested ones included.</returns>
    public static int Write(PdfDocument document, IEnumerable<PdfBookmarkNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(nodes);

        var count = Write(document, document.Outlines, nodes);

        // The outline's root count is recalculated on save; one read from the file would be stale.
        if (PdfObjects.Resolve(document.Internals.Catalog.Elements["/Outlines"]) is PdfDictionary root)
        {
            root.Elements.Remove("/Count");
        }

        document.PageMode = PdfPageMode.UseOutlines;

        return count;
    }

    /// <summary>
    /// Counts a document's bookmarks, nested ones included.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The count.</returns>
    public static int Count(PigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.TryGetBookmarks(out var bookmarks))
        {
            return 0;
        }

        return bookmarks.GetNodes().Count();
    }

    /// <summary>
    /// Describes a document's bookmarks, one per line, indented by level.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="limit">The most bookmarks described.</param>
    /// <param name="total">The number of bookmarks the document has.</param>
    /// <returns>The description, or <see langword="null"/> when the document has no bookmarks.</returns>
    public static string Describe(PigDocument document, int limit, out int total)
    {
        ArgumentNullException.ThrowIfNull(document);

        total = 0;

        if (!document.TryGetBookmarks(out var bookmarks) || bookmarks.Roots.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        var pending = new Stack<(BookmarkNode Node, int Level)>();

        for (var index = bookmarks.Roots.Count - 1; index >= 0; index--)
        {
            pending.Push((bookmarks.Roots[index], 1));
        }

        while (pending.Count > 0)
        {
            var (node, level) = pending.Pop();

            total++;

            if (total <= limit)
            {
                var target = node switch
                {
                    DocumentBookmarkNode local => string.Create(CultureInfo.InvariantCulture, $"page {local.PageNumber}"),
                    UriBookmarkNode uri => uri.Uri,
                    _ => "no target",
                };

                builder.Append(' ', (level - 1) * 2)
                    .Append("- ")
                    .Append(string.IsNullOrWhiteSpace(node.Title) ? "(untitled)" : node.Title.Trim())
                    .Append(" (")
                    .Append(target)
                    .Append(string.Create(CultureInfo.InvariantCulture, $", level {level})"))
                    .AppendLine();
            }

            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                pending.Push((node.Children[index], level + 1));
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static int Write(PdfDocument document, PdfOutlineCollection target, IEnumerable<PdfBookmarkNode> nodes)
    {
        var count = 0;

        foreach (var node in nodes)
        {
            var style = (node.Bold, node.Italic) switch
            {
                (true, true) => PdfOutlineStyle.BoldItalic,
                (true, false) => PdfOutlineStyle.Bold,
                (false, true) => PdfOutlineStyle.Italic,
                _ => PdfOutlineStyle.Regular,
            };

            var page = document.Pages[node.Page - 1];
            var outline = node.Color is { } color
                ? target.Add(node.Title, page, node.Open, style, color.ToXColor())
                : target.Add(node.Title, page, node.Open, style);

            outline.PageDestinationType = PdfPageDestinationType.Xyz;
            outline.Left = page.EffectiveCropBoxReadOnly.X1;
            outline.Top = node.Top ?? page.EffectiveCropBoxReadOnly.Y2;
            outline.Zoom = null;

            count++;

            if (node.Children.Count > 0)
            {
                count += Write(document, outline.Outlines, node.Children);
            }
        }

        return count;
    }
}
