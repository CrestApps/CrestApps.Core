using System.Collections;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The pages of a <see cref="PdfDocument"/>, in order.
/// </summary>
/// <remarks>
/// The page tree a file stores is read into one list, each page taking the entries it inherited, and is
/// written back as a single node holding every page.
/// </remarks>
internal sealed class PdfPages : IEnumerable<PdfPage>
{
    private static readonly string[] _inherited = ["/Resources", "/MediaBox", "/CropBox", "/Rotate"];

    private readonly PdfDocument _document;
    private readonly List<PdfPage> _pages = [];
    private PdfImporter _importer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfPages"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    public PdfPages(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Gets the number of pages.
    /// </summary>
    public int Count => _pages.Count;

    /// <summary>
    /// Gets a page by its zero-based index.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>The page.</returns>
    public PdfPage this[int index] => _pages[index];

    /// <summary>
    /// Returns the zero-based index of a page, or -1.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The index.</returns>
    public int IndexOf(PdfPage page)
    {
        return _pages.IndexOf(page);
    }

    /// <summary>
    /// Returns whether the document has a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns><see langword="true"/> when the page is in the document.</returns>
    public bool Contains(PdfPage page)
    {
        return _pages.Contains(page);
    }

    /// <summary>
    /// Adds a page at the end. A page of another document is copied in, with everything it uses.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The page as it is in this document.</returns>
    public PdfPage Add(PdfPage page)
    {
        return Insert(_pages.Count, page);
    }

    /// <summary>
    /// Inserts a page. A page of another document is copied in, with everything it uses.
    /// </summary>
    /// <param name="index">The zero-based index the page takes.</param>
    /// <param name="page">The page.</param>
    /// <returns>The page as it is in this document.</returns>
    public PdfPage Insert(int index, PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!ReferenceEquals(page.Owner, _document))
        {
            _importer ??= new PdfImporter(_document);
            page = _importer.ImportPage(page);
        }

        _document.AddObject(page);
        _pages.Insert(Math.Clamp(index, 0, _pages.Count), page);

        return page;
    }

    /// <summary>
    /// Removes a page by its zero-based index.
    /// </summary>
    /// <param name="index">The index.</param>
    public void RemoveAt(int index)
    {
        _pages.RemoveAt(index);
    }

    /// <summary>
    /// Moves a page.
    /// </summary>
    /// <param name="from">The zero-based index the page has.</param>
    /// <param name="to">The zero-based index it takes.</param>
    public void MovePage(int from, int to)
    {
        var page = _pages[from];

        _pages.RemoveAt(from);
        _pages.Insert(Math.Clamp(to, 0, _pages.Count), page);
    }

    /// <inheritdoc/>
    public IEnumerator<PdfPage> GetEnumerator()
    {
        return _pages.GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Reads the page tree a file stores into the list of pages.
    /// </summary>
    /// <param name="root">The catalog's <c>/Pages</c> entry.</param>
    internal void LoadTree(PdfItem root)
    {
        var visited = new HashSet<PdfReference>();

        Walk(root, [], visited, 0);
    }

    /// <summary>
    /// Writes the list of pages back as the catalog's page tree: one node whose kids are every page.
    /// </summary>
    internal void WriteTree()
    {
        var node = _document.Catalog.Elements.GetDictionary("/Pages");

        if (node is null)
        {
            node = new PdfDictionary(_document);
            _document.Catalog.Elements.SetReference("/Pages", node);
        }
        else if (!node.IsIndirect)
        {
            _document.Catalog.Elements.SetReference("/Pages", node);
        }

        // The pages carry what they inherited, so the node keeps nothing for them to inherit.
        foreach (var key in _inherited)
        {
            node.Elements.Remove(key);
        }

        var kids = new PdfArray(_document);

        foreach (var page in _pages)
        {
            page.Elements["/Parent"] = node.Reference;
            kids.Elements.Add(_document.AddObject(page));
        }

        node.Elements.SetName("/Type", "/Pages");
        node.Elements["/Kids"] = kids;
        node.Elements.SetInteger("/Count", _pages.Count);
    }

    private void Walk(PdfItem item, Dictionary<string, PdfItem> inherited, HashSet<PdfReference> visited, int depth)
    {
        if (depth > 64 || (item is PdfReference reference && !visited.Add(reference)))
        {
            return;
        }

        if (PdfObjectModel.Resolve(item) is not PdfDictionary node)
        {
            return;
        }

        var kids = node.Elements.GetArray("/Kids");
        var isPage = node is PdfPage || (kids is null && !string.Equals(node.Elements.GetName("/Type"), "/Pages", StringComparison.Ordinal));

        if (isPage)
        {
            _pages.Add(AsPage(item as PdfReference, node, inherited));

            return;
        }

        var passed = new Dictionary<string, PdfItem>(inherited, StringComparer.Ordinal);

        foreach (var key in _inherited)
        {
            if (node.Elements[key] is { } value)
            {
                passed[key] = value;
            }
        }

        foreach (var kid in kids?.Elements ?? [])
        {
            Walk(kid, passed, visited, depth + 1);
        }
    }

    private PdfPage AsPage(PdfReference reference, PdfDictionary node, Dictionary<string, PdfItem> inherited)
    {
        var page = node as PdfPage ?? new PdfPage(_document, node);

        foreach (var (key, value) in inherited)
        {
            if (!page.Elements.ContainsKey(key))
            {
                page.Elements[key] = value;
            }
        }

        page.Elements.Remove("/Parent");

        if (reference is not null && !ReferenceEquals(reference.Value, page))
        {
            reference.Value = page;
            page.Reference = reference;
        }

        return page;
    }
}
