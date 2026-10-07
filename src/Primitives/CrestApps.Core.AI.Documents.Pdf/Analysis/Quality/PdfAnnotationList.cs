using System.Globalization;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One annotation of a page, with where a link annotation leads.
/// </summary>
internal sealed class PdfAnnotationEntry
{
    /// <summary>
    /// Gets or sets the annotation dictionary.
    /// </summary>
    public PdfDictionary Dictionary { get; set; }

    /// <summary>
    /// Gets or sets the one-based page the annotation is on.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Gets or sets the annotation type without its slash, for example <c>Link</c> or <c>Widget</c>.
    /// </summary>
    public string Subtype { get; set; }

    /// <summary>
    /// Gets or sets where the annotation is placed, or <see langword="null"/> when its rectangle is missing or malformed.
    /// </summary>
    public PdfBox? Rect { get; set; }

    /// <summary>
    /// Gets or sets the action type of a link without its slash, for example <c>URI</c> or <c>GoTo</c>, or
    /// <c>Dest</c> for a link that names its destination directly.
    /// </summary>
    public string Action { get; set; }

    /// <summary>
    /// Gets or sets the web address of a <c>URI</c> link.
    /// </summary>
    public string Uri { get; set; }

    /// <summary>
    /// Gets or sets the one-based page an internal link leads to, when it resolves.
    /// </summary>
    public int? TargetPage { get; set; }

    /// <summary>
    /// Gets or sets why an internal link's destination does not resolve, or <see langword="null"/>.
    /// </summary>
    public string TargetProblem { get; set; }

    /// <summary>
    /// Gets or sets the file a link to another document names.
    /// </summary>
    public string RemoteFile { get; set; }

    /// <summary>
    /// Gets the annotation's text description (<c>/Contents</c>), or <see langword="null"/>.
    /// </summary>
    public string Contents => PdfObjectReader.GetText(Dictionary, "/Contents");

    /// <summary>
    /// Gets a value indicating whether the annotation is a link.
    /// </summary>
    public bool IsLink => string.Equals(Subtype, "Link", StringComparison.Ordinal);
}

/// <summary>
/// Lists the annotations of pages and resolves where their links lead, without following any web address.
/// </summary>
internal static class PdfAnnotationList
{
    private const int MaxAnnotations = 20_000;

    /// <summary>
    /// Lists the annotations of pages.
    /// </summary>
    /// <param name="document">The document, opened with PDFsharp.</param>
    /// <param name="pages">The one-based pages.</param>
    /// <returns>The annotations, page by page.</returns>
    public static List<PdfAnnotationEntry> Read(PdfDocument document, IEnumerable<int> pages)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pages);

        var pageIndex = new PdfPageIndex(document);
        var entries = new List<PdfAnnotationEntry>();

        foreach (var number in pages)
        {
            if (number < 1 || number > document.PageCount)
            {
                continue;
            }

            foreach (var item in PdfObjectReader.Items(PdfObjectReader.GetArray(document.Pages[number - 1], "/Annots")))
            {
                if (item is not PdfDictionary annotation)
                {
                    continue;
                }

                var entry = new PdfAnnotationEntry
                {
                    Dictionary = annotation,
                    Page = number,
                    Subtype = PdfObjectReader.GetName(annotation, "/Subtype")?.TrimStart('/'),
                    Rect = ReadRect(annotation),
                };

                if (entry.IsLink)
                {
                    ResolveLink(document, entry, pageIndex);
                }

                entries.Add(entry);

                if (entries.Count >= MaxAnnotations)
                {
                    return entries;
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Resolves a destination to the page it shows.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="destination">The destination: an explicit array, or a named destination.</param>
    /// <param name="pageIndex">The document's page index.</param>
    /// <returns>The one-based page, or the reason the destination does not resolve.</returns>
    public static (int? Page, string Problem) ResolveDestination(
        PdfDocument document,
        PdfItem destination,
        PdfPageIndex pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        return ResolveDestination(document, destination, pageIndex, 0);
    }

    private static (int? Page, string Problem) ResolveDestination(
        PdfDocument document,
        PdfItem destination,
        PdfPageIndex pageIndex,
        int depth)
    {
        if (depth > 4)
        {
            return (null, "its destination refers to itself");
        }

        var resolved = PdfObjectReader.Resolve(destination);

        if (resolved is PdfDictionary wrapper && PdfObjectReader.Get(wrapper, "/D") is { } inner)
        {
            return ResolveDestination(document, inner, pageIndex, depth + 1);
        }

        if (resolved is PdfArray array)
        {
            if (array.Elements.Count == 0)
            {
                return (null, "its destination is empty");
            }

            var first = array.Elements[0];

            if (PdfObjectReader.Resolve(first) is PdfDictionary)
            {
                if (pageIndex.TryGetPage(first, out var page))
                {
                    return (page, null);
                }

                return (null, "its destination is not a page of this document");
            }

            var number = PdfObjectReader.AsNumber(first);

            if (number is not null)
            {
                var index = (int)number.Value;

                return index >= 0 && index < document.PageCount
                    ? (index + 1, "its destination names the page by number instead of referring to the page, which some viewers ignore")
                    : (null, string.Create(CultureInfo.InvariantCulture, $"its destination names page index {index}, which does not exist"));
            }

            return (null, "its destination refers to an object that does not exist");
        }

        var name = PdfObjectReader.AsText(resolved);

        if (name is null)
        {
            return (null, "its destination is missing");
        }

        var catalog = document.Internals.Catalog;
        var named = PdfObjectReader.Get(PdfObjectReader.GetDictionary(catalog, "/Dests"), "/" + name) ??
            PdfObjectReader.LookUpNameTree(PdfObjectReader.GetDictionary(PdfObjectReader.GetDictionary(catalog, "/Names"), "/Dests"), name);

        if (named is null)
        {
            return (null, $"its named destination \"{name}\" does not exist");
        }

        return ResolveDestination(document, named, pageIndex, depth + 1);
    }

    private static void ResolveLink(
        PdfDocument document,
        PdfAnnotationEntry entry,
        PdfPageIndex pageIndex)
    {
        var annotation = entry.Dictionary;
        var action = PdfObjectReader.GetDictionary(annotation, "/A");

        if (action is null)
        {
            if (annotation.Elements.ContainsKey("/Dest"))
            {
                entry.Action = "Dest";
                (entry.TargetPage, entry.TargetProblem) = ResolveDestination(document, PdfObjectReader.Get(annotation, "/Dest"), pageIndex);
            }

            return;
        }

        entry.Action = PdfObjectReader.GetName(action, "/S")?.TrimStart('/') ?? "unknown";

        switch (entry.Action)
        {
            case "URI":
                entry.Uri = PdfObjectReader.GetText(action, "/URI");

                break;
            case "GoTo":
                (entry.TargetPage, entry.TargetProblem) = ResolveDestination(document, PdfObjectReader.Get(action, "/D"), pageIndex);

                break;
            case "GoToR" or "GoToE" or "Launch":
                var file = PdfObjectReader.Get(action, "/F");

                entry.RemoteFile = file is PdfDictionary specification
                    ? PdfObjectReader.GetText(specification, "/UF") ?? PdfObjectReader.GetText(specification, "/F")
                    : PdfObjectReader.AsText(file);

                break;
        }
    }

    private static PdfBox? ReadRect(PdfDictionary annotation)
    {
        var numbers = PdfObjectReader.GetNumbers(annotation, "/Rect");

        if (numbers is not { Count: 4 })
        {
            return null;
        }

        return new PdfBox(
            Math.Min(numbers[0], numbers[2]),
            Math.Min(numbers[1], numbers[3]),
            Math.Max(numbers[0], numbers[2]),
            Math.Max(numbers[1], numbers[3]));
    }
}
