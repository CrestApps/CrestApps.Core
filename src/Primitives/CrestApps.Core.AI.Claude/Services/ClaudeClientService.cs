using Anthropic;
using Anthropic.Core;
using CrestApps.Core.AI.Claude.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Claude.Services;

/// <summary>
/// Creates configured Anthropic SDK clients and exposes model discovery helpers.
/// </summary>
public sealed class ClaudeClientService
{
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeClientService> _logger;

    public ClaudeClientService(
        IOptions<ClaudeOptions> options,
        ILogger<ClaudeClientService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public AnthropicClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Claude is not configured. Please configure an API key.");
        }

        var clientOptions = new ClientOptions
        {
            ApiKey = _options.ApiKey,
        };

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            clientOptions.BaseUrl = _options.BaseUrl;
        }

        return new AnthropicClient(clientOptions);
    }

    public async Task<IReadOnlyCollection<ClaudeModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return [];
        }

        try
        {
            using var client = CreateClient();

            var page = await client.Models.List(cancellationToken: cancellationToken);
            var models = new List<ClaudeModelInfo>();

            while (page is not null)
            {
                models.AddRange(page.Items
                    .Where(model => !string.IsNullOrWhiteSpace(model.ID))
                    .Select(model => new ClaudeModelInfo
                    {
                        Id = model.ID,
                        Name = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ID : model.DisplayName,
                        MaxInputTokens = model.MaxInputTokens,
                        MaxOutputTokens = model.MaxTokens,
                    }));

                if (!page.HasNext())
                {
                    break;
                }

                page = await page.Next(cancellationToken);
            }

            return models
                .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error listing Claude models.");
            return [];
        }
    }
}
