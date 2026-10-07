using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// A document laid out into pages: what each page draws, which page each element starts and ends on, the
/// typefaces used, and the problems found while laying it out.
/// </summary>
internal sealed class WordLayout
{
    /// <summary>
    /// Gets the pages.
    /// </summary>
    public List<WordLayoutPage> Pages { get; } = [];

    /// <summary>
    /// Gets the page each block starts on, from 1.
    /// </summary>
    public Dictionary<OpenXmlElement, int> FirstPage { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets the page each block ends on, from 1.
    /// </summary>
    public Dictionary<OpenXmlElement, int> LastPage { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets the typefaces the document's text is set in.
    /// </summary>
    public HashSet<string> Fonts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the problems found while laying out.
    /// </summary>
    public List<WordLayoutIssue> Issues { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether layout stopped at the page limit before the end of the document.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// Gets the number of pages.
    /// </summary>
    public int PageCount => Pages.Count;

    /// <summary>
    /// Returns the page an element starts on: the element itself, or the block it is in.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The page, from 1, or 0 when the element was not laid out.</returns>
    public int PageOf(OpenXmlElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (FirstPage.TryGetValue(current, out var page))
            {
                return page;
            }
        }

        return 0;
    }

    /// <summary>
    /// Returns the printed page number of the page an element starts on.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The printed number, or an empty string.</returns>
    public string DisplayNumberOf(OpenXmlElement element)
    {
        var page = PageOf(element);

        return page >= 1 && page <= Pages.Count ? Pages[page - 1].DisplayNumber : string.Empty;
    }

    /// <summary>
    /// Returns the area an element covers on each page it is drawn on.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The page index and the box, per page.</returns>
    public List<(int Page, double X, double Y, double Width, double Height)> BoundsOf(OpenXmlElement element)
    {
        var bounds = new List<(int Page, double X, double Y, double Width, double Height)>();

        foreach (var page in Pages)
        {
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

            foreach (var item in page.Items)
            {
                if (!IsWithin(item.Source, element))
                {
                    continue;
                }

                var (x, y, width, height) = Box(item);

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + width);
                bottom = Math.Max(bottom, y + height);
            }

            if (left < double.MaxValue)
            {
                bounds.Add((page.Index, left, top, right - left, bottom - top));
            }
        }

        return bounds;
    }

    /// <summary>
    /// Returns the box an item covers.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The left, top, width and height.</returns>
    public static (double X, double Y, double Width, double Height) Box(WordDrawItem item)
    {
        return item switch
        {
            WordTextItem text => (text.X, text.Baseline - (text.Format.DrawnSize * 0.8), text.Width, text.Format.DrawnSize),
            WordRectItem rect => (rect.X, rect.Y, rect.Width, rect.Height),
            WordImageItem image => (image.X, image.Y, image.Width, image.Height),
            WordLineItem line => (Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1)),
            _ => (0, 0, 0, 0),
        };
    }

    private static bool IsWithin(OpenXmlElement source, OpenXmlElement element)
    {
        for (var current = source; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, element))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A problem found while laying a document out.
/// </summary>
/// <param name="Kind">The kind: <c>overflow</c>, <c>clipped</c>, <c>overlap</c>, <c>missing_picture</c>, <c>blank_page</c>…</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="Source">The block it concerns, or <see langword="null"/>.</param>
internal sealed record WordLayoutIssue(string Kind, int Page, string Message, OpenXmlElement Source);
