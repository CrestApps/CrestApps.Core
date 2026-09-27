using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Permanently removes content from a PDF: finds what is to go, takes it out of the page content, removes the
/// annotations over it, marks the areas, and then reads the result back to confirm nothing is left to
/// extract.
/// </summary>
internal static class PdfRedactor
{
    private const double Padding = 1;
    private const int MaxMatchesPerPage = 500;

    /// <summary>
    /// Finds the areas a request removes.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file needs one to open.</param>
    /// <param name="request">The request.</param>
    /// <returns>The areas by page, and what was matched.</returns>
    public static (Dictionary<int, List<PdfBox>> Areas, List<PdfTextMatch> Matches) FindAreas(byte[] bytes, string password, PdfRedactionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var areas = new Dictionary<int, List<PdfBox>>();
        var matches = new List<PdfTextMatch>();

        using var pdf = PdfFiles.OpenForReading(bytes, password);

        void Add(int page, PdfBox box)
        {
            if (!areas.TryGetValue(page, out var list))
            {
                areas[page] = list = [];
            }

            list.Add(box);
        }

        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            if (request.Pages is not null && !request.Pages.Contains(number))
            {
                continue;
            }

            var page = pdf.GetPage(number);
            var found = new List<PdfTextMatch>();

            foreach (var text in request.Texts.Where(text => !string.IsNullOrWhiteSpace(text)))
            {
                found.AddRange(PdfTextFinder.Find(page, text, isRegex: false, request.MatchCase, wholeWord: false, MaxMatchesPerPage));
            }

            foreach (var pattern in request.Patterns.Where(pattern => !string.IsNullOrWhiteSpace(pattern)))
            {
                found.AddRange(PdfTextFinder.Find(page, pattern, isRegex: true, request.MatchCase, wholeWord: false, MaxMatchesPerPage));
            }

            if (request.Categories.Count > 0)
            {
                found.AddRange(PdfTextFinder.FindPatterns(page, request.Categories));
            }

            foreach (var match in found)
            {
                foreach (var box in match.Boxes)
                {
                    Add(number, box.Inflate(Padding));
                }
            }

            matches.AddRange(found);

            if (request.WholePages.Contains(number))
            {
                Add(number, PdfBox.VisibleArea(page));
            }
        }

        foreach (var (page, box) in request.Areas)
        {
            if (page < 1 || page > pdf.NumberOfPages)
            {
                throw new PdfToolException($"Page {page} does not exist; the document has {pdf.NumberOfPages} page(s).");
            }

            Add(page, box);
        }

        return (areas, matches);
    }

    /// <summary>
    /// Removes the content of areas from a PDF.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The owner password, when the file is protected.</param>
    /// <param name="areas">The areas by page, in user space.</param>
    /// <param name="request">How the areas are marked.</param>
    /// <returns>The redacted file and what was removed from each page.</returns>
    public static (byte[] Bytes, List<PdfRedactionPageResult> Pages) Apply(
        byte[] bytes,
        string password,
        Dictionary<int, List<PdfBox>> areas,
        PdfRedactionRequest request)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<PdfRedactionPageResult>();

        using (var document = PdfFiles.OpenForEditing(bytes, password))
        {
            foreach (var (number, boxes) in areas.OrderBy(entry => entry.Key))
            {
                var page = document.Pages[number - 1];
                var result = new PdfRedactionPageResult { Page = number, Areas = boxes.Count };

                try
                {
                    var rewriter = PdfContentRewriter.Rewrite(page, new PdfContentRewriteOptions { Areas = boxes });

                    result.Glyphs = rewriter.GlyphsRemoved;
                    result.Images = rewriter.ImagesRemoved;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.Error = "the page content could not be rewritten (" + ex.Message + ")";
                }

                if (request.RemoveAnnotations)
                {
                    result.Annotations = RemoveAnnotations(page, boxes);
                }

                if (request.FillAreas)
                {
                    Paint(page, boxes, request);
                }

                results.Add(result);
            }

            bytes = PdfFiles.Save(document);
        }

        // Read back from the saved file: what counts is what a reader of the delivered file can extract.
        using var check = PdfFiles.OpenForReading(bytes);

        foreach (var result in results)
        {
            var boxes = areas[result.Page];
            var page = check.GetPage(result.Page);

            result.Remaining = page.Letters.Count(letter =>
            {
                var box = PdfBox.From(letter.BoundingBox);
                var x = (box.Left + box.Right) / 2;
                var y = (box.Bottom + box.Top) / 2;

                return !string.IsNullOrWhiteSpace(letter.Value) &&
                    letter.RenderingMode != UglyToad.PdfPig.Core.TextRenderingMode.Neither &&
                    boxes.Any(area => area.Contains(x, y)) &&
                    !IsOverlay(letter.Value, request.OverlayText);
            });
        }

        return (bytes, results);
    }

    private static bool IsOverlay(string letter, string overlay)
    {
        return !string.IsNullOrEmpty(overlay) && overlay.Contains(letter, StringComparison.Ordinal);
    }

    private static int RemoveAnnotations(PdfPage page, List<PdfBox> boxes)
    {
        var annotations = PdfLowLevel.GetAnnotations(page, create: false);

        if (annotations is null)
        {
            return 0;
        }

        var removed = 0;

        for (var index = annotations.Elements.Count - 1; index >= 0; index--)
        {
            if (PdfLowLevel.Resolve(annotations.Elements[index]) is not PdfDictionary annotation ||
                PdfLowLevel.GetRectangle(annotation) is not { } rectangle)
            {
                continue;
            }

            var box = new PdfBox(rectangle.X1, rectangle.Y1, rectangle.X2, rectangle.Y2);

            if (boxes.Any(area => area.Intersects(box)))
            {
                annotations.Elements.RemoveAt(index);
                removed++;
            }
        }

        return removed;
    }

    private static void Paint(PdfPage page, List<PdfBox> boxes, PdfRedactionRequest request)
    {
        var content = new StringBuilder();
        var fill = request.Fill;

        content.Append("q ")
            .Append(PdfLowLevel.Format(fill.Red / 255d)).Append(' ')
            .Append(PdfLowLevel.Format(fill.Green / 255d)).Append(' ')
            .Append(PdfLowLevel.Format(fill.Blue / 255d)).Append(" rg ");

        foreach (var box in boxes)
        {
            content.Append(PdfLowLevel.Format(box.Left)).Append(' ').Append(PdfLowLevel.Format(box.Bottom)).Append(' ')
                .Append(PdfLowLevel.Format(box.Width)).Append(' ').Append(PdfLowLevel.Format(box.Height)).Append(" re ");
        }

        content.Append("f Q");

        if (!string.IsNullOrWhiteSpace(request.OverlayText))
        {
            EnsureHelvetica(page);

            var textColor = fill.IsLight ? "0 g" : "1 g";

            foreach (var box in boxes)
            {
                var size = Math.Clamp(box.Height * 0.6, 4, 14);
                var width = request.OverlayText.Length * size * 0.55;

                if (width > box.Width)
                {
                    continue;
                }

                content.Append(" BT /Helv ").Append(PdfLowLevel.Format(size)).Append(" Tf ").Append(textColor).Append(' ')
                    .Append(PdfLowLevel.Format(box.Left + ((box.Width - width) / 2))).Append(' ')
                    .Append(PdfLowLevel.Format(box.Bottom + ((box.Height - (size * 0.7)) / 2))).Append(" Td ")
                    .Append(PdfLowLevel.Literal(request.OverlayText)).Append(" Tj ET");
            }
        }

        PdfLowLevel.AppendIsolated(page, content.ToString());
    }

    private static void EnsureHelvetica(PdfPage page)
    {
        var fonts = PdfLowLevel.GetDictionary(page.Resources, "/Font");

        if (fonts is null)
        {
            fonts = new PdfDictionary(page.Owner);
            page.Resources.Elements["/Font"] = fonts;
        }

        if (!fonts.Elements.ContainsKey("/Helv"))
        {
            fonts.Elements["/Helv"] = PdfLowLevel.Helvetica(page.Owner).Reference;
        }
    }
}
