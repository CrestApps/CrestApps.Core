using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Small helpers for working with PDF objects directly through PDFsharp's dictionaries, where its typed model
/// does not reach: creating fields and appearance streams, and walking inherited entries.
/// </summary>
internal static class PdfLowLevel
{
    /// <summary>
    /// Follows a reference to the object it names.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The direct object.</returns>
    public static PdfItem Resolve(PdfItem item)
    {
        return item is PdfReference reference
            ? reference.Value
            : item;
    }

    /// <summary>
    /// Reads an entry as a dictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The dictionary, or <see langword="null"/>.</returns>
    public static PdfDictionary GetDictionary(PdfDictionary dictionary, string key)
    {
        return dictionary is null
            ? null
            : Resolve(dictionary.Elements[key]) as PdfDictionary;
    }

    /// <summary>
    /// Reads an entry as an array.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The array, or <see langword="null"/>.</returns>
    public static PdfArray GetArray(PdfDictionary dictionary, string key)
    {
        return dictionary is null
            ? null
            : Resolve(dictionary.Elements[key]) as PdfArray;
    }

    /// <summary>
    /// Lists the items of an array, or none when there is no array.
    /// </summary>
    /// <param name="array">The array, or <see langword="null"/>.</param>
    /// <returns>The items.</returns>
    public static List<PdfItem> Items(PdfArray array)
    {
        return array is null
            ? []
            : [.. array.Elements];
    }

    /// <summary>
    /// Reads an entry, looking up the chain of parents when the dictionary does not set it itself, the way
    /// field attributes are inherited.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static PdfItem GetInherited(PdfDictionary dictionary, string key)
    {
        var current = dictionary;

        for (var depth = 0; current is not null && depth < 32; depth++)
        {
            if (current.Elements.ContainsKey(key))
            {
                return Resolve(current.Elements[key]);
            }

            current = GetDictionary(current, "/Parent");
        }

        return null;
    }

    /// <summary>
    /// Reads a text value, whatever string form it is stored in.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public static string Text(PdfItem item)
    {
        return Resolve(item) switch
        {
            PdfString text => text.Value,
            PdfName name => name.Value.TrimStart('/'),
            PdfInteger number => number.Value.ToString(CultureInfo.InvariantCulture),
            PdfReal number => number.Value.ToString(CultureInfo.InvariantCulture),
            PdfBoolean flag => flag.Value ? "true" : "false",
            _ => null,
        };
    }

    /// <summary>
    /// Reads a number.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The number, or 0.</returns>
    public static double Number(PdfItem item)
    {
        return Resolve(item) switch
        {
            PdfInteger number => number.Value,
            PdfReal number => number.Value,
            PdfLongInteger number => number.Value,
            _ => 0,
        };
    }

    /// <summary>
    /// Creates an array of numbers.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="values">The numbers.</param>
    /// <returns>The array.</returns>
    public static PdfArray Numbers(PdfDocument document, params double[] values)
    {
        var array = new PdfArray(document);

        foreach (var value in values)
        {
            array.Elements.Add(new PdfReal(Math.Round(value, 3)));
        }

        return array;
    }

    /// <summary>
    /// Gets a page's annotation array, creating it when the page has none.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="create">Whether to create the array when it is missing.</param>
    /// <returns>The array, or <see langword="null"/> when there is none and <paramref name="create"/> is false.</returns>
    public static PdfArray GetAnnotations(PdfPage page, bool create)
    {
        ArgumentNullException.ThrowIfNull(page);

        var annotations = GetArray(page, "/Annots");

        if (annotations is null && create)
        {
            annotations = new PdfArray(page.Owner);
            page.Elements["/Annots"] = annotations;
        }

        return annotations;
    }

    /// <summary>
    /// Returns whether an array holds a reference to an object.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="value">The object.</param>
    /// <returns>The index of the entry, or -1.</returns>
    public static int IndexOf(PdfArray array, PdfObject value)
    {
        if (array is null || value is null)
        {
            return -1;
        }

        for (var index = 0; index < array.Elements.Count; index++)
        {
            if (ReferenceEquals(Resolve(array.Elements[index]), value))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the one-based page an annotation sits on.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="annotation">The annotation.</param>
    /// <returns>The page number, or <see langword="null"/>.</returns>
    public static int? FindPage(PdfDocument document, PdfDictionary annotation)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pageReference = GetDictionary(annotation, "/P");

        for (var index = 0; index < document.PageCount; index++)
        {
            var page = document.Pages[index];

            if (ReferenceEquals(page, pageReference) || IndexOf(GetAnnotations(page, create: false), annotation) >= 0)
            {
                return index + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a rectangle array.
    /// </summary>
    /// <param name="dictionary">The dictionary holding it.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The rectangle, or <see langword="null"/>.</returns>
    public static PdfRectangle GetRectangle(PdfDictionary dictionary, string key = "/Rect")
    {
        var array = GetArray(dictionary, key);

        if (array is null || array.Elements.Count < 4)
        {
            return null;
        }

        var x1 = Number(array.Elements[0]);
        var y1 = Number(array.Elements[1]);
        var x2 = Number(array.Elements[2]);
        var y2 = Number(array.Elements[3]);

        return new PdfRectangle(
            new PdfSharp.Drawing.XPoint(Math.Min(x1, x2), Math.Min(y1, y2)),
            new PdfSharp.Drawing.XPoint(Math.Max(x1, x2), Math.Max(y1, y2)));
    }

    /// <summary>
    /// Creates a form XObject, the kind of object an annotation's appearance is drawn with.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="content">The content stream.</param>
    /// <param name="resources">The resources the content uses, or <see langword="null"/>.</param>
    /// <returns>The form, added to the document.</returns>
    public static PdfDictionary CreateForm(PdfDocument document, double width, double height, string content, PdfDictionary resources = null)
    {
        var form = new PdfDictionary(document);

        form.Elements.SetName("/Type", "/XObject");
        form.Elements.SetName("/Subtype", "/Form");
        form.Elements["/BBox"] = Numbers(document, 0, 0, width, height);

        if (resources is not null)
        {
            form.Elements["/Resources"] = resources;
        }

        form.CreateStream(Encoding.Latin1.GetBytes(content));
        document.Internals.AddObject(form);

        return form;
    }

    /// <summary>
    /// Gets or creates the standard Helvetica font a document's fields and stamps are written in.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The font dictionary, added to the document.</returns>
    public static PdfDictionary Helvetica(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var form = GetDictionary(document.Internals.Catalog, "/AcroForm");
        var existing = GetDictionary(GetDictionary(GetDictionary(form, "/DR"), "/Font"), "/Helv");

        if (existing is not null)
        {
            return existing;
        }

        var font = new PdfDictionary(document);

        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        document.Internals.AddObject(font);

        return font;
    }

    /// <summary>
    /// Creates a resource dictionary naming the Helvetica font <c>/Helv</c>.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The resources.</returns>
    public static PdfDictionary HelveticaResources(PdfDocument document)
    {
        var fonts = new PdfDictionary(document);
        fonts.Elements["/Helv"] = Helvetica(document).Reference;

        var resources = new PdfDictionary(document);
        resources.Elements["/Font"] = fonts;

        return resources;
    }

    /// <summary>
    /// Writes text as a PDF literal string in the WinAnsi encoding the standard fonts use, replacing what the
    /// encoding cannot hold.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The literal, with its parentheses.</returns>
    public static string Literal(string text)
    {
        var builder = new StringBuilder("(");

        foreach (var character in text ?? string.Empty)
        {
            switch (character)
            {
                case '(' or ')' or '\\':
                    builder.Append('\\').Append(character);

                    break;
                case '\r' or '\n' or '\t':
                    builder.Append(' ');

                    break;
                default:
                    builder.Append(character is >= ' ' and <= 'ÿ' ? character : '?');

                    break;
            }
        }

        return builder.Append(')').ToString();
    }

    /// <summary>
    /// Formats a number for a content stream.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The invariant text.</returns>
    public static string Format(double value)
    {
        return Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Appends drawing to a page, isolated from its existing content by a saved graphics state.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="content">The content to draw.</param>
    public static void AppendIsolated(PdfPage page, string content)
    {
        ArgumentNullException.ThrowIfNull(page);

        // The existing content is wrapped so a transformation it leaves in effect cannot move the new drawing.
        page.Contents.PrependContent().CreateStream(Encoding.ASCII.GetBytes("q\n"));
        page.Contents.AppendContent().CreateStream(Encoding.Latin1.GetBytes("\nQ\n" + content + "\n"));
    }

    /// <summary>
    /// Adds a form XObject to a page's resources under a fresh name.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="form">The form.</param>
    /// <param name="prefix">The name prefix.</param>
    /// <returns>The resource name, with its slash.</returns>
    public static string AddXObject(PdfPage page, PdfDictionary form, string prefix)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(form);

        var xobjects = GetDictionary(page.Resources, "/XObject");

        if (xobjects is null)
        {
            xobjects = new PdfDictionary(page.Owner);
            page.Resources.Elements["/XObject"] = xobjects;
        }

        if (form.Reference is null)
        {
            page.Owner.Internals.AddObject(form);
        }

        var index = 1;
        string name;

        do
        {
            name = "/" + prefix + index.ToString(CultureInfo.InvariantCulture);
            index++;
        }
        while (xobjects.Elements.ContainsKey(name));

        xobjects.Elements[name] = form.Reference;

        return name;
    }
}
