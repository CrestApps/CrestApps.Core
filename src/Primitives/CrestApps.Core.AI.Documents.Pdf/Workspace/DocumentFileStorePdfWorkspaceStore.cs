using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// Keeps PDF workspaces in the <see cref="IDocumentFileStore"/>, next to the conversation's documents, so a
/// host that moves document files to blob storage moves the workspaces with them.
/// </summary>
internal sealed class DocumentFileStorePdfWorkspaceStore : IPdfWorkspaceStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    private readonly IDocumentFileStore _fileStore;
    private readonly ILogger<DocumentFileStorePdfWorkspaceStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentFileStorePdfWorkspaceStore"/> class.
    /// </summary>
    /// <param name="fileStore">The document file store.</param>
    /// <param name="logger">The logger.</param>
    public DocumentFileStorePdfWorkspaceStore(
        IDocumentFileStore fileStore,
        ILogger<DocumentFileStorePdfWorkspaceStore> logger)
    {
        _fileStore = fileStore;
        _logger = logger;
    }

    /// <summary>
    /// Loads a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<PdfWorkspaceState> LoadAsync(PdfWorkspaceScope scope, CancellationToken cancellationToken = default)
    {
        await using var stored = await _fileStore.GetFileAsync(scope.StatePath);

        if (stored is null)
        {
            return new PdfWorkspaceState();
        }

        try
        {
            await using var gzip = new GZipStream(stored, CompressionMode.Decompress);

            return await JsonSerializer.DeserializeAsync<PdfWorkspaceState>(gzip, PdfDefinitionJson.Options, cancellationToken)
                ?? new PdfWorkspaceState();
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            // A workspace that cannot be read is replaced rather than left to fail every tool call in the
            // conversation. The uploads it was made from are untouched, so nothing the reader owns is lost.
            _logger.LogWarning(ex, "The PDF workspace at '{Path}' could not be read and was reset.", scope.StatePath);

            return new PdfWorkspaceState();
        }
    }

    /// <summary>
    /// Saves a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="state">The workspace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(PdfWorkspaceScope scope, PdfWorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var buffer = new MemoryStream();

        await using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            await JsonSerializer.SerializeAsync(gzip, state, PdfDefinitionJson.Options, cancellationToken);
        }

        buffer.Position = 0;
        await _fileStore.SaveFileAsync(scope.StatePath, buffer);
    }

    /// <summary>
    /// Reads a stored file.
    /// </summary>
    /// <param name="path">The path the file was written to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<byte[]> ReadBlobAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        await using var stream = await _fileStore.GetFileAsync(path);

        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    /// <summary>
    /// Stores a file in a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="extension">The file extension, with its leading dot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<string> WriteBlobAsync(PdfWorkspaceScope scope, byte[] bytes, string extension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        cancellationToken.ThrowIfCancellationRequested();

        var safeExtension = string.IsNullOrWhiteSpace(extension) || extension.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.')
            ? ".bin"
            : extension.ToLowerInvariant();

        var path = scope.Folder + "/" + UniqueId.GenerateId() + safeExtension;

        await using var stream = new MemoryStream(bytes, writable: false);
        await _fileStore.SaveFileAsync(path, stream);

        return path;
    }

    /// <summary>
    /// Deletes a stored file.
    /// </summary>
    /// <param name="path">The path the file was written to.</param>
    public async Task DeleteBlobAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await _fileStore.DeleteFileAsync(path);
        }
        catch (Exception ex)
        {
            // A file left behind costs storage, not correctness: nothing refers to it any more.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Failed to delete the PDF workspace file '{Path}'.", path);
            }
        }
    }

    /// <summary>
    /// Deletes a workspace and every file it holds.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task DeleteAsync(PdfWorkspaceScope scope, CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(scope, cancellationToken);

        foreach (var document in state.Documents)
        {
            await DeleteBlobAsync(document.BlobPath);
        }

        foreach (var asset in state.Assets)
        {
            await DeleteBlobAsync(asset.BlobPath);
        }

        await DeleteBlobAsync(scope.StatePath);
    }

    /// <summary>
    /// Takes the workspace's write lock.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IDisposable> LockAsync(PdfWorkspaceScope scope, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(scope.Folder, static _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);

        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}
