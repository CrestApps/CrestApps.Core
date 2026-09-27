using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// What a redaction removes, and how the removed areas are marked.
/// </summary>
internal sealed class PdfRedactionRequest
{
    /// <summary>
    /// Gets or sets the phrases removed wherever they appear.
    /// </summary>
    public List<string> Texts { get; set; } = [];

    /// <summary>
    /// Gets or sets the regular expressions whose matches are removed.
    /// </summary>
    public List<string> Patterns { get; set; } = [];

    /// <summary>
    /// Gets or sets the kinds of value removed, as <see cref="PdfPatternLibrary"/> names them.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets explicit areas removed, by page, in user space.
    /// </summary>
    public List<(int Page, PdfBox Box)> Areas { get; set; } = [];

    /// <summary>
    /// Gets or sets pages removed entirely.
    /// </summary>
    public List<int> WholePages { get; set; } = [];

    /// <summary>
    /// Gets or sets the pages searched, or <see langword="null"/> for all.
    /// </summary>
    public HashSet<int> Pages { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether phrases match case.
    /// </summary>
    public bool MatchCase { get; set; }

    /// <summary>
    /// Gets or sets the colour the removed areas are filled with.
    /// </summary>
    public PdfColor Fill { get; set; } = new(0, 0, 0);

    /// <summary>
    /// Gets or sets a value indicating whether the removed areas are filled at all.
    /// </summary>
    public bool FillAreas { get; set; } = true;

    /// <summary>
    /// Gets or sets text printed in each filled area, such as <c>REDACTED</c>.
    /// </summary>
    public string OverlayText { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether annotations and form fields over a removed area are removed.
    /// </summary>
    public bool RemoveAnnotations { get; set; } = true;
}
