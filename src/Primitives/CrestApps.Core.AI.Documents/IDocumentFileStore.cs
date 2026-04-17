namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Stores uploaded AI documents. Replace this service to route document files to custom storage such as Azure Blob Storage.
/// </summary>
public interface IDocumentFileStore
{
    /// <summary>
    /// Saves a file stream under the provided logical file name or relative path.
    /// </summary>
    Task<string> SaveFileAsync(string fileName, Stream content);

    /// <summary>
    /// Opens a stored file for reading.
    /// </summary>
    Task<Stream> GetFileAsync(string fileName);

    /// <summary>
    /// Deletes a stored file.
    /// </summary>
    Task<bool> DeleteFileAsync(string fileName);
}
