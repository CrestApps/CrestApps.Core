using System.Text;

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

    /// <summary>
    /// Copies what a page shows into a form object, to draw it at another size or place. The form is the
    /// page's visible area, turned the way the page is shown; its links and form fields are not carried over.
    /// </summary>
    /// <param name="source">The page of another document.</param>
    /// <returns>The form, stored on its own in the target document, and the size it is shown at.</returns>
    public (PdfDictionary Form, double Width, double Height) ImportPageAsForm(PdfPage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var media = source.MediaBox;
        var crop = source.CropBox;
        var box = new PdfRectangle(
            Math.Max(crop.X1, media.X1),
            Math.Max(crop.Y1, media.Y1),
            Math.Min(crop.X2, media.X2),
            Math.Min(crop.Y2, media.Y2));

        if (box.Width <= 0 || box.Height <= 0)
        {
            box = media;
        }

        var (x, y, width, height) = (box.X1, box.Y1, box.Width, box.Height);

        // The form's space starts at the visible area's corner, turned as viewers turn the page.
        var (matrix, shownWidth, shownHeight) = source.Rotate switch
        {
            90 => ($"0 -1 1 0 {F(-y)} {F(x + width)}", height, width),
            180 => ($"-1 0 0 -1 {F(x + width)} {F(y + height)}", width, height),
            270 => ($"0 1 -1 0 {F(y + height)} {F(-x)}", height, width),
            _ => ($"1 0 0 1 {F(-x)} {F(-y)}", width, height),
        };

        var form = new PdfDictionary(_target);

        form.Elements.SetName("/Type", "/XObject");
        form.Elements.SetName("/Subtype", "/Form");
        form.Elements["/BBox"] = new PdfRectangle(0, 0, shownWidth, shownHeight);
        form.Elements["/Resources"] = source.Elements["/Resources"] is { } resources
            ? Copy(resources, 0)
            : new PdfDictionary(_target);

        var content = source.Contents.ReadAll();

        form.CreateStream([]);
        form.Stream.SetCompressed([.. Encoding.ASCII.GetBytes($"q {matrix} cm\n"), .. content, .. "\nQ\n"u8]);
        _target.AddObject(form);

        return (form, shownWidth, shownHeight);
    }

    private static string F(double value)
    {
        return PdfObjectModel.Format(value);
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
