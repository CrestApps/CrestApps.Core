using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.AI.A2A.Services;

internal sealed class DefaultA2AConnectionAuthService : IA2AConnectionAuthService
{
    private readonly IConnectionAuthHeaderBuilder _authHeaderBuilder;

    public DefaultA2AConnectionAuthService(IConnectionAuthHeaderBuilder authHeaderBuilder)
    {
        _authHeaderBuilder = authHeaderBuilder;
    }

    public async Task<Dictionary<string, string>> BuildHeadersAsync(A2AConnectionMetadata metadata, CancellationToken cancellationToken = default)
    {
        return await _authHeaderBuilder.BuildHeadersAsync(metadata, A2AConstants.DataProtectionPurpose, cancellationToken);
    }

    public async Task ConfigureHttpClientAsync(HttpClient httpClient, A2AConnectionMetadata metadata, CancellationToken cancellationToken = default)
    {
        var headers = await BuildHeadersAsync(metadata, cancellationToken);

        foreach (var header in headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
