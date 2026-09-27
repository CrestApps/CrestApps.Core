using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Checks how pages will look: blank pages, text too small to read, invisible or nearly invisible text,
/// lines of text drawn over each other, and text cut off by the page edge.
/// </summary>
internal static class PdfRenderingChecks
{
    private const int MaxListed = 5;
    private const double TinyTextPoints = 5;
    private const double LightTextLuma = 0.93;
    private const int MaxOverlapLetters = 20_000;

    /// <summary>
    /// Runs the rendering checks.
    /// </summary>
    /// <param name="checks">The report to add to.</param>
    /// <param name="pages">The inspected pages.</param>
    public static void Run(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(pages);

        var unreadable = pages.Where(page => page.Error is not null).ToList();

        if (unreadable.Count > 0)
        {
            checks.Warn(
                "Unreadable content",
                "Part of the content could not be read, so the checks below may miss problems there: " +
                    PdfCheckList.List(unreadable.Select(page => string.Create(CultureInfo.InvariantCulture, $"page {page.Number}: {page.Error}")), MaxListed) + ".",
                "Run validate_pdf to see what is damaged.");
        }

        CheckBlankPages(checks, pages);
        CheckTinyText(checks, pages);
        CheckInvisibleText(checks, pages);
        CheckLightText(checks, pages);
        CheckOverlappingText(checks, pages);
        CheckClippedText(checks, pages);
    }

    /// <summary>
    /// Returns the letters of a page's visible text grouped into the words a reader sees, for quoting.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="letter">A letter.</param>
    /// <returns>The word the letter belongs to, or the letter itself.</returns>
    public static string WordOf(PdfPageInspection page, Letter letter)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(letter);

        foreach (var word in PdfPageText.GetWords(page.Page))
        {
            if (word.Letters.Any(candidate => ReferenceEquals(candidate, letter)))
            {
                return word.Text;
            }
        }

        return letter.Value;
    }

    private static void CheckBlankPages(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages)
    {
        var blank = pages.Where(page => page.IsBlank).Select(page => page.Number).ToList();

        if (blank.Count == 0)
        {
            checks.Pass("Blank pages", "Every page shows text, pictures or graphics.");

            return;
        }

        var finding = $"{PdfCheckList.Pages(blank, "shows", "show")} nothing: no visible text, no pictures and no visible graphics.";
        var annotated = pages.Where(page => page.IsBlank && page.AnnotationCount > 0).Select(page => page.Number).ToList();

        if (annotated.Count > 0)
        {
            finding += $" Of these, {PdfCheckList.Pages(annotated, "carries", "carry")} form fields or annotations, which viewers draw on top and may be all the page is meant to show.";
        }

        if (blank.Count == pages.Count)
        {
            finding += " If the file is a scan, its pictures may not be readable by this checker; look at it with preview_pdf.";
        }

        checks.Warn("Blank pages", finding, "If the blank pages are not intended (a blank page left by a page break, a failed render), remove them with edit_pdf_pages (operation 'delete').");
    }

    private static void CheckTinyText(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages)
    {
        var findings = new List<string>();
        var flagged = new List<int>();

        foreach (var page in pages)
        {
            var tiny = page.VisibleLetters.Where(letter => letter.PointSize > 0 && letter.PointSize < TinyTextPoints).ToList();

            if (tiny.Count == 0)
            {
                continue;
            }

            var smallest = tiny.MinBy(letter => letter.PointSize);

            flagged.Add(page.Number);
            findings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"page {page.Number}: {tiny.Count} character(s) down to {Math.Round(smallest.PointSize, 1)} pt, such as {PdfCheckList.Quote(WordOf(page, smallest))} at {PdfPageInspection.Box(smallest).Describe(page.Page)}"));
        }

        if (flagged.Count == 0)
        {
            checks.Pass("Tiny text", string.Create(CultureInfo.InvariantCulture, $"No text is smaller than {TinyTextPoints} pt."));

            return;
        }

        checks.Warn(
            "Tiny text",
            string.Create(CultureInfo.InvariantCulture, $"Text smaller than {TinyTextPoints} pt is hard or impossible to read in print: ") + PdfCheckList.List(findings, MaxListed) + ".",
            "Enlarge the text in the source; for a composed document, raise the block's font_size or the theme's font size.");
    }

    private static void CheckInvisibleText(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages)
    {
        var layers = new List<int>();
        var hidden = new List<string>();
        var hiddenPages = new List<int>();

        foreach (var page in pages)
        {
            if (page.InvisibleLetters.Count == 0)
            {
                continue;
            }

            var imageArea = page.Images.Sum(image => Math.Max(0, image.Width) * Math.Max(0, image.Height));

            if (imageArea >= page.Area * 0.5)
            {
                layers.Add(page.Number);

                continue;
            }

            hiddenPages.Add(page.Number);
            hidden.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"page {page.Number}: {page.InvisibleLetters.Count} character(s), such as {PdfCheckList.Quote(WordOf(page, page.InvisibleLetters[0]))}"));
        }

        if (layers.Count > 0)
        {
            checks.Info("Invisible text", $"{PdfCheckList.Pages(layers, "carries", "carry")} invisible text over a full-page picture: an OCR text layer, which makes a scan searchable. This is expected.");
        }

        if (hiddenPages.Count > 0)
        {
            checks.Warn(
                "Invisible text",
                "Text is drawn invisibly (text rendering mode 3) without a scanned picture beneath it, so search and copy find words the reader cannot see: " + PdfCheckList.List(hidden, MaxListed) + ". It may be an OCR layer over vector artwork, or hidden content.",
                "Check the pages with preview_pdf and extract_pdf_text; remove hidden content with redact_pdf or sanitize_pdf if it should not be there.");
        }

        if (layers.Count == 0 && hiddenPages.Count == 0)
        {
            checks.Pass("Invisible text", "No text is drawn invisibly.");
        }
    }

    private static void CheckLightText(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages)
    {
        var findings = new List<string>();
        var flagged = new List<int>();

        foreach (var page in pages)
        {
            var light = new List<Letter>();

            foreach (var letter in page.VisibleLetters)
            {
                var color = TextColor(letter);

                if (color is null || PdfPageInspection.Luma(color) <= LightTextLuma || HasBackground(page, letter))
                {
                    continue;
                }

                light.Add(letter);
            }

            if (light.Count == 0)
            {
                continue;
            }

            var sample = light[0];
            var contrast = PdfPageInspection.ContrastWithWhite(TextColor(sample));

            flagged.Add(page.Number);
            findings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"page {page.Number}: {light.Count} character(s), such as {PdfCheckList.Quote(WordOf(page, sample))} (contrast {Math.Round(contrast, 2)}:1 against white)"));
        }

        if (flagged.Count == 0)
        {
            checks.Pass("Light text", "No text is drawn in a near-white colour on an empty background.");

            return;
        }

        checks.Warn(
            "Light text",
            "Text is drawn in a colour so light it all but disappears on white paper: " + PdfCheckList.List(findings, MaxListed) + ".",
            "Darken the text colour (WCAG asks for a contrast of at least 4.5:1), or give it a dark background.");
    }

    private static IColor TextColor(Letter letter)
    {
        return letter.RenderingMode is TextRenderingMode.Stroke or TextRenderingMode.StrokeClip
            ? letter.StrokeColor
            : letter.FillColor;
    }

    private static bool HasBackground(PdfPageInspection page, Letter letter)
    {
        var box = PdfPageInspection.Box(letter);
        var x = (box.Left + box.Right) / 2;
        var y = (box.Bottom + box.Top) / 2;

        return page.Images.Any(image => image.Contains(x, y)) ||
            page.Paths.Any(path => path.IsFilled && path.FillLuma < 0.85 && path.Box.Contains(x, y));
    }

    private static void CheckOverlappingText(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages)
    {
        var findings = new List<string>();
        var flagged = new List<int>();
        var watermarkPages = new List<int>();

        foreach (var page in pages)
        {
            if (page.VisibleLetters.Any(letter => letter.TextOrientation == TextOrientation.Other))
            {
                watermarkPages.Add(page.Number);
            }

            var overlap = FindOverlap(page);

            if (overlap is null)
            {
                continue;
            }

            var (count, first, second) = overlap.Value;

            flagged.Add(page.Number);
            findings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"page {page.Number}: {PdfCheckList.Quote(WordOf(page, first))} runs into {PdfCheckList.Quote(WordOf(page, second))} at {PdfPageInspection.Box(first).Union(PdfPageInspection.Box(second)).Describe(page.Page)} ({count} overlapping character pairs)"));
        }

        if (flagged.Count == 0)
        {
            checks.Pass("Overlapping text", "No lines of text are drawn over each other.");
        }
        else
        {
            checks.Warn(
                "Overlapping text",
                "Lines of text are drawn over each other, so they are hard to read: " + PdfCheckList.List(findings, MaxListed) + ".",
                "Look at the pages with preview_pdf. In a composed document, give the text more room (less text in a fixed-height area, more line spacing or a smaller font); in another file, edit or rebuild the page.");
        }

        if (watermarkPages.Count > 0)
        {
            checks.Info("Rotated text", $"{PdfCheckList.Pages(watermarkPages, "carries", "carry")} text drawn at an angle, such as a watermark; it is not counted as overlapping.");
        }
    }

    private static (int Count, Letter First, Letter Second)? FindOverlap(PdfPageInspection page)
    {
        var letters = page.VisibleLetters
            .Where(letter => letter.TextOrientation != TextOrientation.Other && letter.Value.Length > 0 && char.IsLetterOrDigit(letter.Value[0]))
            .Take(MaxOverlapLetters)
            .ToList();

        if (letters.Count < 2)
        {
            return null;
        }

        const double Cell = 24;
        var boxes = letters.Select(PdfPageInspection.Box).ToList();
        var grid = new Dictionary<(int, int), List<int>>();

        for (var index = 0; index < letters.Count; index++)
        {
            var box = boxes[index];

            for (var column = (int)Math.Floor(box.Left / Cell); column <= (int)Math.Floor(box.Right / Cell); column++)
            {
                for (var row = (int)Math.Floor(box.Bottom / Cell); row <= (int)Math.Floor(box.Top / Cell); row++)
                {
                    if (!grid.TryGetValue((column, row), out var cell))
                    {
                        cell = [];
                        grid[(column, row)] = cell;
                    }

                    cell.Add(index);
                }
            }
        }

        var pairs = new HashSet<long>();
        (int First, int Second)? example = null;

        foreach (var cell in grid.Values)
        {
            for (var i = 0; i < cell.Count; i++)
            {
                for (var j = i + 1; j < cell.Count; j++)
                {
                    var a = cell[i];
                    var b = cell[j];

                    if (!Collide(letters[a], letters[b], boxes[a], boxes[b]))
                    {
                        continue;
                    }

                    var key = ((long)Math.Min(a, b) * letters.Count) + Math.Max(a, b);

                    if (pairs.Add(key))
                    {
                        example ??= (a, b);
                    }
                }
            }
        }

        // A single pair is as likely an accent or a ligature drawn apart as a real collision.
        if (pairs.Count < 2 || example is null)
        {
            return null;
        }

        return (pairs.Count, letters[example.Value.First], letters[example.Value.Second]);
    }

    private static bool Collide(Letter first, Letter second, PdfBox firstBox, PdfBox secondBox)
    {
        if (first.TextOrientation != second.TextOrientation || !firstBox.Intersects(secondBox))
        {
            return false;
        }

        var vertical = first.TextOrientation is TextOrientation.Rotate90 or TextOrientation.Rotate270;
        var baselineGap = vertical
            ? Math.Abs(first.StartBaseLine.X - second.StartBaseLine.X)
            : Math.Abs(first.StartBaseLine.Y - second.StartBaseLine.Y);

        // Letters of one line touch by design; only letters of different lines colliding are a problem.
        if (baselineGap < 0.3 * Math.Max(first.PointSize, second.PointSize))
        {
            return false;
        }

        var width = Math.Min(firstBox.Right, secondBox.Right) - Math.Max(firstBox.Left, secondBox.Left);
        var height = Math.Min(firstBox.Top, secondBox.Top) - Math.Max(firstBox.Bottom, secondBox.Bottom);
        var smaller = Math.Min(firstBox.Width * firstBox.Height, secondBox.Width * secondBox.Height);

        return smaller > 0 && width * height >= 0.25 * smaller;
    }

    private static void CheckClippedText(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages)
    {
        var findings = new List<string>();
        var flagged = new List<int>();

        foreach (var page in pages)
        {
            var cut = page.VisibleLetters
                .Where(letter =>
                {
                    var box = PdfPageInspection.Box(letter);

                    return box.Intersects(page.Visible) &&
                        (box.Left < page.Visible.Left - 0.5 || box.Right > page.Visible.Right + 0.5 || box.Bottom < page.Visible.Bottom - 0.5 || box.Top > page.Visible.Top + 0.5);
                })
                .ToList();

            if (cut.Count == 0)
            {
                continue;
            }

            var finding = string.Create(
                CultureInfo.InvariantCulture,
                $"page {page.Number}: {PdfCheckList.Quote(WordOf(page, cut[0]))} at {PdfPageInspection.Box(cut[0]).Describe(page.Page)}");

            if (cut.Count > 1)
            {
                finding += string.Create(CultureInfo.InvariantCulture, $" and {cut.Count - 1} more character(s)");
            }

            flagged.Add(page.Number);
            findings.Add(finding);
        }

        if (flagged.Count == 0)
        {
            checks.Pass("Text cut by the page edge", "No text crosses the edge of the visible page.");

            return;
        }

        checks.Warn(
            "Text cut by the page edge",
            "Text runs over the edge of the visible page, so part of it is cut off: " + PdfCheckList.List(findings, MaxListed) + ".",
            "Move or shrink the text; if the page was cropped, widen the crop with edit_pdf_pages.");
    }
}
