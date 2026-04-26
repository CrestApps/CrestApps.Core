using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Builds HTTP authentication headers from connection metadata.
/// Protocol-agnostic - works for MCP SSE, A2A, or any future protocol.
/// </summary>
public interface IConnectionAuthHeaderBuilder
{
    /// <summary>
    /// Builds a dictionary of HTTP authentication headers based on the provided metadata.
    /// </summary>
    /// <param name="metadata">The connection authentication metadata.</param>
    /// <param name="dataProtectionPurpose">The data protection purpose string for unprotecting credentials.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A dictionary of HTTP header name-value pairs.</returns>
    Task<Dictionary<string, string>> BuildHeadersAsync(
        IConnectionAuthMetadata metadata,
        string dataProtectionPurpose,
        CancellationToken cancellationToken = default);
}
