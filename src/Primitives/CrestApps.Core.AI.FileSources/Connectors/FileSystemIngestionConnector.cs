using System.Globalization;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources.Connectors;

/// <summary>
/// Reads files out of a folder on the host's file system.
/// </summary>
/// <remarks>
/// This is the simplest possible source, and the one that makes every other part of the intake path testable
/// end to end: point it at a folder, and its files become typed knowledge the same way an upload does.
/// <para>
/// Where it may look comes from <see cref="FileSystemConnectorOptions"/>: a host sets that boundary once,
/// and a file source picks a folder inside it. Without the boundary, an administrator with access to the
/// file source screen can read any file the host process can open.
/// </para>
/// </remarks>
public sealed class FileSystemIngestionConnector : IIngestionConnector
{
    /// <summary>
    /// The connector's registered name, stored as the file source's source.
    /// </summary>
    public const string ConnectorName = "FileSystem";

    private readonly FileSourceOptions _fileSourceOptions;
    private readonly FileSystemConnectorOptions _options;
    private readonly ILogger<FileSystemIngestionConnector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemIngestionConnector"/> class.
    /// </summary>
    /// <param name="fileSourceOptions">The file source options.</param>
    /// <param name="options">The file-system connector options.</param>
    /// <param name="logger">The logger.</param>
    public FileSystemIngestionConnector(
        IOptions<FileSourceOptions> fileSourceOptions,
        IOptions<FileSystemConnectorOptions> options,
        ILogger<FileSystemIngestionConnector> logger)
    {
        _fileSourceOptions = fileSourceOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ConnectorName;

    /// <inheritdoc />
    public ValueTask ValidateAsync(IngestionSource source, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);

        var metadata = source.GetOrCreate<FileSystemFileSourceMetadata>();

        // A blank folder is not refused here: it means the folder the host allows, and the resolver below
        // decides whether there is one. Validating it separately is what made the default configuration
        // look invalid.
        //
        // The refusal says which of the several reasons applies, because a bare "not allowed" sends an
        // administrator to the allowed-roots list when the real problem was a '..' they typed.
        if (!_options.TryResolveRoot(metadata.RootPath, out _, out var reason))
        {
            result.Fail(new System.ComponentModel.DataAnnotations.ValidationResult(
                reason,
                [nameof(FileSystemFileSourceMetadata.RootPath)]));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Files are listed in path order so a run can stop and the next one pick up where it left off. A run
    /// that resumed saw only a window onto the folder, so it is never complete and never deletes anything:
    /// deciding something is gone needs a listing of the whole folder in one pass.
    /// </remarks>
    public Task<IngestionDiscoveryResult> DiscoverAsync(IngestionSource settings, string continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var metadata = settings.GetOrCreate<FileSystemFileSourceMetadata>();

        if (!TryResolveRoot(metadata, out var root, out var reason))
        {
            return Task.FromResult(new IngestionDiscoveryResult([], IsComplete: false, reason));
        }

        // Relative to this file source's own folder, which is what `root` already is: a file source pointed
        // at file-sources/test reads test and, when recursive, everything under test -- never the allowed
        // root it happens to sit in.
        var search = metadata.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var limit = metadata.MaxItems ?? _fileSourceOptions.MaxItemsPerRun;
        var resumed = !string.IsNullOrEmpty(continuationToken);
        var items = new List<IngestionItemRef>();
        string cursor = null;

        try
        {
            // Every file is listed. Which of them can be read is the reader resolver's business, not a glob
            // the connector guesses at.
            //
            // Ordered, because a cursor is only meaningful against a stable order. The framework promises
            // nothing about enumeration order, so the promise is made here.
            var paths = Directory.EnumerateFiles(root, "*", search)
                .Select(path => (Path: path, ItemId: Path.GetRelativePath(root, path).Replace('\\', '/')))
                .OrderBy(entry => entry.ItemId, StringComparer.Ordinal);

            foreach (var entry in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (resumed && string.CompareOrdinal(entry.ItemId, continuationToken) <= 0)
                {
                    continue;
                }

                if (limit > 0 && items.Count >= limit)
                {
                    // More files than one run will take on. The listing is not the whole folder, and saying
                    // so is what stops the files it did not reach being treated as deleted.
                    cursor = items[^1].ItemId;

                    break;
                }

                var info = new FileInfo(entry.Path);

                items.Add(new IngestionItemRef(
                    entry.ItemId,
                    string.Create(CultureInfo.InvariantCulture, $"{info.LastWriteTimeUtc.Ticks}:{info.Length}"),
                    info.Length,
                    info.LastWriteTimeUtc));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list files under '{RootPath}'.", root);

            return Task.FromResult(new IngestionDiscoveryResult([], IsComplete: false, "The folder could not be listed."));
        }

        var complete = cursor is null && !resumed;
        var message = complete
            ? null
            : cursor is null
                ? "This run resumed part-way through the folder, so the listing is not the whole of it."
                : "More files are present than one run will take on.";

        return Task.FromResult(new IngestionDiscoveryResult(items, complete, message, cursor));
    }

    /// <inheritdoc />
    public Task<IngestionItemContent> FetchAsync(IngestionSource settings, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(itemId);

        var metadata = settings.GetOrCreate<FileSystemFileSourceMetadata>();

        if (!TryResolveRoot(metadata, out var root, out _))
        {
            return Task.FromResult<IngestionItemContent>(null);
        }

        // The item id came out of a listing, but it also arrives from stored state, so it is checked rather
        // than trusted. A '..' is refused before the path is combined, so one that climbs out and lands back
        // inside the root is refused too rather than quietly accepted by the containment check below.
        if (FileSystemConnectorOptions.HasParentTraversal(itemId) || Path.IsPathRooted(itemId))
        {
            _logger.LogWarning("Refused to read '{ItemId}' because it is not a path relative to the indexed folder.", itemId);

            return Task.FromResult<IngestionItemContent>(null);
        }

        var path = Path.GetFullPath(Path.Combine(root, itemId));

        if (!FileSystemConnectorOptions.Contains(root, path))
        {
            _logger.LogWarning("Refused to read '{ItemId}' because it resolves outside the indexed folder.", itemId);

            return Task.FromResult<IngestionItemContent>(null);
        }

        if (!File.Exists(path))
        {
            return Task.FromResult<IngestionItemContent>(null);
        }

        try
        {
            var stream = File.OpenRead(path);

            return Task.FromResult(new IngestionItemContent
            {
                Content = stream,
                MediaType = MediaTypeHelper.InferMediaType(Path.GetExtension(path), fallbackContentType: string.Empty),
                Title = Path.GetFileName(path),
                FileName = Path.GetFileName(path),
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read '{Path}'.", path);

            return Task.FromResult<IngestionItemContent>(null);
        }
    }

    private bool TryResolveRoot(FileSystemFileSourceMetadata metadata, out string root, out string reason)
    {
        if (!_options.TryResolveRoot(metadata.RootPath, out root, out reason))
        {
            root = null;

            return false;
        }

        if (!Directory.Exists(root))
        {
            reason = "The configured folder does not exist.";
            root = null;

            return false;
        }

        return true;
    }
}
