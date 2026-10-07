using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Reads and rewrites a PDF name tree, such as the one that lists a document's embedded files.
/// </summary>
/// <remarks>
/// A name tree may be one node with a <c>/Names</c> array, or a tree of <c>/Kids</c> whose leaves hold the
/// names. It is read by walking every node, and written back as a single sorted node, which every reader
/// accepts and which leaves no node holding an entry that was removed.
/// </remarks>
internal static class PdfNameTree
{
    /// <summary>
    /// Reads every entry of a name tree.
    /// </summary>
    /// <param name="root">The root node.</param>
    /// <returns>The entries, in tree order; each value is left as written (usually a reference).</returns>
    public static List<KeyValuePair<string, PdfItem>> Read(PdfDictionary root)
    {
        var entries = new List<KeyValuePair<string, PdfItem>>();

        if (root is null)
        {
            return entries;
        }

        var seen = new HashSet<PdfDictionary>();
        var pending = new Stack<(PdfDictionary Node, int Depth)>();

        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();

            if (!seen.Add(node) || depth > 32)
            {
                continue;
            }

            var names = PdfObjects.GetArray(node, "/Names");

            if (names is not null)
            {
                for (var index = 0; index + 1 < names.Elements.Count; index += 2)
                {
                    var key = PdfObjects.Resolve(names.Elements[index]) switch
                    {
                        PdfString text => text.Value,
                        PdfName name => name.Value.TrimStart('/'),
                        _ => null,
                    };

                    if (key is not null)
                    {
                        entries.Add(new KeyValuePair<string, PdfItem>(key, names.Elements[index + 1]));
                    }
                }
            }

            var kids = PdfObjects.GetArray(node, "/Kids");

            if (kids is null)
            {
                continue;
            }

            // Pushed in reverse so the kids are read in order.
            for (var index = kids.Elements.Count - 1; index >= 0; index--)
            {
                if (PdfObjects.Resolve(kids.Elements[index]) is PdfDictionary kid)
                {
                    pending.Push((kid, depth + 1));
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Builds a single name tree node holding the given entries, sorted by name.
    /// </summary>
    /// <param name="document">The document the node belongs to.</param>
    /// <param name="entries">The entries.</param>
    /// <returns>The node, added to the document as an indirect object.</returns>
    public static PdfDictionary Build(PdfDocument document, IEnumerable<KeyValuePair<string, PdfItem>> entries)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entries);

        var names = new PdfArray(document);

        foreach (var entry in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            names.Elements.Add(new PdfString(entry.Key));
            names.Elements.Add(entry.Value);
        }

        var node = new PdfDictionary(document);

        node.Elements["/Names"] = names;
        document.Internals.AddObject(node);

        return node;
    }
}
