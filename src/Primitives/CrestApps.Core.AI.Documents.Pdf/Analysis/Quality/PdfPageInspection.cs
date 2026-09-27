using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A path a page draws, reduced to what the quality checks need.
/// </summary>
/// <param name="Box">Its extent.</param>
/// <param name="IsFilled">Whether it is filled.</param>
/// <param name="FillLuma">The lightness of its fill, from 0 (black) to 1 (white).</param>
/// <param name="IsStroked">Whether it is stroked.</param>
/// <param name="StrokeLuma">The lightness of its stroke.</param>
internal readonly record struct PdfPagePath(PdfBox Box, bool IsFilled, double FillLuma, bool IsStroked, double StrokeLuma)
{
    /// <summary>
    /// Gets a value indicating whether the path leaves a mark a reader sees on white paper.
    /// </summary>
    public bool IsVisible => (IsFilled && FillLuma < 0.97) || (IsStroked && StrokeLuma < 0.97);
}

/// <summary>
/// What one page draws — its letters, pictures and paths — read once with PdfPig for every check that looks
/// at the page's appearance.
/// </summary>
internal sealed partial class PdfPageInspection
{
    private PdfPageInspection(Page page)
    {
        Page = page;
        Number = page.Number;
        Visible = PdfBox.VisibleArea(page);
    }

    /// <summary>
    /// Gets the page.
    /// </summary>
    public Page Page { get; }

    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Gets the page's visible area.
    /// </summary>
    public PdfBox Visible { get; }

    /// <summary>
    /// Gets the letters that leave ink, whitespace excluded.
    /// </summary>
    public List<Letter> VisibleLetters { get; } = [];

    /// <summary>
    /// Gets the letters drawn invisibly (text rendering mode 3), the way an OCR text layer is.
    /// </summary>
    public List<Letter> InvisibleLetters { get; } = [];

    /// <summary>
    /// Gets where the page's pictures are placed.
    /// </summary>
    public List<PdfBox> Images { get; } = [];

    /// <summary>
    /// Gets the page's paths, clipping paths excluded.
    /// </summary>
    public List<PdfPagePath> Paths { get; } = [];

    /// <summary>
    /// Gets what could not be read from the page, or <see langword="null"/>.
    /// </summary>
    public string Error { get; private set; }

    /// <summary>
    /// Gets the number of annotations and form field widgets on the page, which a viewer draws on top of the page's own content.
    /// </summary>
    public int AnnotationCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the page shows nothing: no ink from text, pictures or paths.
    /// </summary>
    public bool IsBlank => Error is null && VisibleLetters.Count == 0 && Images.Count == 0 && !Paths.Any(path => path.IsVisible);

    /// <summary>
    /// Gets the area of the page.
    /// </summary>
    public double Area => Math.Max(1, Visible.Width * Visible.Height);

    /// <summary>
    /// Reads a page.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="number">The one-based page number.</param>
    /// <returns>The page, or <see langword="null"/> when PdfPig cannot read it at all.</returns>
    public static PdfPageInspection Inspect(PdfDocument document, int number)
    {
        ArgumentNullException.ThrowIfNull(document);

        Page page;

        try
        {
            page = document.GetPage(number);
        }
        catch (Exception)
        {
            return null;
        }

        var inspection = new PdfPageInspection(page);
        var problems = new List<string>();

        try
        {
            foreach (var letter in page.Letters)
            {
                if (string.IsNullOrWhiteSpace(letter.Value))
                {
                    continue;
                }

                if (letter.RenderingMode is TextRenderingMode.Neither or TextRenderingMode.NeitherClip)
                {
                    inspection.InvisibleLetters.Add(letter);
                }
                else
                {
                    inspection.VisibleLetters.Add(letter);
                }
            }
        }
        catch (Exception ex)
        {
            problems.Add("text: " + ex.Message);
        }

        try
        {
            foreach (var image in page.GetImages())
            {
                inspection.Images.Add(PdfBox.From(image.BoundingBox));
            }
        }
        catch (Exception ex)
        {
            problems.Add("images: " + ex.Message);
        }

        try
        {
            foreach (var path in page.Paths)
            {
                if (path.IsClipping || (!path.IsFilled && !path.IsStroked))
                {
                    continue;
                }

                var bounds = path.GetBoundingRectangle();

                if (bounds is null)
                {
                    continue;
                }

                inspection.Paths.Add(new PdfPagePath(
                    PdfBox.From(bounds.Value),
                    path.IsFilled,
                    Luma(path.FillColor),
                    path.IsStroked,
                    Luma(path.StrokeColor)));
            }
        }
        catch (Exception ex)
        {
            problems.Add("graphics: " + ex.Message);
        }

        try
        {
            inspection.AnnotationCount = page.GetAnnotations().Count();
        }
        catch (Exception)
        {
            // Annotations that cannot be read are simply not counted.
        }

        if (problems.Count > 0)
        {
            inspection.Error = string.Join("; ", problems);
        }

        return inspection;
    }

    /// <summary>
    /// Gets the lightness of a colour as it is written (gamma-encoded), from 0 (black) to 1 (white).
    /// </summary>
    /// <param name="color">The colour, or <see langword="null"/> for black.</param>
    /// <returns>The lightness.</returns>
    public static double Luma(IColor color)
    {
        if (color is null)
        {
            return 0;
        }

        try
        {
            var (red, green, blue) = color.ToRGBValues();

            return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Gets the contrast ratio of a colour against white, as WCAG defines it.
    /// </summary>
    /// <param name="color">The colour.</param>
    /// <returns>The ratio, from 1 (none) to 21 (black on white).</returns>
    public static double ContrastWithWhite(IColor color)
    {
        if (color is null)
        {
            return 21;
        }

        try
        {
            var (red, green, blue) = color.ToRGBValues();
            var luminance = (0.2126 * Linear(red)) + (0.7152 * Linear(green)) + (0.0722 * Linear(blue));

            return 1.05 / (luminance + 0.05);
        }
        catch (Exception)
        {
            return 21;
        }
    }

    /// <summary>
    /// Returns the box of a letter, the way it is placed on the page.
    /// </summary>
    /// <param name="letter">The letter.</param>
    /// <returns>The box.</returns>
    public static PdfBox Box(Letter letter)
    {
        ArgumentNullException.ThrowIfNull(letter);

        return PdfBox.From(letter.BoundingBox);
    }

    /// <summary>
    /// Reads the words whose centre lies in an area of a page, such as the text under a link.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="area">The area.</param>
    /// <param name="max">The most characters returned.</param>
    /// <returns>The words joined by single spaces, or an empty string.</returns>
    public static string TextIn(Page page, PdfBox area, int max = 120)
    {
        ArgumentNullException.ThrowIfNull(page);

        var words = PdfPageText.GetWords(page)
            .Where(word =>
            {
                if (string.IsNullOrWhiteSpace(word.Text))
                {
                    return false;
                }

                var box = PdfBox.From(word.BoundingBox);

                return area.Contains((box.Left + box.Right) / 2, (box.Bottom + box.Top) / 2);
            })
            .Select(word => word.Text.Trim());

        // Dot leaders, as a table of contents draws them, are not part of what the text says.
        var text = string.Join(' ', LeaderRun().Replace(string.Join(' ', words), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return text.Length > max
            ? text[..max].TrimEnd() + "…"
            : text;
    }

    [GeneratedRegex(@"[.\u00B7\u2026_]{3,}")]
    private static partial Regex LeaderRun();

    private static double Linear(double channel)
    {
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
