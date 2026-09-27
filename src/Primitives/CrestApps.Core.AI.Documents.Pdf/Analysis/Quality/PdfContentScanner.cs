using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A resource a content stream names that its resource dictionary does not have.
/// </summary>
/// <param name="Page">The one-based page.</param>
/// <param name="Kind">The resource category, for example <c>Font</c>.</param>
/// <param name="Name">The resource name, for example <c>/F3</c>.</param>
/// <param name="Where">Where the content is: <c>page</c>, or the form XObject it is drawn from.</param>
internal sealed record PdfMissingResource(int Page, string Kind, string Name, string Where);

/// <summary>
/// A problem reading the content of a page.
/// </summary>
/// <param name="Page">The one-based page.</param>
/// <param name="Where">Where the content is: <c>page</c>, or the form XObject it is drawn from.</param>
/// <param name="Message">What is wrong.</param>
internal sealed record PdfContentProblem(int Page, string Where, string Message);

/// <summary>
/// What scanning the content streams of a document found.
/// </summary>
internal sealed class PdfContentScanResult
{
    /// <summary>
    /// Gets the syntax errors, by page.
    /// </summary>
    public List<PdfContentProblem> Errors { get; } = [];

    /// <summary>
    /// Gets the problems that do not stop the content from being drawn.
    /// </summary>
    public List<PdfContentProblem> Warnings { get; } = [];

    /// <summary>
    /// Gets the resources the content names but the resource dictionaries lack.
    /// </summary>
    public List<PdfMissingResource> MissingResources { get; } = [];

    /// <summary>
    /// Gets, for every font a content stream selects, the pages it is selected on.
    /// </summary>
    public Dictionary<PdfDictionary, SortedSet<int>> FontPages { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets, for every font text is shown in, the pages text is shown in it on.
    /// </summary>
    public Dictionary<PdfDictionary, SortedSet<int>> TextFontPages { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets the pages whose content was scanned.
    /// </summary>
    public List<int> ScannedPages { get; } = [];
}

/// <summary>
/// Reads the content streams of pages, and of the form XObjects they draw, with PDFsharp's object model and
/// <see cref="PdfContentParser"/>: syntax, the resources each stream names and the fonts it selects.
/// </summary>
internal static class PdfContentScanner
{
    private const int MaxFormDepth = 8;
    private const int MaxRecords = 200;

    private static readonly HashSet<string> _deviceColorSpaces = new(StringComparer.Ordinal)
    {
        "/DeviceGray",
        "/DeviceRGB",
        "/DeviceCMYK",
        "/Pattern",
        "/G",
        "/RGB",
        "/CMYK",
    };

    /// <summary>
    /// Scans pages of a document.
    /// </summary>
    /// <param name="document">The document, opened with PDFsharp.</param>
    /// <param name="pages">The one-based pages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was found.</returns>
    public static PdfContentScanResult Scan(PdfDocument document, IEnumerable<int> pages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pages);

        var result = new PdfContentScanResult();

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (number < 1 || number > document.PageCount)
            {
                continue;
            }

            var page = document.Pages[number - 1];
            var resources = PdfObjects.GetInherited(page, "/Resources") as PdfDictionary;

            result.ScannedPages.Add(number);

            byte[] content;

            try
            {
                content = ReadContents(page);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Add(result.Errors, new PdfContentProblem(number, "page", "its content stream cannot be decoded: " + ex.Message));

                continue;
            }

            ScanStream(content, resources, number, "page", result, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), 0);
        }

        return result;
    }

    /// <summary>
    /// Reads a page's content: its content stream, or the streams of its content array joined in order.
    /// </summary>
    /// <param name="page">The page dictionary.</param>
    /// <returns>The decoded content.</returns>
    public static byte[] ReadContents(PdfDictionary page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var contents = PdfObjects.Get(page, "/Contents");

        if (contents is PdfDictionary single)
        {
            return single.Stream?.UnfilteredValue ?? [];
        }

        if (contents is not PdfArray array)
        {
            return [];
        }

        using var buffer = new MemoryStream();

        foreach (var item in PdfObjects.Items(array))
        {
            if (item is not PdfDictionary part || part.Stream is null)
            {
                continue;
            }

            var bytes = part.Stream.UnfilteredValue;

            buffer.Write(bytes, 0, bytes.Length);
            buffer.WriteByte((byte)'\n');
        }

        return buffer.ToArray();
    }

    private static void ScanStream(
        byte[] content,
        PdfDictionary resources,
        int page,
        string where,
        PdfContentScanResult result,
        HashSet<PdfDictionary> forms,
        int depth)
    {
        var parsed = PdfContentParser.Parse(content);

        foreach (var error in parsed.Errors)
        {
            Add(result.Errors, new PdfContentProblem(page, where, error));
        }

        foreach (var warning in parsed.Warnings)
        {
            Add(result.Warnings, new PdfContentProblem(page, where, warning));
        }

        if (parsed.Truncated)
        {
            Add(result.Warnings, new PdfContentProblem(page, where, "the content is very long; only its first operations were checked"));
        }

        PdfDictionary currentFont = null;

        foreach (var operation in parsed.Operations)
        {
            var operands = operation.Operands;

            switch (operation.Operator)
            {
                case "Tf" when operands.Count > 0 && IsName(operands[0]):
                    currentFont = Require(resources, "/Font", "Font", operands[0], page, where, result);

                    if (currentFont is not null)
                    {
                        Record(result.FontPages, currentFont, page);
                    }

                    break;
                case "Do" when operands.Count > 0 && IsName(operands[0]):
                    var xobject = Require(resources, "/XObject", "XObject", operands[0], page, where, result);

                    if (xobject is not null &&
                        PdfObjects.IsName(xobject, "/Subtype", "/Form") &&
                        depth < MaxFormDepth &&
                        forms.Add(xobject))
                    {
                        byte[] formContent;

                        try
                        {
                            formContent = xobject.Stream?.UnfilteredValue ?? [];
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Add(result.Errors, new PdfContentProblem(page, "form " + operands[0], "its content stream cannot be decoded: " + ex.Message));

                            break;
                        }

                        var formResources = PdfObjects.GetDictionary(xobject, "/Resources") ?? resources;

                        ScanStream(formContent, formResources, page, "form " + operands[0], result, forms, depth + 1);
                    }

                    break;
                case "gs" when operands.Count > 0 && IsName(operands[0]):
                    Require(resources, "/ExtGState", "ExtGState", operands[0], page, where, result);

                    break;
                case "sh" when operands.Count > 0 && IsName(operands[0]):
                    Require(resources, "/Shading", "Shading", operands[0], page, where, result);

                    break;
                case "cs" or "CS" when operands.Count > 0 && IsName(operands[0]) && !_deviceColorSpaces.Contains(operands[0]):
                    Require(resources, "/ColorSpace", "ColorSpace", operands[0], page, where, result);

                    break;
                case "scn" or "SCN" when operands.Count > 0 && IsName(operands[^1]):
                    Require(resources, "/Pattern", "Pattern", operands[^1], page, where, result);

                    break;
                case "BDC" or "DP" when operands.Count > 1 && IsName(operands[1]):
                    Require(resources, "/Properties", "Properties", operands[1], page, where, result);

                    break;
                default:
                    if (currentFont is not null && PdfContentParser.IsTextShowing(operation.Operator))
                    {
                        Record(result.TextFontPages, currentFont, page);
                    }

                    break;
            }
        }
    }

    private static PdfDictionary Require(
        PdfDictionary resources,
        string category,
        string kind,
        string name,
        int page,
        string where,
        PdfContentScanResult result)
    {
        var dictionary = PdfObjects.GetDictionary(resources, category);
        var value = dictionary is null
            ? null
            : PdfObjects.Get(dictionary, name);

        if (value is PdfDictionary found)
        {
            return found;
        }

        Add(result.MissingResources, new PdfMissingResource(page, kind, name, where));

        return null;
    }

    private static void Record(Dictionary<PdfDictionary, SortedSet<int>> map, PdfDictionary font, int page)
    {
        if (!map.TryGetValue(font, out var pages))
        {
            pages = [];
            map[font] = pages;
        }

        pages.Add(page);
    }

    private static bool IsName(string operand)
    {
        return operand.Length > 1 && operand[0] == '/';
    }

    private static void Add<T>(List<T> list, T item)
    {
        if (list.Count < MaxRecords)
        {
            list.Add(item);
        }
    }
}
