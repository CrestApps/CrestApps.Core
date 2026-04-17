namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Configures the default local file-system storage used for uploaded AI documents.
/// </summary>
public sealed class DocumentFileSystemFileStoreOptions
{
    /// <summary>
    /// Gets or sets the base directory where uploaded AI documents are stored.
    /// Relative paths are resolved from the host content root. When not set,
    /// uploads are stored under <c>App_Data\Documents</c>.
    /// </summary>
    public string BasePath { get; set; }
}
