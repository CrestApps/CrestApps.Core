namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// Keeps each conversation's PDF workspace — its working documents, their files and its pictures — between
/// turns.
/// </summary>
internal interface IPdfWorkspaceStore
{
    /// <summary>
    /// Loads a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The workspace, empty when the conversation has none yet.</returns>
    Task<PdfWorkspaceState> LoadAsync(PdfWorkspaceScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="state">The workspace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveAsync(PdfWorkspaceScope scope, PdfWorkspaceState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a stored file.
    /// </summary>
    /// <param name="path">The path the file was written to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes, or <see langword="null"/> when the file is gone.</returns>
    Task<byte[]> ReadBlobAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a file in a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="extension">The file extension, with its leading dot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The path the file was written to.</returns>
    Task<string> WriteBlobAsync(PdfWorkspaceScope scope, byte[] bytes, string extension, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a stored file.
    /// </summary>
    /// <param name="path">The path the file was written to.</param>
    Task DeleteBlobAsync(string path);

    /// <summary>
    /// Deletes a workspace and every file it holds.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteAsync(PdfWorkspaceScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the workspace's write lock, so two tool calls running at once cannot overwrite each other's
    /// changes.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lock, released when disposed.</returns>
    Task<IDisposable> LockAsync(PdfWorkspaceScope scope, CancellationToken cancellationToken = default);
}
