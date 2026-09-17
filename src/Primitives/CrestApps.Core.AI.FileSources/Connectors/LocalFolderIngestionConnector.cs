using System.Globalization;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources.Connectors;

/// <summary>
/// The settings a local-folder indexer carries.
/// </summary>
public sealed class LocalFolderIndexerMetadata
{
    /// <summary>
    /// Gets or sets the folder to read.
    /// </summary>
    public string RootPath { get; set; }

    /// <summary>
    /// Gets or sets the file pattern to match.
    /// </summary>
    public string SearchPattern { get; set; } = "*.*";

    /// <summary>
    /// Gets or sets a value indicating whether sub-folders are read too.
    /// </summary>
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// Gets or sets the most files the indexer will list, or <see langword="null"/> for the host default.
    /// </summary>
    public int? MaxItems { get; set; }
}

/// <summary>
/// Reads files out of a folder on the host.
/// </summary>
/// <remarks>
/// This is the simplest possible source, and the one that makes every other part of the intake path testable
/// end to end: point it at a folder, and its files become typed knowledge the same way an upload does.
/// <para>
/// The folder has to sit inside a host-allow-listed root. Without that, an administrator with access to the
/// indexer screen can read any file the host process can open.
/// </para>
/// </remarks>
public sealed class LocalFolderIngestionConnector : IIngestionConnector
{
    /// <summary>
    /// The connector's registered name, stored as the indexer's source.
    /// </summary>
    public const string ConnectorName = "LocalFolder";

    private readonly FileSourceOptions _options;
    private readonly ILogger<LocalFolderIngestionConnector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFolderIngestionConnector"/> class.
    /// </summary>
    /// <param name="options">The indexer options.</param>
    /// <param name="logger">The logger.</param>
    public LocalFolderIngestionConnector(
        IOptions<FileSourceOptions> options,
        ILogger<LocalFolderIngestionConnector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => ConnectorName;

    /// <inheritdoc />
    public ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(result);

        var metadata = settings.GetOrCreate<LocalFolderIndexerMetadata>();

        if (string.IsNullOrWhiteSpace(metadata.RootPath))
        {
            result.Fail(new System.ComponentModel.DataAnnotations.ValidationResult(
                "A folder is required.",
                [nameof(LocalFolderIndexerMetadata.RootPath)]));

            return ValueTask.CompletedTask;
        }

        if (!_options.IsAllowedLocalRoot(metadata.RootPath))
        {
            result.Fail(new System.ComponentModel.DataAnnotations.ValidationResult(
                "That folder is not one this application is configured to read. Ask an administrator to add it to the allowed roots.",
                [nameof(LocalFolderIndexerMetadata.RootPath)]));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Files are listed in path order so a run can stop and the next one pick up where it left off. A run
    /// that resumed saw only a window onto the folder, so it is never complete and never deletes anything:
    /// deciding something is gone needs a listing of the whole folder in one pass.
    /// </remarks>
    public Task<IngestionDiscoveryResult> DiscoverAsync(WebCrawler settings, string continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var metadata = settings.GetOrCreate<LocalFolderIndexerMetadata>();

        if (!TryResolveRoot(metadata, out var root, out var reason))
        {
            return Task.FromResult(new IngestionDiscoveryResult([], IsComplete: false, reason));
        }

        var pattern = string.IsNullOrWhiteSpace(metadata.SearchPattern) ? "*.*" : metadata.SearchPattern;
        var search = metadata.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var limit = metadata.MaxItems ?? _options.MaxItemsPerRun;
        var resumed = !string.IsNullOrEmpty(continuationToken);
        var items = new List<IngestionItemRef>();
        string cursor = null;

        try
        {
            // Ordered, because a cursor is only meaningful against a stable order. The framework promises
            // nothing about enumeration order, so the promise is made here.
            var paths = Directory.EnumerateFiles(root, pattern, search)
                .Select(path => (Path: path, ItemId: System.IO.Path.GetRelativePath(root, path).Replace('\\', '/')))
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
    public Task<IngestionItemContent> FetchAsync(WebCrawler settings, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(itemId);

        var metadata = settings.GetOrCreate<LocalFolderIndexerMetadata>();

        if (!TryResolveRoot(metadata, out var root, out _))
        {
            return Task.FromResult<IngestionItemContent>(null);
        }

        var path = Path.GetFullPath(Path.Combine(root, itemId));

        // The item id came out of a listing, but it also arrives from stored state, so it is checked rather
        // than trusted: a relative path that climbs out of the root must never open a file.
        var relative = Path.GetRelativePath(root, path);

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
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

    private bool TryResolveRoot(LocalFolderIndexerMetadata metadata, out string root, out string reason)
    {
        root = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(metadata.RootPath))
        {
            reason = "No folder is configured.";

            return false;
        }

        if (!_options.IsAllowedLocalRoot(metadata.RootPath))
        {
            reason = "The configured folder is not an allowed root.";

            return false;
        }

        root = Path.GetFullPath(metadata.RootPath);

        if (!Directory.Exists(root))
        {
            reason = "The configured folder does not exist.";
            root = null;

            return false;
        }

        return true;
    }
}
