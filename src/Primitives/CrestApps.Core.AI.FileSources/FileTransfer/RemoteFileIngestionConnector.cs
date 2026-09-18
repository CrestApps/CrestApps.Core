using System.Globalization;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// The folder settings a file-server indexer carries.
/// </summary>
public sealed class RemoteFolderIndexerMetadata
{
    /// <summary>
    /// Gets or sets the folder on the server to read.
    /// </summary>
    public string RootPath { get; set; } = "/";

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
/// Reads files off a file server.
/// </summary>
/// <remarks>
/// FTP and SFTP differ in how a connection is made and almost nothing else, so the protocol lives behind
/// <see cref="IRemoteFileClient"/> and everything that decides what gets indexed lives here, once.
/// <para>
/// A listing that failed halfway is reported as incomplete rather than as a shorter list. A file server that
/// drops a connection mid-listing is ordinary, and treating what arrived as the whole folder would delete
/// everything that did not.
/// </para>
/// </remarks>
public abstract class RemoteFileIngestionConnector : IIngestionConnector
{
    private readonly IRemoteFileClientFactory _clientFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteFileIngestionConnector"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="logger">The logger.</param>
    protected RemoteFileIngestionConnector(IRemoteFileClientFactory clientFactory, ILogger logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => _clientFactory.ConnectorName;

    /// <inheritdoc />
    public abstract ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public async Task<IngestionDiscoveryResult> DiscoverAsync(WebCrawler settings, string continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var folder = settings.GetOrCreate<RemoteFolderIndexerMetadata>();

        try
        {
            await using var client = await _clientFactory.CreateAsync(settings, cancellationToken);

            var listing = await client.ListAsync(
                string.IsNullOrWhiteSpace(folder.RootPath) ? "/" : folder.RootPath,
                folder.Recursive,
                folder.MaxItems ?? 0,
                cancellationToken);

            var items = new List<IngestionItemRef>(listing.Files.Count);

            foreach (var file in listing.Files)
            {
                items.Add(new IngestionItemRef(
                    file.Path,
                    BuildChangeToken(file),
                    file.SizeBytes,
                    file.LastModifiedUtc));
            }

            return new IngestionDiscoveryResult(items, listing.IsComplete, listing.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list '{RootPath}' for indexer '{IndexerId}'.", folder.RootPath, settings.ItemId);

            return new IngestionDiscoveryResult([], IsComplete: false, "The folder could not be listed.");
        }
    }

    /// <inheritdoc />
    public async Task<IngestionItemContent> FetchAsync(WebCrawler settings, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(itemId);

        var folder = settings.GetOrCreate<RemoteFolderIndexerMetadata>();

        if (itemId.Contains("..", StringComparison.Ordinal))
        {
            // The identifier came out of a listing, but it also arrives from stored state, so it is checked
            // rather than trusted.
            _logger.LogWarning("Refused to read '{ItemId}' because it climbs out of the indexed folder.", itemId);

            return null;
        }

        IRemoteFileClient client = null;

        try
        {
            client = await _clientFactory.CreateAsync(settings, cancellationToken);

            var stream = await client.OpenReadAsync(
                string.IsNullOrWhiteSpace(folder.RootPath) ? "/" : folder.RootPath,
                itemId,
                cancellationToken);

            if (stream is null)
            {
                await client.DisposeAsync();

                return null;
            }

            var fileName = Path.GetFileName(itemId.Replace('\\', '/'));

            return new IngestionItemContent
            {
                // The client is released when the content is, so a connection is never held open longer
                // than the stream that needs it.
                Content = new ClientOwningStream(stream, client),
                MediaType = MediaTypeHelper.InferMediaType(Path.GetExtension(fileName), fallbackContentType: string.Empty),
                Title = fileName,
                FileName = fileName,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read '{ItemId}' for indexer '{IndexerId}'.", itemId, settings.ItemId);

            if (client is not null)
            {
                await client.DisposeAsync();
            }

            return null;
        }
    }

    /// <summary>
    /// Builds the value that says whether a file has changed since it was last read.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>The token, or <see langword="null"/> when the server said nothing that could serve as one.</returns>
    /// <remarks>
    /// A server that reports neither a size nor a modified time gives nothing to compare, and an item with no
    /// token is re-read every run. That is the honest answer: FTP in particular has no reliable validator, so
    /// the content hash computed during ingestion is what actually prevents duplicate work.
    /// </remarks>
    private static string BuildChangeToken(RemoteFile file)
    {
        if (file.LastModifiedUtc is null && file.SizeBytes is null)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{file.LastModifiedUtc?.UtcTicks ?? 0}:{file.SizeBytes ?? -1}");
    }

    /// <summary>
    /// A stream that releases the connection it was read over.
    /// </summary>
    private sealed class ClientOwningStream : Stream
    {
        private readonly Stream _inner;
        private readonly IRemoteFileClient _client;

        public ClientOwningStream(Stream inner, IRemoteFileClient client)
        {
            _inner = inner;
            _client = client;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await _client.DisposeAsync();

            await base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            base.Dispose(disposing);
        }
    }
}
