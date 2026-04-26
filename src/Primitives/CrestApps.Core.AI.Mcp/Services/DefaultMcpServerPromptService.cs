using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CrestApps.Core.AI.Mcp.Services;

/// <summary>
/// Represents the default MCP Server Prompt Service.
/// </summary>
public sealed class DefaultMcpServerPromptService : IMcpServerPromptService
{
    private readonly INamedCatalog<McpPrompt> _catalog;
    private readonly IEnumerable<IMcpPromptProvider> _promptProviders;
    private readonly IEnumerable<McpServerPrompt> _sdkPrompts;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMcpServerPromptService"/> class.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="promptProviders">The prompt providers.</param>
    /// <param name="sdkPrompts">The sdk prompts.</param>
    public DefaultMcpServerPromptService(
        INamedCatalog<McpPrompt> catalog,
        IEnumerable<IMcpPromptProvider> promptProviders = null,
        IEnumerable<McpServerPrompt> sdkPrompts = null)
    {
        _catalog = catalog;
        _promptProviders = promptProviders ?? [];
        _sdkPrompts = sdkPrompts ?? [];
    }

    /// <summary>
    /// Lists the operation.
    /// </summary>
    public async Task<IList<Prompt>> ListAsync()
    {
        var prompts = (await _catalog.GetAllAsync())
            .Where(entry => entry.Prompt != null)
            .Select(entry => entry.Prompt)
            .ToList();

        // Include prompts from registered providers (e.g., agent skill files).
        foreach (var provider in _promptProviders)
        {
            foreach (var skillPrompt in await provider.GetPromptsAsync())
            {
                if (!prompts.Any(prompt => prompt.Name == skillPrompt.ProtocolPrompt.Name))
                {
                    prompts.Add(skillPrompt.ProtocolPrompt);
                }
            }
        }

        // Include prompts registered via the MCP C# SDK.
        foreach (var sdkPrompt in _sdkPrompts)
        {
            if (!prompts.Any(prompt => prompt.Name == sdkPrompt.ProtocolPrompt.Name))
            {
                prompts.Add(sdkPrompt.ProtocolPrompt);
            }
        }

        return prompts;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<GetPromptResult> GetAsync(RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = (await _catalog.GetAllAsync(cancellationToken)).FirstOrDefault(entry => entry.Prompt?.Name == request.Params.Name);

        if (entry?.Prompt is not null)
        {
            return new GetPromptResult
            {
                Description = entry.Prompt.Description,
                Messages = [],
            };
        }

        // Try prompts from registered providers.
        foreach (var provider in _promptProviders)
        {
            var skillPrompt = (await provider.GetPromptsAsync())
                .FirstOrDefault(p => p.ProtocolPrompt.Name == request.Params.Name);

            if (skillPrompt is not null)
            {
                return await skillPrompt.GetAsync(request, cancellationToken);
            }
        }

        // Try prompts registered via the MCP C# SDK.
        var sdkPrompt = _sdkPrompts.FirstOrDefault(prompt => prompt.ProtocolPrompt.Name == request.Params.Name);

        if (sdkPrompt is not null)
        {
            return await sdkPrompt.GetAsync(request, cancellationToken);
        }

        throw new McpException($"Prompt '{request.Params.Name}' not found.");
    }
}
