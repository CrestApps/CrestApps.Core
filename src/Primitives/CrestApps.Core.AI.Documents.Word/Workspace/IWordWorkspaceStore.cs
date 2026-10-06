namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// Keeps each conversation's Word workspace — its working documents and their files — between turns.
/// </summary>
internal interface IWordWorkspaceStore
{
    /// <summary>
    /// Loads a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The workspace, empty when the conversation has none yet.</returns>
    Task<WordWorkspaceState> LoadAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="state">The workspace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveAsync(WordWorkspaceScope scope, WordWorkspaceState state, CancellationToken cancellationToken = default);

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
    Task<string> WriteBlobAsync(WordWorkspaceScope scope, byte[] bytes, string extension, CancellationToken cancellationToken = default);

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
    Task DeleteAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the workspace's write lock, so two tool calls that run at once do not overwrite each other.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lock, released when disposed.</returns>
    Task<IDisposable> LockAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default);
}
