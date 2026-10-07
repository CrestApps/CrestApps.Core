using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// Keeps Word workspaces in the <see cref="IDocumentFileStore"/>, next to the conversation's documents, so a
/// host that moves document files to blob storage moves the workspaces with them.
/// </summary>
/// <remarks>
/// The workspace lock is held in this process's memory. A host that runs tool calls for one conversation on
/// several servers at the same moment is not serialized by it: two changes saved at once keep only the later one.
/// </remarks>
internal sealed class DocumentFileStoreWordWorkspaceStore : IWordWorkspaceStore
{
    private static readonly Dictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);

    private readonly IDocumentFileStore _fileStore;
    private readonly ILogger<DocumentFileStoreWordWorkspaceStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentFileStoreWordWorkspaceStore"/> class.
    /// </summary>
    /// <param name="fileStore">The document file store.</param>
    /// <param name="logger">The logger.</param>
    public DocumentFileStoreWordWorkspaceStore(
        IDocumentFileStore fileStore,
        ILogger<DocumentFileStoreWordWorkspaceStore> logger)
    {
        _fileStore = fileStore;
        _logger = logger;
    }

    /// <summary>
    /// Gets the number of workspaces whose lock is held or waited for, for tests.
    /// </summary>
    internal static int LockCount
    {
        get
        {
            lock (_locks)
            {
                return _locks.Count;
            }
        }
    }

    /// <summary>
    /// Loads a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<WordWorkspaceState> LoadAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default)
    {
        byte[] stored;

        await using (var stream = await _fileStore.GetFileAsync(scope.StatePath))
        {
            if (stream is null)
            {
                return new WordWorkspaceState();
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            stored = buffer.ToArray();
        }

        try
        {
            await using var gzip = new GZipStream(new MemoryStream(stored, writable: false), CompressionMode.Decompress);

            return await JsonSerializer.DeserializeAsync<WordWorkspaceState>(gzip, WordJson.Options, cancellationToken)
                ?? new WordWorkspaceState();
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            // A workspace that cannot be read is replaced rather than left to fail every tool call in the
            // conversation. The uploads it was made from are untouched, so nothing the reader owns is lost. The
            // files it still names are remembered, so deleting the workspace deletes them too.
            _logger.LogWarning(ex, "The Word workspace at '{Path}' could not be read and was reset.", scope.StatePath);

            return new WordWorkspaceState
            {
                OrphanedBlobs = SalvageBlobPaths(scope, stored),
            };
        }
    }

    /// <summary>
    /// Saves a workspace.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="state">The workspace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(WordWorkspaceScope scope, WordWorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var buffer = new MemoryStream();

        await using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            await JsonSerializer.SerializeAsync(gzip, state, WordJson.Options, cancellationToken);
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
    public async Task<string> WriteBlobAsync(WordWorkspaceScope scope, byte[] bytes, string extension, CancellationToken cancellationToken = default)
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
    public async Task<bool> DeleteBlobAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            await _fileStore.DeleteFileAsync(path);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete the Word workspace file '{Path}'.", path);

            return false;
        }
    }

    /// <summary>
    /// Deletes a workspace and every file it holds.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<WordWorkspaceState> DeleteAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default)
    {
        using var gate = await LockAsync(scope, cancellationToken);

        var state = await LoadAsync(scope, cancellationToken);
        var leftBehind = new List<string>();

        // The file store cannot list a folder, so the workspace is the only record of its files: it is deleted
        // last, and only once every file it names is gone.
        foreach (var path in state.Documents.Select(document => document.BlobPath).Concat(state.OrphanedBlobs).Distinct(StringComparer.Ordinal))
        {
            if (!await DeleteBlobAsync(path))
            {
                leftBehind.Add(path);
            }
        }

        if (leftBehind.Count == 0)
        {
            await DeleteBlobAsync(scope.StatePath);

            return state;
        }

        _logger.LogWarning("{Count} file(s) of the Word workspace at '{Path}' could not be deleted; the workspace keeps them listed for the next delete.", leftBehind.Count, scope.StatePath);

        await SaveAsync(scope, new WordWorkspaceState { OrphanedBlobs = leftBehind }, CancellationToken.None);

        return state;
    }

    /// <summary>
    /// Takes the workspace's write lock.
    /// </summary>
    /// <param name="scope">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IDisposable> LockAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default)
    {
        var key = scope.Folder;
        LockEntry entry;

        lock (_locks)
        {
            if (!_locks.TryGetValue(key, out entry))
            {
                entry = new LockEntry();
                _locks[key] = entry;
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Release(key, entry, holdsSemaphore: false);

            throw;
        }

        return new Releaser(key, entry);
    }

    /// <summary>
    /// Gives up a hold on, or a wait for, a workspace lock, and forgets the lock once nobody holds or waits for it.
    /// </summary>
    private static void Release(string key, LockEntry entry, bool holdsSemaphore)
    {
        lock (_locks)
        {
            if (--entry.References == 0)
            {
                _locks.Remove(key);
            }
        }

        if (holdsSemaphore)
        {
            entry.Semaphore.Release();
        }
    }

    /// <summary>
    /// Finds the paths of this workspace's files in a workspace file that cannot be read, so they can still be
    /// deleted with the workspace.
    /// </summary>
    private static List<string> SalvageBlobPaths(WordWorkspaceScope scope, byte[] stored)
    {
        var text = new StringBuilder();

        try
        {
            using var gzip = new GZipStream(new MemoryStream(stored, writable: false), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var buffer = new char[8192];
            int read;

            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                text.Append(buffer, 0, read);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // What was decompressed before the damage is still searched.
        }

        var pattern = Regex.Escape(scope.Folder + "/") + "[A-Za-z0-9]+\\.[A-Za-z0-9]+";

        return [.. Regex.Matches(text.ToString(), pattern, RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Where(path => !scope.StatePath.StartsWith(path, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int References { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly string _key;
        private LockEntry _entry;

        public Releaser(string key, LockEntry entry)
        {
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);

            if (entry is not null)
            {
                Release(_key, entry, holdsSemaphore: true);
            }
        }
    }
}
