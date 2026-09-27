using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Checks where content sits against the page edges: content drawn outside the visible page, and content so
/// close to an edge that printing may cut it.
/// </summary>
internal static class PdfOverflowChecks
{
    private const int MaxListed = 5;

    // A picture or a path covering at least this share of the page, or spanning nearly its whole width or
    // height, is a background or a band that is meant to run to the edge.
    private const double BackgroundShare = 0.5;
    private const double SpanShare = 0.95;

    /// <summary>
    /// Runs the overflow checks.
    /// </summary>
    /// <param name="checks">The report to add to.</param>
    /// <param name="pages">The inspected pages.</param>
    /// <param name="minMarginPoints">The smallest distance from an edge content may keep, in points.</param>
    public static void Run(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages, double minMarginPoints)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(pages);

        var outside = new List<string>();
        var outsidePages = new List<int>();
        var near = new List<string>();
        var nearPages = new List<int>();

        foreach (var page in pages)
        {
            var items = Collect(page);
            var pageOutside = new List<string>();
            var hiddenWords = new List<string>();
            (double Distance, string Edge, string What)? closest = null;

            foreach (var (box, what) in items)
            {
                if (!box.Intersects(page.Visible))
                {
                    if (what.StartsWith("text ", StringComparison.Ordinal))
                    {
                        hiddenWords.Add(what["text ".Length..]);
                    }
                    else
                    {
                        pageOutside.Add(what + " lies entirely outside the visible page, so it is never seen");
                    }

                    continue;
                }

                var beyond = Beyond(box, page.Visible);

                // Text crossing the edge is reported by the rendering checks; pictures and graphics are reported here.
                if (beyond.Distance > 1 && !what.StartsWith("text", StringComparison.Ordinal))
                {
                    pageOutside.Add(string.Create(CultureInfo.InvariantCulture, $"{what} extends {PdfCheckList.Millimetres(beyond.Distance)} past the {EdgeName(beyond.Edge, page.Page)} edge"));

                    continue;
                }

                if (beyond.Distance > 0)
                {
                    continue;
                }

                var inside = Inside(box, page.Visible);

                if (inside.Distance < minMarginPoints && (closest is null || inside.Distance < closest.Value.Distance))
                {
                    closest = (inside.Distance, inside.Edge, what);
                }
            }

            if (hiddenWords.Count > 0)
            {
                pageOutside.Insert(0, "text " + PdfCheckList.List(hiddenWords, 4).Replace("; ", ", ", StringComparison.Ordinal) + " lies entirely outside the visible page, so it is never seen");
            }

            if (pageOutside.Count > 0)
            {
                outsidePages.Add(page.Number);
                outside.Add(string.Create(CultureInfo.InvariantCulture, $"page {page.Number}: {PdfCheckList.List(pageOutside, 2)}"));
            }

            if (closest is { } nearest)
            {
                nearPages.Add(page.Number);
                near.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"page {page.Number}: {nearest.What} is {PdfCheckList.Millimetres(Math.Max(0, nearest.Distance))} from the {EdgeName(nearest.Edge, page.Page)} edge"));
            }
        }

        if (outsidePages.Count == 0)
        {
            checks.Pass("Content outside the page", "Nothing is drawn outside the visible page, apart from backgrounds meant to run to the edge.");
        }
        else
        {
            checks.Warn(
                "Content outside the page",
                "Content is placed beyond the visible page: " + PdfCheckList.List(outside, MaxListed) + ".",
                "Move or resize it; a picture meant to run to the edge (a bleed) is fine when the page will be trimmed. If the page was cropped too tightly, widen the crop with edit_pdf_pages.");
        }

        var limit = PdfCheckList.Millimetres(minMarginPoints);

        if (nearPages.Count == 0)
        {
            checks.Pass("Content near the edge", $"All content keeps at least {limit} from the page edges.");
        }
        else
        {
            checks.Warn(
                "Content near the edge",
                $"Content comes closer than {limit} to a page edge, where printers may cut it off: " + PdfCheckList.List(near, MaxListed) + ".",
                "Widen the margins (for a composed document, the page setup margins), or move the content inwards.");
        }
    }

    /// <summary>
    /// Returns whether a box is a background or a band meant to run to the edge of the page.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="page">The page.</param>
    /// <returns><see langword="true"/> for a background.</returns>
    public static bool IsBackground(PdfBox box, PdfPageInspection page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return box.Width * box.Height >= page.Area * BackgroundShare ||
            box.Width >= page.Visible.Width * SpanShare ||
            box.Height >= page.Visible.Height * SpanShare;
    }

    /// <summary>
    /// Names a page edge the way the page is shown, taking its rotation into account.
    /// </summary>
    /// <param name="edge">The edge in the page's own coordinates: <c>left</c>, <c>right</c>, <c>top</c> or <c>bottom</c>.</param>
    /// <param name="page">The page.</param>
    /// <returns>The edge as shown.</returns>
    public static string EdgeName(string edge, Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var rotation = (((int)page.Rotation.Value % 360) + 360) % 360;
        string[] order = ["top", "right", "bottom", "left"];
        var index = Array.IndexOf(order, edge);

        if (index < 0)
        {
            return edge;
        }

        return order[(index + (rotation / 90)) % 4];
    }

    private static List<(PdfBox Box, string What)> Collect(PdfPageInspection page)
    {
        var items = new List<(PdfBox Box, string What)>();

        // Words read better than letters in the findings.
        if (page.VisibleLetters.Count > 0)
        {
            foreach (var word in PdfPageText.GetWords(page.Page))
            {
                if (string.IsNullOrWhiteSpace(word.Text) ||
                    word.Letters.All(letter => letter.RenderingMode is TextRenderingMode.Neither or TextRenderingMode.NeitherClip))
                {
                    continue;
                }

                items.Add((PdfBox.From(word.BoundingBox), "text " + PdfCheckList.Quote(word.Text)));
            }
        }

        foreach (var image in page.Images)
        {
            if (!IsBackground(image, page))
            {
                items.Add((image, string.Create(CultureInfo.InvariantCulture, $"a picture at {image.Describe(page.Page)}")));
            }
        }

        foreach (var path in page.Paths)
        {
            if (path.IsVisible && !IsBackground(path.Box, page) && (path.Box.Width > 0.5 || path.Box.Height > 0.5))
            {
                items.Add((path.Box, string.Create(CultureInfo.InvariantCulture, $"a graphic at {path.Box.Describe(page.Page)}")));
            }
        }

        return items;
    }

    private static (double Distance, string Edge) Beyond(PdfBox box, PdfBox visible)
    {
        var candidates = new (double Distance, string Edge)[]
        {
            (visible.Left - box.Left, "left"),
            (box.Right - visible.Right, "right"),
            (box.Top - visible.Top, "top"),
            (visible.Bottom - box.Bottom, "bottom"),
        };

        return candidates.MaxBy(candidate => candidate.Distance);
    }

    private static (double Distance, string Edge) Inside(PdfBox box, PdfBox visible)
    {
        var candidates = new (double Distance, string Edge)[]
        {
            (box.Left - visible.Left, "left"),
            (visible.Right - box.Right, "right"),
            (visible.Top - box.Top, "top"),
            (box.Bottom - visible.Bottom, "bottom"),
        };

        return candidates.MinBy(candidate => candidate.Distance);
    }
}
