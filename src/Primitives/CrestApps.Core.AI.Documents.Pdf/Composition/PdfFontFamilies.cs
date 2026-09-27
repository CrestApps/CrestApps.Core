namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Maps the font names people ask for onto families the PDF renderer can resolve.
/// </summary>
/// <remarks>
/// The renderer throws when asked for a family the host does not have, and a model will happily ask for
/// "Helvetica Neue" or "sans-serif". Generic names and common aliases are therefore mapped to installed
/// equivalents, and anything outside the list falls back to the default with a warning rather than failing
/// the whole document.
/// </remarks>
internal static class PdfFontFamilies
{
    /// <summary>
    /// The family used when nothing else is asked for or what was asked for is unavailable.
    /// </summary>
    public const string Default = "Arial";

    /// <summary>
    /// The family code blocks are set in.
    /// </summary>
    public const string Monospace = "Courier New";

    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sans-serif"] = "Arial",
        ["sans serif"] = "Arial",
        ["sans"] = "Arial",
        ["helvetica"] = "Arial",
        ["helvetica neue"] = "Arial",
        ["inter"] = "Arial",
        ["roboto"] = "Arial",
        ["open sans"] = "Arial",
        ["system-ui"] = "Segoe UI",
        ["serif"] = "Times New Roman",
        ["times"] = "Times New Roman",
        ["times roman"] = "Times New Roman",
        ["garamond"] = "Georgia",
        ["monospace"] = "Courier New",
        ["mono"] = "Courier New",
        ["courier"] = "Courier New",
        ["consolas"] = "Consolas",
    };

    private static readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase)
    {
        "Arial",
        "Times New Roman",
        "Courier New",
        "Georgia",
        "Verdana",
        "Tahoma",
        "Trebuchet MS",
        "Calibri",
        "Cambria",
        "Candara",
        "Segoe UI",
        "Consolas",
        "Palatino Linotype",
        "Book Antiqua",
        "Century Gothic",
        "Lucida Console",
    };

    /// <summary>
    /// Gets the families that can be asked for by name.
    /// </summary>
    public static IEnumerable<string> Known => _known;

    /// <summary>
    /// Resolves a requested family.
    /// </summary>
    /// <param name="requested">The family asked for, or <see langword="null"/>.</param>
    /// <param name="fallback">The family used when nothing usable was asked for.</param>
    /// <param name="warnings">Receives a note when the request could not be honoured.</param>
    /// <returns>The family to render with.</returns>
    public static string Resolve(string requested, string fallback, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return fallback;
        }

        var name = requested.Trim().Trim('"', '\'');

        // A CSS stack names several; the first one the renderer knows wins.
        foreach (var candidate in name.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var unquoted = candidate.Trim('"', '\'');

            if (_known.Contains(unquoted))
            {
                return _known.First(known => string.Equals(known, unquoted, StringComparison.OrdinalIgnoreCase));
            }

            if (_aliases.TryGetValue(unquoted, out var alias))
            {
                return alias;
            }
        }

        warnings?.Add($"The font \"{requested}\" is not available, so {fallback} was used. Available fonts: {string.Join(", ", _known)}.");

        return fallback;
    }
}
