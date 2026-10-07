using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One element of a tagged PDF's structure tree.
/// </summary>
/// <param name="Dictionary">The structure element dictionary.</param>
/// <param name="Type">The element type as written, without its slash, for example <c>Figure</c>.</param>
/// <param name="StandardType">The standard type it maps to through the role map, for example <c>Figure</c> for a custom <c>Chart</c>.</param>
internal sealed record PdfTagElement(PdfDictionary Dictionary, string Type, string StandardType)
{
    /// <summary>
    /// Gets the alternative text, or <see langword="null"/>.
    /// </summary>
    public string Alt => PdfObjectReader.GetText(Dictionary, "/Alt");

    /// <summary>
    /// Gets the replacement text, or <see langword="null"/>.
    /// </summary>
    public string ActualText => PdfObjectReader.GetText(Dictionary, "/ActualText");

    /// <summary>
    /// Gets a value indicating whether the element is a heading: <c>H</c> or <c>H1</c> to <c>H6</c>.
    /// </summary>
    public bool IsHeading => StandardType is "H" or "H1" or "H2" or "H3" or "H4" or "H5" or "H6";
}
