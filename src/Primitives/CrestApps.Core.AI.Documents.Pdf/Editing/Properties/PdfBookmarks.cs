using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Objects;
using UglyToad.PdfPig.Outline;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Reads a PDF's bookmarks (its outline) and writes new ones.
/// </summary>
internal static class PdfBookmarks
{
    /// <summary>
    /// Removes a document's outline, so a fresh one is started when bookmarks are written afterwards.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <returns><see langword="true"/> when the document had an outline.</returns>
    public static bool Clear(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var catalog = document.Catalog;
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

        var root = document.Catalog.Elements.GetDictionary("/Outlines");

        if (root is null)
        {
            root = new PdfDictionary(document);
            root.Elements.SetName("/Type", "/Outlines");
            document.Catalog.Elements.SetReference("/Outlines", root);
        }

        var count = Write(document, root, nodes, 0);
        var visible = Recount(root, 0);

        if (visible > 0)
        {
            root.Elements.SetInteger("/Count", visible);
        }
        else
        {
            root.Elements.Remove("/Count");
        }

        document.Catalog.Elements.SetName("/PageMode", "/UseOutlines");

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

    private static int Write(PdfDocument document, PdfDictionary parent, IEnumerable<PdfBookmarkNode> nodes, int depth)
    {
        var count = 0;

        foreach (var node in nodes)
        {
            var page = document.Pages[node.Page - 1];
            var item = new PdfDictionary(document);
            var reference = document.AddObject(item);
            var box = page.CropBox;

            item.Elements.SetString("/Title", node.Title ?? string.Empty);
            item.Elements["/Parent"] = document.AddObject(parent);
            item.Elements["/Dest"] = new PdfArray(document, document.AddObject(page), new PdfName("/XYZ"), new PdfReal(box.X1), new PdfReal(node.Top ?? box.Y2), PdfNull.Value);

            // The style flags: 1 for italic, 2 for bold.
            var flags = (node.Italic ? 1 : 0) | (node.Bold ? 2 : 0);

            if (flags != 0)
            {
                item.Elements.SetInteger("/F", flags);
            }

            if (node.Color is { } color)
            {
                item.Elements["/C"] = new PdfArray(document, new PdfReal(color.Red / 255d), new PdfReal(color.Green / 255d), new PdfReal(color.Blue / 255d));
            }

            // The sign of the count says whether the bookmark opens with its children shown; Recount sets its size.
            item.Elements.SetInteger("/Count", node.Open ? 1 : -1);

            if (parent.Elements.GetDictionary("/Last") is { } last)
            {
                last.Elements["/Next"] = reference;
                item.Elements["/Prev"] = document.AddObject(last);
            }
            else
            {
                parent.Elements["/First"] = reference;
            }

            parent.Elements["/Last"] = reference;
            count++;

            if (node.Children.Count > 0 && depth < 32)
            {
                count += Write(document, item, node.Children, depth + 1);
            }
        }

        return count;
    }

    /// <summary>
    /// Sets the count of every bookmark under an outline node: the number of bookmarks it shows when open,
    /// negated when it starts closed, and none when it has no children.
    /// </summary>
    /// <param name="node">The outline node.</param>
    /// <param name="depth">How deep the node is.</param>
    /// <returns>The number of bookmarks under the node that show when it is open.</returns>
    private static int Recount(PdfDictionary node, int depth)
    {
        var visible = 0;
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        for (var item = node.Elements.GetDictionary("/First"); item is not null && depth < 32 && visited.Add(item); item = item.Elements.GetDictionary("/Next"))
        {
            var open = item.Elements.GetInteger("/Count") > 0;
            var shown = Recount(item, depth + 1);

            if (shown == 0)
            {
                item.Elements.Remove("/Count");
            }
            else
            {
                item.Elements.SetInteger("/Count", open ? shown : -shown);
            }

            visible += 1 + (open ? shown : 0);
        }

        return visible;
    }
}
