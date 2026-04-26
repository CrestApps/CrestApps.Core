using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CrestApps.Core.AI.Mcp.Services;

/// <summary>
/// Represents the default MCP Server Resource Service.
/// </summary>
public sealed class DefaultMcpServerResourceService : IMcpServerResourceService
{
    private readonly ISourceCatalog<McpResource> _catalog;
    private readonly IEnumerable<IMcpResourceProvider> _resourceProviders;
    private readonly IEnumerable<McpServerResource> _sdkResources;

    public DefaultMcpServerResourceService(
        ISourceCatalog<McpResource> catalog,
        IEnumerable<IMcpResourceProvider> resourceProviders,
        IEnumerable<McpServerResource> sdkResources)
    {
        _catalog = catalog;
        _resourceProviders = resourceProviders;
        _sdkResources = sdkResources;
    }

    public async Task<IList<Resource>> ListAsync()
    {
        return (await GetAllResourcesAsync()).Where(resource => resource.Uri is null || !McpResourceUri.IsTemplate(resource.Uri)).ToList();
    }

    public async Task<IList<ResourceTemplate>> ListTemplatesAsync()
    {
        var templates = new List<ResourceTemplate>();
        foreach (var resource in await GetAllResourcesAsync())
        {
            if (resource.Uri is not null && McpResourceUri.IsTemplate(resource.Uri))
            {
                templates.Add(new ResourceTemplate { Name = resource.Name, UriTemplate = resource.Uri, Description = resource.Description, MimeType = resource.MimeType, });
            }
        }

        return templates;
    }

    public async Task<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Try resources from registered providers first.
        foreach (var provider in _resourceProviders)
        {
            var skillResource = (await provider.GetResourcesAsync())
                .FirstOrDefault(r => r.IsMatch(request.Params.Uri));

            if (skillResource is not null)
            {
                return await skillResource.ReadAsync(request, cancellationToken);
            }
        }

        // Try SDK-registered resources.
        var sdkResource = _sdkResources.FirstOrDefault(resource => resource.IsMatch(request.Params.Uri));
        if (sdkResource is not null)
        {
            return await sdkResource.ReadAsync(request, cancellationToken);
        }

        // Try catalog-managed resources.
        var uri = request.Params.Uri;
        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            throw new McpException($"Invalid resource URI format: '{uri}'. Expected format: scheme://itemId/path");
        }

        var afterScheme = uri[(schemeEnd + 3)..];
        var slashIndex = afterScheme.IndexOf('/');
        var itemId = slashIndex >= 0 ? afterScheme[..slashIndex] : afterScheme;
        var entry = (await _catalog.GetAllAsync(cancellationToken)).FirstOrDefault(resource => string.Equals(resource.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
        if (entry?.Resource?.Uri is null)
        {
            throw new McpException($"Resource '{uri}' not found.");
        }

        var handler = request.Services.GetKeyedService<IMcpResourceTypeHandler>(entry.Source);
        if (handler is null)
        {
            throw new McpException($"No handler found for resource type '{entry.Source}'.");
        }

        if (!McpResourceUri.IsTemplate(entry.Resource.Uri))
        {
            return await handler.ReadAsync(entry, new Dictionary<string, string>(), cancellationToken);
        }

        if (McpResourceUri.TryMatch(entry.Resource.Uri, uri, out var variables))
        {
            return await handler.ReadAsync(entry, variables, cancellationToken);
        }

        throw new McpException($"Resource URI '{uri}' does not match the expected pattern '{entry.Resource.Uri}'.");
    }

    private async Task<IList<Resource>> GetAllResourcesAsync()
    {
        var resources = (await _catalog.GetAllAsync()).Where(entry => entry.Resource != null).Select(entry => entry.Resource).ToList();

        // Include resources from registered providers (e.g., agent skill files).
        foreach (var provider in _resourceProviders)
        {
            foreach (var skillResource in await provider.GetResourcesAsync())
            {
                if (skillResource.ProtocolResource is not null && !resources.Any(resource => resource.Uri == skillResource.ProtocolResource.Uri))
                {
                    resources.Add(skillResource.ProtocolResource);
                }
            }
        }

        // Include resources registered via the MCP C# SDK.
        foreach (var sdkResource in _sdkResources)
        {
            if (sdkResource.ProtocolResource is not null && !resources.Any(resource => resource.Uri == sdkResource.ProtocolResource.Uri))
            {
                resources.Add(sdkResource.ProtocolResource);
            }
        }

        return resources;
    }
}
