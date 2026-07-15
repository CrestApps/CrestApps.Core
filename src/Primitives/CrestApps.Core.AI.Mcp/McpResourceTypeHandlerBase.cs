using CrestApps.Core.AI.Mcp.Models;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Base class for MCP resource type handlers that provides common logic.
/// Subclasses only need to implement the <see cref="GetResultAsync(McpResource, IReadOnlyDictionary{string, string}, CancellationToken)"/> method.
/// </summary>
public abstract class McpResourceTypeHandlerBase : IMcpResourceTypeHandler
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpResourceTypeHandlerBase"/> class.
    /// </summary>
    /// <param name="type">The type.</param>
    protected McpResourceTypeHandlerBase(string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);

        Type = type;
    }

    /// <summary>
    /// Gets the type.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="variables">The variables.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<ReadResourceResult> ReadAsync(McpResource resource, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(variables);

        return GetResultAsync(resource, variables, cancellationToken);
    }

    /// <summary>
    /// Reads the resource content using the extracted URI variables.
    /// </summary>
    /// <param name="resource">The MCP resource definition.</param>
    /// <param name="variables">The variables extracted from the URI pattern match.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task containing the read resource result.</returns>
    protected abstract Task<ReadResourceResult> GetResultAsync(McpResource resource, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a <see cref="ReadResourceResult"/> containing an error message instead of throwing an exception.
    /// </summary>
    /// <param name="uri">The resource URI to include in the response.</param>
    /// <param name="errorMessage">The error message to return to the caller.</param>
    /// <returns>A <see cref="ReadResourceResult"/> with the error message as text content.</returns>
    public static ReadResourceResult CreateErrorResult(string uri, string errorMessage)
    {
        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                Uri = uri,
                MimeType = "text/plain",
                Text = errorMessage,
                }
            ]
        };
    }

    /// <summary>
    /// Sanitizes a user-supplied path to prevent directory traversal attacks.
    /// Rejects paths containing ".." segments, null bytes, or other dangerous patterns.
    /// </summary>
    /// <param name="path">The raw path value from the user.</param>
    /// <returns>The sanitized path with leading/trailing slashes trimmed.</returns>
    /// <exception cref="ArgumentException">Thrown when the path contains directory traversal sequences or null bytes.</exception>
    protected static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (path.Contains('\0'))
        {
            throw new ArgumentException("Path contains invalid characters.", nameof(path));
        }

        if (path.AsSpan().IndexOfAny('/', '\\') < 0)
        {
            if (path is "." or "..")
            {
                throw new ArgumentException("Path must not contain directory traversal sequences.", nameof(path));
            }

            return path;
        }

        var outputLength = 0;
        var segmentCount = 0;
        var segmentStart = 0;
        var requiresNormalization = false;

        for (var i = 0; i <= path.Length; i++)
        {
            if (i < path.Length && path[i] is not ('/' or '\\'))
            {
                continue;
            }

            var segmentLength = i - segmentStart;

            if (segmentLength == 0)
            {
                requiresNormalization = true;
            }
            else
            {
                if (path[segmentStart] == '.' &&
                    (segmentLength == 1 || (segmentLength == 2 && path[segmentStart + 1] == '.')))
                {
                    throw new ArgumentException("Path must not contain directory traversal sequences.", nameof(path));
                }

                outputLength += segmentLength + (segmentCount > 0 ? 1 : 0);
                segmentCount++;
            }

            if (i < path.Length && path[i] == '\\')
            {
                requiresNormalization = true;
            }

            segmentStart = i + 1;
        }

        if (outputLength == 0)
        {
            return string.Empty;
        }

        if (!requiresNormalization)
        {
            return path;
        }

        return string.Create(outputLength, path, static (destination, source) =>
        {
            var destinationIndex = 0;
            var sourceSegmentStart = 0;

            for (var i = 0; i <= source.Length; i++)
            {
                if (i < source.Length && source[i] is not ('/' or '\\'))
                {
                    continue;
                }

                var segmentLength = i - sourceSegmentStart;

                if (segmentLength > 0)
                {
                    if (destinationIndex > 0)
                    {
                        destination[destinationIndex++] = '/';
                    }

                    source.AsSpan(sourceSegmentStart, segmentLength).CopyTo(destination[destinationIndex..]);
                    destinationIndex += segmentLength;
                }

                sourceSegmentStart = i + 1;
            }
        });
    }

    /// <summary>
    /// Determines whether the given MIME type represents text-based content
    /// that can be safely read as a string.
    /// </summary>
    /// <param name="mimeType">The MIME type to check.</param>
    /// <returns><c>true</c> if the MIME type is text-based; otherwise, <c>false</c>.</returns>
    protected static bool IsTextMimeType(string mimeType)
    {
        if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return mimeType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
            || mimeType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
                || mimeType.EndsWith("/xml", StringComparison.OrdinalIgnoreCase)
                    || mimeType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
                        || mimeType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
                            || mimeType.Equals("application/ecmascript", StringComparison.OrdinalIgnoreCase)
                                || mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase);
    }
}
