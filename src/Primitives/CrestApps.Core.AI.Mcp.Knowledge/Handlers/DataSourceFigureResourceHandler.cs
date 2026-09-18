using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.AI.Mcp.Knowledge.Handlers;

/// <summary>
/// Serves the stored picture of an ingested figure or chart to an MCP client.
/// </summary>
/// <remarks>
/// A canonical identifier is short and guessable, so nothing here trusts the one it was handed: the object
/// has to belong to the data source in the address and has to actually be a picture. Anything else is a
/// not-found, never a read of somebody else's figure.
/// </remarks>
public sealed class DataSourceFigureResourceHandler : McpResourceTypeHandlerBase
{
    private readonly IKnowledgeObjectStore _store;
    private readonly IDocumentFileStore _fileStore;
    private readonly ILogger<DataSourceFigureResourceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourceFigureResourceHandler"/> class.
    /// </summary>
    /// <param name="store">The knowledge object store.</param>
    /// <param name="fileStore">The store the figure bytes were written to.</param>
    /// <param name="logger">The logger.</param>
    public DataSourceFigureResourceHandler(
        IKnowledgeObjectStore store,
        IDocumentFileStore fileStore,
        ILogger<DataSourceFigureResourceHandler> logger)
        : base(DataSourceFigureResourceConstants.Type)
    {
        _store = store;
        _fileStore = fileStore;
        _logger = logger;
    }

    /// <summary>
    /// Reads one figure.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="variables">The variables extracted from the URI.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The figure bytes, or an error result.</returns>
    protected override async Task<ReadResourceResult> GetResultAsync(
        McpResource resource,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken)
    {
        var uri = resource.Resource?.Uri ?? DataSourceFigureResourceConstants.UriTemplate;

        string dataSourceId;
        string figureId;

        try
        {
            dataSourceId = SanitizePath(variables.TryGetValue("dataSourceId", out var rawDataSourceId) ? rawDataSourceId : null);
            figureId = SanitizePath(variables.TryGetValue("figureId", out var rawFigureId) ? rawFigureId : null);
        }
        catch (ArgumentException)
        {
            return CreateErrorResult(uri, "The figure address is not valid.");
        }

        if (string.IsNullOrEmpty(dataSourceId) || string.IsNullOrEmpty(figureId))
        {
            return CreateErrorResult(uri, "The figure address must name a data source and a figure.");
        }

        var entry = await _store.FindByCanonicalIdAsync(dataSourceId, figureId, cancellationToken);

        // Source is the owning data source. Checking it is what stops a guessed identifier from reading a
        // figure out of a data source the caller was never given.
        if (entry is null ||
            !string.Equals(entry.Source, dataSourceId, StringComparison.OrdinalIgnoreCase) ||
            entry.ObjectType is not (KnowledgeObjectTypes.Figure or KnowledgeObjectTypes.Chart))
        {
            return CreateErrorResult(uri, "The figure was not found.");
        }

        if (string.IsNullOrWhiteSpace(entry.StoragePath))
        {
            return CreateErrorResult(uri, "The figure has no stored picture.");
        }

        byte[] content;

        try
        {
            await using var stream = await _fileStore.GetFileAsync(entry.StoragePath);

            if (stream is null)
            {
                return CreateErrorResult(uri, "The figure was not found.");
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            content = buffer.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the stored figure '{StoragePath}'.", entry.StoragePath);

            return CreateErrorResult(uri, "The figure could not be read.");
        }

        return new ReadResourceResult
        {
            Contents =
            [
                new BlobResourceContents
                {
                    Uri = uri,
                    MimeType = string.IsNullOrWhiteSpace(entry.MediaType) ? "application/octet-stream" : entry.MediaType,
                    Blob = content,
                }
            ],
        };
    }
}
