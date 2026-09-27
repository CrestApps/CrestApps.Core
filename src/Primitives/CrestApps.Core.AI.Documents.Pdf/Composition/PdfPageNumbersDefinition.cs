namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// How the pages of a composed document are numbered.
/// </summary>
internal sealed class PdfPageNumbersDefinition
{
    /// <summary>
    /// Gets or sets a value indicating whether page numbers are printed.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets where the number is printed: <c>footer-left</c>, <c>footer-center</c>,
    /// <c>footer-right</c>, <c>header-left</c>, <c>header-center</c> or <c>header-right</c>.
    /// </summary>
    public string Position { get; set; }

    /// <summary>
    /// Gets or sets the numbering style: <c>1</c> (arabic), <c>i</c>, <c>I</c>, <c>a</c> or <c>A</c>.
    /// </summary>
    public string Format { get; set; }

    /// <summary>
    /// Gets or sets the text the number is printed in, for example <c>Page {page} of {pages}</c>.
    /// </summary>
    public string Template { get; set; }

    /// <summary>
    /// Gets or sets the number the first body page carries.
    /// </summary>
    public int? StartAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the first body page is left unnumbered.
    /// </summary>
    public bool? SkipFirstPage { get; set; }
}
