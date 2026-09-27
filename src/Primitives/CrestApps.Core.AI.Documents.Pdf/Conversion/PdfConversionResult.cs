using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Conversion;

/// <summary>
/// What a file became when it was converted into the blocks of a composed PDF.
/// </summary>
internal sealed class PdfConversionResult
{
    /// <summary>
    /// Gets or sets the title the file states, when it states one.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets the blocks, in reading order.
    /// </summary>
    public List<PdfBlockDefinition> Blocks { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the file is laid out in landscape, as slides and wide sheets are.
    /// </summary>
    public bool Landscape { get; set; }

    /// <summary>
    /// Gets what could not be converted.
    /// </summary>
    public List<string> Warnings { get; } = [];
}
