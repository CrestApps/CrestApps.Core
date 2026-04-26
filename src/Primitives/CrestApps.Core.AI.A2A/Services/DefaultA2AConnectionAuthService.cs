using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.AI.A2A.Services;

internal sealed class DefaultA2AConnectionAuthService : IA2AConnectionAuthService
{
    private readonly IConnectionAuthHeaderBuilder _authHeaderBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultA2AConnectionAuthService"/> class.
    /// </summary>
    /// <param name="authHeaderBuilder">The auth header builder.</param>
    public DefaultA2AConnectionAuthService(IConnectionAuthHeaderBuilder authHeaderBuilder)
    {
        _authHeaderBuilder = authHeaderBuilder;
    }

    /// <summary>
    /// Builds headers.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<Dictionary<string, string>> BuildHeadersAsync(A2AConnectionMetadata metadata, CancellationToken cancellationToken = default)
    {
        return await _authHeaderBuilder.BuildHeadersAsync(metadata, A2AConstants.DataProtectionPurpose, cancellationToken);
    }

    /// <summary>
    /// Configures http client.
    /// </summary>
    /// <param name="httpClient">The http client.</param>
    /// <param name="metadata">The metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task ConfigureHttpClientAsync(HttpClient httpClient, A2AConnectionMetadata metadata, CancellationToken cancellationToken = default)
    {
        var headers = await BuildHeadersAsync(metadata, cancellationToken);

        foreach (var header in headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
