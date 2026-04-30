namespace CrestApps.Core.AI.Mcp.IO;

/// <summary>
/// Thrown when an MCP resource handler attempts to download more bytes than the
/// configured maximum allowed for a remote resource.
/// </summary>
public sealed class ResourceSizeLimitExceededException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ResourceSizeLimitExceededException"/> class.</summary>
    /// <param name="maxBytes">The configured maximum number of bytes that may be read.</param>
    public ResourceSizeLimitExceededException(long maxBytes)
        : base($"The remote resource exceeds the configured maximum size of {maxBytes:N0} bytes.")
    {
        MaxBytes = maxBytes;
    }

    /// <summary>Gets the configured maximum number of bytes that may be read for a single resource request.</summary>
    public long MaxBytes { get; }
}
