using CrestApps.Core.AI.Mcp.IO;
using CrestApps.Core.AI.Mcp.Models;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Base class for MCP resource handlers that read a single text-based file from a
/// remote transport (e.g., FTP, SFTP). It centralizes path sanitization, size limits
/// via <see cref="LimitedWriteStream"/>, MIME-type detection, and consistent error
/// formatting so individual transports only implement the actual download.
/// </summary>
/// <typeparam name="TMetadata">The connection metadata type carried by the resource.</typeparam>
public abstract class RemoteFileResourceHandlerBase<TMetadata> : McpResourceTypeHandlerBase
    where TMetadata : class
{
    /// <summary>
    /// Default upper bound (in bytes) for downloaded resource content when the
    /// transport metadata does not specify one.
    /// </summary>
    protected const long DefaultMaxResourceBytes = 5 * 1024 * 1024;

    private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="RemoteFileResourceHandlerBase{TMetadata}"/>.
    /// </summary>
    /// <param name="type">The resource type identifier this handler serves.</param>
    /// <param name="logger">The logger used for warnings and diagnostics.</param>
    protected RemoteFileResourceHandlerBase(string type, ILogger logger)
        : base(type)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <summary>
    /// Gets a short, human-readable transport name (e.g., "FTP", "SFTP") used in
    /// error messages and log entries.
    /// </summary>
    protected abstract string TransportName { get; }

    /// <summary>
    /// Gets the message returned to the caller when the resource is missing the
    /// expected connection metadata.
    /// </summary>
    protected virtual string MissingMetadataMessage
        => $"{TransportName} connection metadata is missing.";

    /// <summary>
    /// Gets the message returned to the caller when the connection metadata does
    /// not include a host.
    /// </summary>
    protected virtual string MissingHostMessage
        => $"{TransportName} host is required in the connection metadata.";

    /// <inheritdoc/>
    protected override async Task<ReadResourceResult> GetResultAsync(McpResource resource, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken)
    {
        if (!resource.TryGet<TMetadata>(out var metadata))
        {
            return CreateErrorResult(resource.Resource.Uri, MissingMetadataMessage);
        }

        if (!TryGetHost(metadata, out var host) || string.IsNullOrEmpty(host))
        {
            return CreateErrorResult(resource.Resource.Uri, MissingHostMessage);
        }

        var validationError = ValidateMetadata(metadata);

        if (!string.IsNullOrEmpty(validationError))
        {
            return CreateErrorResult(resource.Resource.Uri, validationError);
        }

        var rawPath = variables.TryGetValue("path", out var pathValue) ? pathValue : string.Empty;
        var remotePath = "/" + SanitizePath(rawPath);
        var maxBytes = ResolveMaxResourceBytes(metadata);

        using var stream = new MemoryStream();
        await using var limited = new LimitedWriteStream(stream, maxBytes);

        try
        {
            await DownloadAsync(metadata, host, remotePath, limited, cancellationToken);
        }
        catch (ResourceSizeLimitExceededException ex)
        {
            _logger.LogWarning(ex, "{Transport} resource {ResourceId} exceeded the maximum allowed size of {MaxBytes} bytes.", TransportName, resource.ItemId, ex.MaxBytes);

            return CreateErrorResult(resource.Resource.Uri, $"The requested {TransportName} resource exceeds the maximum allowed size of {ex.MaxBytes:N0} bytes.");
        }

        stream.Position = 0;

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);

        var mimeType = resource.Resource?.MimeType;

        if (string.IsNullOrEmpty(mimeType) && !_contentTypeProvider.TryGetContentType(remotePath, out mimeType))
        {
            mimeType = "application/octet-stream";
        }

        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = resource.Resource.Uri,
                    MimeType = mimeType,
                    Text = content,
                }
            ]
        };
    }

    /// <summary>
    /// Resolves the maximum allowed download size, in bytes, for the supplied metadata.
    /// </summary>
    /// <param name="metadata">The connection metadata.</param>
    /// <returns>The configured maximum or <see cref="DefaultMaxResourceBytes"/> when none is set.</returns>
    protected virtual long ResolveMaxResourceBytes(TMetadata metadata)
    {
        var configured = GetMaxResourceBytes(metadata);

        return configured is > 0 ? configured.Value : DefaultMaxResourceBytes;
    }

    /// <summary>
    /// Performs additional metadata validation before attempting to download.
    /// Return a non-empty string to short-circuit with an error response.
    /// </summary>
    /// <param name="metadata">The connection metadata.</param>
    /// <returns>An error message, or <c>null</c> when the metadata is valid.</returns>
    protected virtual string ValidateMetadata(TMetadata metadata) => null;

    /// <summary>
    /// Gets the per-resource size cap, if one is configured by the metadata.
    /// </summary>
    /// <param name="metadata">The connection metadata.</param>
    /// <returns>The configured size cap, or <c>null</c> to use the default.</returns>
    protected abstract long? GetMaxResourceBytes(TMetadata metadata);

    /// <summary>
    /// Extracts the host portion from the metadata.
    /// </summary>
    /// <param name="metadata">The connection metadata.</param>
    /// <param name="host">The extracted host value.</param>
    /// <returns><c>true</c> when a host was extracted; otherwise, <c>false</c>.</returns>
    protected abstract bool TryGetHost(TMetadata metadata, out string host);

    /// <summary>
    /// Connects to the remote transport and writes the requested file to the supplied stream.
    /// Implementations must observe <paramref name="cancellationToken"/> and may throw
    /// <see cref="ResourceSizeLimitExceededException"/> indirectly via the destination stream.
    /// </summary>
    /// <param name="metadata">The connection metadata.</param>
    /// <param name="host">The validated host extracted by <see cref="TryGetHost(TMetadata, out string)"/>.</param>
    /// <param name="remotePath">The sanitized remote path to download.</param>
    /// <param name="destination">The size-limited destination stream.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    protected abstract Task DownloadAsync(TMetadata metadata, string host, string remotePath, Stream destination, CancellationToken cancellationToken);
}
