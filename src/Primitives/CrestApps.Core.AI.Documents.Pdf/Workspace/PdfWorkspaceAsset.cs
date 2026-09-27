namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// A picture kept in the workspace so a composed document can place it, such as an image read out of an
/// uploaded PDF or one supplied inline.
/// </summary>
internal sealed class PdfWorkspaceAsset
{
    /// <summary>
    /// Gets or sets the identifier the asset is referred to by, as <c>asset:{Id}</c>.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    public string MediaType { get; set; }

    /// <summary>
    /// Gets or sets where the bytes are stored.
    /// </summary>
    public string BlobPath { get; set; }

    /// <summary>
    /// Gets or sets the size, in bytes.
    /// </summary>
    public long ByteLength { get; set; }

    /// <summary>
    /// Gets or sets what the asset is, for example "Image 2 on page 3 of report.pdf".
    /// </summary>
    public string Description { get; set; }
}
