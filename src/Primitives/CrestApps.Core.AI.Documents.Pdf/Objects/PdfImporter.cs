namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// Copies pages from other documents into one, with every object they use.
/// </summary>
/// <remarks>
/// Objects are copied once per source object, so pages taken from the same file share their fonts and
/// images. A reference to a page that is not copied, such as a link's destination, becomes null.
/// </remarks>
internal sealed class PdfImporter
{
    private const int MaxDepth = 256;

    private readonly PdfDocument _target;
    private readonly Dictionary<PdfReference, PdfReference> _copied = [];
    private readonly Dictionary<PdfReference, PdfReference> _pages = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfImporter"/> class.
    /// </summary>
    /// <param name="target">The document pages are copied into.</param>
    public PdfImporter(PdfDocument target)
    {
        _target = target;
    }

    /// <summary>
    /// Copies a page.
    /// </summary>
    /// <param name="source">The page of another document.</param>
    /// <returns>The copy, stored on its own in the target document.</returns>
    public PdfPage ImportPage(PdfPage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var page = new PdfPage(_target, new PdfDictionary(_target));
        var reference = _target.AddObject(page);

        // A page copied twice is two pages; references to it, such as its annotations' and later links',
        // point at the latest copy.
        if (source.Reference is not null)
        {
            _pages[source.Reference] = reference;
        }

        foreach (var (key, value) in source.Elements)
        {
            if (key != "/Parent")
            {
                page.Elements[key] = Copy(value, 0);
            }
        }

        return page;
    }

    private PdfItem Copy(PdfItem item, int depth)
    {
        if (depth > MaxDepth)
        {
            return PdfNull.Value;
        }

        switch (item)
        {
            case PdfReference reference:
                {
                    if (_copied.TryGetValue(reference, out var existing))
                    {
                        return existing;
                    }

                    var value = reference.Value;

                    if (value is PdfPage)
                    {
                        return _pages.TryGetValue(reference, out var page)
                            ? page
                            : PdfNull.Value;
                    }

                    if (value is not PdfObject source)
                    {
                        return Copy(value, depth + 1);
                    }

                    var copy = Empty(source);
                    var copied = _target.AddObject(copy);

                    _copied[reference] = copied;
                    Fill(copy, source, depth);

                    return copied;
                }

            case PdfObject source:
                {
                    var copy = Empty(source);

                    Fill(copy, source, depth);

                    return copy;
                }

            default:
                // Numbers, names, strings, booleans and rectangles never change, so they are shared.
                return item;
        }
    }

    private PdfObject Empty(PdfObject source)
    {
        return source is PdfArray
            ? new PdfArray(_target)
            : new PdfDictionary(_target);
    }

    private void Fill(PdfObject copy, PdfObject source, int depth)
    {
        if (source is PdfArray sourceArray && copy is PdfArray array)
        {
            foreach (var element in sourceArray.Elements)
            {
                array.Elements.Add(Copy(element, depth + 1));
            }

            return;
        }

        if (source is not PdfDictionary sourceDictionary || copy is not PdfDictionary dictionary)
        {
            return;
        }

        foreach (var (key, value) in sourceDictionary.Elements)
        {
            dictionary.Elements[key] = Copy(value, depth + 1);
        }

        if (sourceDictionary.Stream is not null)
        {
            dictionary.CreateStream(sourceDictionary.Stream.Value);
        }
    }
}
