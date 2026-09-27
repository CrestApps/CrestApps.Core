namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Paper sizes and the unit conversions page setup is written in.
/// </summary>
internal static class PdfPageSizes
{
    /// <summary>
    /// The number of points in a millimetre.
    /// </summary>
    public const double PointsPerMillimetre = 72d / 25.4d;

    private static readonly Dictionary<string, (double Width, double Height)> _sizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A3"] = (841.89, 1190.55),
        ["A4"] = (595.28, 841.89),
        ["A5"] = (419.53, 595.28),
        ["A6"] = (297.64, 419.53),
        ["B5"] = (498.90, 708.66),
        ["Letter"] = (612, 792),
        ["Legal"] = (612, 1008),
        ["Tabloid"] = (792, 1224),
        ["Ledger"] = (1224, 792),
        ["Executive"] = (522, 756),
    };

    /// <summary>
    /// Gets the names of the paper sizes that can be asked for.
    /// </summary>
    public static IEnumerable<string> Names => _sizes.Keys;

    /// <summary>
    /// Looks up a named paper size, in portrait.
    /// </summary>
    /// <param name="name">The size name, for example <c>A4</c> or <c>Letter</c>.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <returns><see langword="true"/> when the size is known.</returns>
    public static bool TryGet(string name, out double width, out double height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = name.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (string.Equals(key, "USLetter", StringComparison.OrdinalIgnoreCase))
        {
            key = "Letter";
        }

        if (!_sizes.TryGetValue(key, out var size))
        {
            return false;
        }

        (width, height) = size;

        return true;
    }

    /// <summary>
    /// Converts millimetres to points.
    /// </summary>
    /// <param name="millimetres">The length in millimetres.</param>
    /// <returns>The length in points.</returns>
    public static double FromMillimetres(double millimetres)
    {
        return millimetres * PointsPerMillimetre;
    }

    /// <summary>
    /// Converts points to millimetres.
    /// </summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The length in millimetres.</returns>
    public static double ToMillimetres(double points)
    {
        return points / PointsPerMillimetre;
    }

    /// <summary>
    /// Names the paper a page is printed on when it matches a known size in either orientation.
    /// </summary>
    /// <param name="width">The page width in points.</param>
    /// <param name="height">The page height in points.</param>
    /// <returns>A description such as <c>A4 portrait</c>, or the dimensions in millimetres.</returns>
    public static string Describe(double width, double height)
    {
        foreach (var (name, size) in _sizes)
        {
            if (Math.Abs(size.Width - width) < 2 && Math.Abs(size.Height - height) < 2)
            {
                return name + " portrait";
            }

            if (Math.Abs(size.Height - width) < 2 && Math.Abs(size.Width - height) < 2 && !string.Equals(name, "Ledger", StringComparison.Ordinal))
            {
                return name + " landscape";
            }
        }

        return FormattableString.Invariant($"{Math.Round(ToMillimetres(width))} × {Math.Round(ToMillimetres(height))} mm");
    }
}
