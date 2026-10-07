namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// An image placed in the flow of a composed document.
/// </summary>
internal sealed class PdfImageDefinition
{
    /// <summary>
    /// Gets or sets where the picture comes from: an uploaded image's file name or document id, a workspace
    /// asset (<c>asset:…</c>), a figure of an uploaded document (<c>figure:{documentId}/{figureId}</c>) or a
    /// link this host serves a document from.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the width as a percentage of the text width.
    /// </summary>
    public double? WidthPercent { get; set; }

    /// <summary>
    /// Gets or sets the width in points.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Gets or sets the height in points. The aspect ratio is kept when only one dimension is given.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Gets or sets the caption printed under the image.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets the text read aloud in place of the image by assistive technology.
    /// </summary>
    public string AltText { get; set; }
}
