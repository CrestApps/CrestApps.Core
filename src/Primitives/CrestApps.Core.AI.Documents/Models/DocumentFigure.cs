namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// One figure recovered from an uploaded document, and where its bytes were stored.
/// </summary>
public sealed class DocumentFigure
{
    /// <summary>
    /// Gets or sets the figure identifier, which is stable across repeated reads of the same file.
    /// </summary>
    public string FigureId { get; set; }

    /// <summary>
    /// Gets or sets the page the figure was printed on.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the caption printed with the figure, when it had one.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets the relative path the figure bytes were stored at.
    /// </summary>
    public string StoragePath { get; set; }

    /// <summary>
    /// Gets or sets the media type of the stored bytes.
    /// </summary>
    public string MediaType { get; set; }
}
