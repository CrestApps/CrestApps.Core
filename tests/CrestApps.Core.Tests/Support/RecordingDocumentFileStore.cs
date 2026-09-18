using CrestApps.Core.AI.Documents;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Keeps saved files in memory so a test can assert what was written without touching the disk.
/// </summary>
internal sealed class RecordingDocumentFileStore : IDocumentFileStore
{
    /// <summary>
    /// Gets the saved files, keyed by the path they were saved under.
    /// </summary>
    public Dictionary<string, byte[]> Saved { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Saves a file.
    /// </summary>
    /// <param name="fileName">The path to save under.</param>
    /// <param name="content">The content.</param>
    /// <returns>The path the file was saved under.</returns>
    public async Task<string> SaveFileAsync(string fileName, Stream content)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);

        Saved[fileName] = buffer.ToArray();

        return fileName;
    }

    /// <summary>
    /// Opens a saved file.
    /// </summary>
    /// <param name="fileName">The path the file was saved under.</param>
    /// <returns>The content, or <see langword="null"/> when nothing was saved there.</returns>
    public Task<Stream> GetFileAsync(string fileName)
    {
        if (!Saved.TryGetValue(fileName, out var content))
        {
            return Task.FromResult<Stream>(null);
        }

        return Task.FromResult<Stream>(new MemoryStream(content));
    }

    /// <summary>
    /// Removes a saved file.
    /// </summary>
    /// <param name="fileName">The path the file was saved under.</param>
    /// <returns><see langword="true"/> when a file was removed.</returns>
    public Task<bool> DeleteFileAsync(string fileName)
    {
        return Task.FromResult(Saved.Remove(fileName));
    }
}
