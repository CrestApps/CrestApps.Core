using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// The paper sizes a document can be laid out on, and the section properties that set one up.
/// </summary>
internal static class WordPageSizes
{
    private static readonly Dictionary<string, (double Width, double Height)> _sizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["letter"] = (612, 792),
        ["legal"] = (612, 1008),
        ["tabloid"] = (792, 1224),
        ["ledger"] = (1224, 792),
        ["executive"] = (522, 756),
        ["statement"] = (396, 612),
        ["a3"] = (841.9, 1190.55),
        ["a4"] = (595.3, 841.9),
        ["a5"] = (419.55, 595.3),
        ["a6"] = (297.65, 419.55),
        ["b4"] = (708.65, 1000.6),
        ["b5"] = (498.9, 708.65),
    };

    /// <summary>
    /// Gets the names of the known paper sizes.
    /// </summary>
    public static IEnumerable<string> Names => _sizes.Keys;

    /// <summary>
    /// Looks up a paper size in portrait orientation.
    /// </summary>
    /// <param name="name">The size name, such as <c>letter</c> or <c>A4</c>.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <returns><see langword="true"/> when the size is known.</returns>
    public static bool TryGet(string name, out double width, out double height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(name) || !_sizes.TryGetValue(name.Trim().Replace(" ", string.Empty, StringComparison.Ordinal), out var size))
        {
            return false;
        }

        (width, height) = size;

        return true;
    }

    /// <summary>
    /// Names a page size, or describes it in inches when it has no name.
    /// </summary>
    /// <param name="widthPoints">The page width in points.</param>
    /// <param name="heightPoints">The page height in points.</param>
    /// <returns>The description, such as <c>Letter portrait</c>.</returns>
    public static string Describe(double widthPoints, double heightPoints)
    {
        var landscape = widthPoints > heightPoints;
        var shortSide = Math.Min(widthPoints, heightPoints);
        var longSide = Math.Max(widthPoints, heightPoints);

        foreach (var (name, size) in _sizes)
        {
            if (Math.Abs(size.Width - shortSide) < 2 && Math.Abs(size.Height - longSide) < 2 && !string.Equals(name, "ledger", StringComparison.Ordinal))
            {
                var display = name.Length <= 2 ? name.ToUpperInvariant() : char.ToUpperInvariant(name[0]) + name[1..];

                return display + (landscape ? " landscape" : " portrait");
            }
        }

        return FormattableString.Invariant($"{widthPoints / WordUnits.PointsPerInch:0.##} x {heightPoints / WordUnits.PointsPerInch:0.##} in");
    }

    /// <summary>
    /// Builds the properties of a section laid out on a named paper size with even margins.
    /// </summary>
    /// <param name="size">The paper size name; an unknown name gives letter.</param>
    /// <param name="landscape">Whether the page is turned to landscape.</param>
    /// <param name="marginPoints">The margin on every side, in points.</param>
    /// <returns>The section properties.</returns>
    public static SectionProperties CreateSection(string size, bool landscape, double marginPoints)
    {
        if (!TryGet(size, out var width, out var height))
        {
            (width, height) = _sizes["letter"];
        }

        var section = new SectionProperties();

        SetPageSize(section, width, height, landscape);
        SetMargins(section, marginPoints, marginPoints, marginPoints, marginPoints, 36, 36);

        return section;
    }

    /// <summary>
    /// Sets a section's page size and orientation.
    /// </summary>
    /// <param name="section">The section properties.</param>
    /// <param name="width">The portrait width in points.</param>
    /// <param name="height">The portrait height in points.</param>
    /// <param name="landscape">Whether the page is turned to landscape.</param>
    public static void SetPageSize(SectionProperties section, double width, double height, bool landscape)
    {
        ArgumentNullException.ThrowIfNull(section);

        var shortSide = Math.Min(width, height);
        var longSide = Math.Max(width, height);

        var pageSize = new PageSize
        {
            Width = (UInt32Value)(uint)WordUnits.ToTwips(landscape ? longSide : shortSide),
            Height = (UInt32Value)(uint)WordUnits.ToTwips(landscape ? shortSide : longSide),
        };

        if (landscape)
        {
            pageSize.Orient = PageOrientationValues.Landscape;
        }

        WordSchemaOrder.Set(section, pageSize);
    }

    /// <summary>
    /// Sets a section's margins.
    /// </summary>
    /// <param name="section">The section properties.</param>
    /// <param name="top">The top margin in points.</param>
    /// <param name="right">The right margin in points.</param>
    /// <param name="bottom">The bottom margin in points.</param>
    /// <param name="left">The left margin in points.</param>
    /// <param name="header">The distance of the header from the top edge, in points.</param>
    /// <param name="footer">The distance of the footer from the bottom edge, in points.</param>
    public static void SetMargins(SectionProperties section, double top, double right, double bottom, double left, double header, double footer)
    {
        ArgumentNullException.ThrowIfNull(section);

        var gutter = section.GetFirstChild<PageMargin>()?.Gutter?.Value ?? 0U;

        WordSchemaOrder.Set(section, new PageMargin
        {
            Top = WordUnits.ToTwips(top),
            Right = (UInt32Value)(uint)WordUnits.ToTwips(right),
            Bottom = WordUnits.ToTwips(bottom),
            Left = (UInt32Value)(uint)WordUnits.ToTwips(left),
            Header = (UInt32Value)(uint)WordUnits.ToTwips(header),
            Footer = (UInt32Value)(uint)WordUnits.ToTwips(footer),
            Gutter = gutter,
        });
    }
}
