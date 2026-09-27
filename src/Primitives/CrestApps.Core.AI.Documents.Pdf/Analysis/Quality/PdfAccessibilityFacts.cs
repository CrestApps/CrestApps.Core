using PdfSharp.Pdf;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// What a PDF says about its own accessibility — tagging, language, title, structure, bookmarks, form
/// fields, annotations, fonts and text — gathered once for the accessibility and compliance checks.
/// </summary>
internal sealed class PdfAccessibilityFacts
{
    /// <summary>
    /// Gets the number of pages.
    /// </summary>
    public int PageCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <c>/MarkInfo /Marked</c> is <see langword="true"/>.
    /// </summary>
    public bool IsMarked { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <c>/MarkInfo /Suspects</c> is <see langword="true"/>: the producer was not sure its tags are right.
    /// </summary>
    public bool IsSuspect { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the document has a structure tree.
    /// </summary>
    public bool HasStructureTree { get; private set; }

    /// <summary>
    /// Gets the document language (<c>/Lang</c>), or <see langword="null"/>.
    /// </summary>
    public string Language { get; private set; }

    /// <summary>
    /// Gets the title of the document information dictionary, or <see langword="null"/>.
    /// </summary>
    public string Title { get; private set; }

    /// <summary>
    /// Gets <c>/ViewerPreferences /DisplayDocTitle</c>, or <see langword="null"/> when it is not set.
    /// </summary>
    public bool? DisplayDocTitle { get; private set; }

    /// <summary>
    /// Gets the structure elements, in document order.
    /// </summary>
    public List<PdfStructureElement> Elements { get; private set; } = [];

    /// <summary>
    /// Gets the number of bookmarks.
    /// </summary>
    public int BookmarkCount { get; private set; }

    /// <summary>
    /// Gets the form fields.
    /// </summary>
    public List<PdfFormField> Fields { get; private set; } = [];

    /// <summary>
    /// Gets the annotations of every page.
    /// </summary>
    public List<PdfAnnotationEntry> Annotations { get; private set; } = [];

    /// <summary>
    /// Gets the pages that have annotations but no <c>/Tabs /S</c> (tab order following the structure).
    /// </summary>
    public List<int> PagesMissingTabOrder { get; } = [];

    /// <summary>
    /// Gets the fonts of every page.
    /// </summary>
    public List<PdfFontInfo> Fonts { get; private set; } = [];

    /// <summary>
    /// Gets the pages that show pictures but carry no text at all: scans without a text layer.
    /// </summary>
    public List<int> ImageOnlyPages { get; } = [];

    /// <summary>
    /// Gets the pages that show pictures.
    /// </summary>
    public List<int> PagesWithImages { get; } = [];

    /// <summary>
    /// Gets the XMP metadata.
    /// </summary>
    public PdfXmpInfo Xmp { get; private set; }

    /// <summary>
    /// Gets the page index, for resolving the pages structure elements refer to.
    /// </summary>
    public PdfPageIndex PageIndex { get; private set; }

    /// <summary>
    /// Gets the figures: structure elements that stand for <c>Figure</c>.
    /// </summary>
    public List<PdfStructureElement> Figures => [.. Elements.Where(element => element.StandardType == "Figure")];

    /// <summary>
    /// Gathers the facts of a document.
    /// </summary>
    /// <param name="objects">The document, opened with PDFsharp.</param>
    /// <param name="content">The document, opened with PdfPig, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The facts.</returns>
    public static PdfAccessibilityFacts Collect(PdfDocument objects, PigDocument content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objects);

        var catalog = objects.Internals.Catalog;
        var markInfo = PdfObjects.GetDictionary(catalog, "/MarkInfo");
        var pages = Enumerable.Range(1, objects.PageCount).ToList();
        var facts = new PdfAccessibilityFacts
        {
            PageCount = objects.PageCount,
            IsMarked = PdfObjects.GetBoolean(markInfo, "/Marked") == true,
            IsSuspect = PdfObjects.GetBoolean(markInfo, "/Suspects") == true,
            HasStructureTree = PdfObjects.GetDictionary(catalog, "/StructTreeRoot") is not null,
            Language = PdfObjects.GetText(catalog, "/Lang")?.Trim(),
            Title = ReadInfoTitle(objects),
            DisplayDocTitle = PdfObjects.GetBoolean(PdfObjects.GetDictionary(catalog, "/ViewerPreferences"), "/DisplayDocTitle"),
            Elements = PdfStructureTree.Read(objects),
            BookmarkCount = CountBookmarks(objects),
            Fields = PdfFormFieldList.Read(objects),
            Annotations = PdfAnnotationList.Read(objects, pages),
            Fonts = PdfFontInventory.Collect(objects, pages),
            Xmp = PdfXmpInfo.Read(objects),
            PageIndex = new PdfPageIndex(objects),
        };

        foreach (var page in facts.Annotations.Where(annotation => annotation.Subtype != "Popup").Select(annotation => annotation.Page).Distinct())
        {
            if (!PdfObjects.IsName(objects.Pages[page - 1], "/Tabs", "/S"))
            {
                facts.PagesMissingTabOrder.Add(page);
            }
        }

        if (content is not null)
        {
            for (var number = 1; number <= content.NumberOfPages; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var page = content.GetPage(number);

                    if (page.NumberOfImages == 0)
                    {
                        continue;
                    }

                    facts.PagesWithImages.Add(number);

                    if (!page.Letters.Any(letter => !string.IsNullOrWhiteSpace(letter.Value)))
                    {
                        facts.ImageOnlyPages.Add(number);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A page PdfPig cannot read is reported by validate_pdf; it is simply not counted here.
                }
            }
        }

        return facts;
    }

    /// <summary>
    /// Finds the page a structure element belongs to.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The one-based page, or <see langword="null"/>.</returns>
    public int? PageOf(PdfStructureElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var current = element.Dictionary;

        for (var depth = 0; depth < 32 && current is not null; depth++)
        {
            var page = PdfObjects.Get(current, "/Pg");

            if (page is PdfDictionary && PageIndex.TryGetPage(page, out var number))
            {
                return number;
            }

            current = PdfObjects.GetDictionary(current, "/P");
        }

        return null;
    }

    private static string ReadInfoTitle(PdfDocument objects)
    {
        try
        {
            var title = objects.Info.Title;

            return string.IsNullOrWhiteSpace(title)
                ? null
                : title.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int CountBookmarks(PdfDocument objects)
    {
        var outlines = PdfObjects.GetDictionary(objects.Internals.Catalog, "/Outlines");
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfDictionary>();
        var count = 0;

        if (PdfObjects.GetDictionary(outlines, "/First") is { } first)
        {
            pending.Push(first);
        }

        while (pending.Count > 0 && count < 100_000)
        {
            for (var current = pending.Pop(); current is not null && visited.Add(current); current = PdfObjects.GetDictionary(current, "/Next"))
            {
                count++;

                if (PdfObjects.GetDictionary(current, "/First") is { } child)
                {
                    pending.Push(child);
                }
            }
        }

        return count;
    }
}
