using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// An optional content group of a PDF — what viewers list as a layer.
/// </summary>
internal sealed class PdfLayer
{
    /// <summary>
    /// Gets or sets the name viewers show.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the layer is shown when the document opens.
    /// </summary>
    public bool Visible { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether viewers keep the reader from changing the layer's visibility.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Gets or sets the layer's intent, such as <c>View</c> or <c>Design</c>.
    /// </summary>
    public string Intent { get; set; }

    /// <summary>
    /// Gets the one-based pages whose resources refer to the layer.
    /// </summary>
    public List<int> Pages { get; } = [];

    /// <summary>
    /// Gets or sets the optional content group dictionary.
    /// </summary>
    public PdfDictionary Group { get; set; }
}
