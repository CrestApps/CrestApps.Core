namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// Everything the PDF agent keeps for one conversation between turns.
/// </summary>
internal sealed class PdfWorkspaceState
{
    /// <summary>
    /// Gets or sets the working documents, in the order they were created.
    /// </summary>
    public List<PdfWorkingDocument> Documents { get; set; } = [];

    /// <summary>
    /// Gets or sets the pictures kept for composed documents to place.
    /// </summary>
    public List<PdfWorkspaceAsset> Assets { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the document tools act on when they are not told which.
    /// </summary>
    public string ActiveDocument { get; set; }

    /// <summary>
    /// Gets or sets the number the next asset is identified by.
    /// </summary>
    public int NextAssetNumber { get; set; } = 1;

    /// <summary>
    /// Finds a working document by name, ignoring case.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The document, or <see langword="null"/>.</returns>
    public PdfWorkingDocument Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();

        return Documents.FirstOrDefault(document => string.Equals(document.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds an asset by identifier, with or without its <c>asset:</c> prefix.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The asset, or <see langword="null"/>.</returns>
    public PdfWorkspaceAsset FindAsset(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var trimmed = id.Trim();

        if (trimmed.StartsWith("asset:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["asset:".Length..];
        }

        return Assets.FirstOrDefault(asset => string.Equals(asset.Id, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
