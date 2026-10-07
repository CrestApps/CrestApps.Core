using System.Globalization;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A PDF standard a file can be checked against: a part and conformance level of PDF/A, or a part of PDF/UA.
/// </summary>
/// <param name="IsArchive">Whether the standard is PDF/A; otherwise it is PDF/UA.</param>
/// <param name="Part">The part, for example 2 for PDF/A-2.</param>
/// <param name="Conformance">The PDF/A conformance level (<c>A</c>, <c>B</c> or <c>U</c>), or <see langword="null"/> for PDF/UA.</param>
internal sealed partial record PdfStandard(bool IsArchive, int Part, string Conformance)
{
    /// <summary>
    /// Gets the standard's name, for example <c>PDF/A-2b</c> or <c>PDF/UA-1</c>.
    /// </summary>
    public string Name => IsArchive
        ? string.Create(CultureInfo.InvariantCulture, $"PDF/A-{Part}{Conformance?.ToLowerInvariant()}")
        : string.Create(CultureInfo.InvariantCulture, $"PDF/UA-{Part}");

    /// <summary>
    /// Reads a standard as a person or a model writes it: <c>pdfa-2b</c>, <c>PDF/A-2b</c>, <c>pdfua-1</c>, <c>PDF/UA</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="standard">The standard.</param>
    /// <returns><see langword="true"/> when the text names a standard this checker knows.</returns>
    public static bool TryParse(string text, out PdfStandard standard)
    {
        standard = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var compact = text.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("/", string.Empty, StringComparison.Ordinal).Replace("_", "-", StringComparison.Ordinal);
        var archive = Archive().Match(compact);

        if (archive.Success)
        {
            var part = int.Parse(archive.Groups[1].Value, CultureInfo.InvariantCulture);
            var level = archive.Groups[2].Success
                ? archive.Groups[2].Value.ToUpperInvariant()
                : "B";

            if (part is < 1 or > 3 || (part == 1 && level == "U"))
            {
                return false;
            }

            standard = new PdfStandard(true, part, level);

            return true;
        }

        if (Universal().IsMatch(compact))
        {
            standard = new PdfStandard(false, 1, null);

            return true;
        }

        return false;
    }

    [GeneratedRegex("^pdfa-?([1-4])([abu])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Archive();

    [GeneratedRegex("^pdfua(-?1)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Universal();
}
