using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures MCP resource aggregation and URI de-duplication.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class McpServerResourceServiceBenchmarks
{
    private BenchmarkSourceCatalog _catalog;
    private BenchmarkResourceProvider _provider;
    private DefaultMcpServerResourceService _service;
    private IReadOnlyList<McpServerResource> _sdkResources;

    /// <summary>
    /// Gets or sets the number of resources supplied by each source.
    /// </summary>
    [Params(100, 1000)]
    public int ResourceCount { get; set; }

    /// <summary>
    /// Creates catalog, provider, and SDK resources with overlapping URIs.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var catalogResources = Enumerable.Range(0, ResourceCount)
            .Select(index => new McpResource
            {
                ItemId = $"catalog-{index}",
                Source = "benchmark",
                Resource = new Resource
                {
                    Name = $"catalog-{index}",
                    Uri = $"resource://{index}",
                },
            })
            .ToArray();

        var providerResources = Enumerable.Range(ResourceCount / 2, ResourceCount)
            .Select(index => CreateServerResource($"provider-{index}", $"resource://{index}"))
            .ToArray();
        _sdkResources = Enumerable.Range(ResourceCount, ResourceCount)
            .Select(index => CreateServerResource($"sdk-{index}", $"resource://{index}"))
            .ToArray();
        _catalog = new BenchmarkSourceCatalog(catalogResources);
        _provider = new BenchmarkResourceProvider(providerResources);

        _service = new DefaultMcpServerResourceService(
            _catalog,
            [_provider],
            _sdkResources);
    }

    /// <summary>
    /// Lists merged resources with the original repeated linear URI scans.
    /// </summary>
    /// <returns>The merged resources.</returns>
    [Benchmark(Baseline = true)]
    public async Task<IList<Resource>> ListBufferedAsync()
    {
        var resources = (await _catalog.GetAllAsync())
            .Where(entry => entry.Resource is not null)
            .Select(entry => entry.Resource)
            .ToList();

        foreach (var resource in await _provider.GetResourcesAsync())
        {
            if (resource.ProtocolResource is not null &&
                !resources.Any(existing => existing.Uri == resource.ProtocolResource.Uri))
            {
                resources.Add(resource.ProtocolResource);
            }
        }

        foreach (var resource in _sdkResources)
        {
            if (resource.ProtocolResource is not null &&
                !resources.Any(existing => existing.Uri == resource.ProtocolResource.Uri))
            {
                resources.Add(resource.ProtocolResource);
            }
        }

        return resources
            .Where(resource => resource.Uri is null || !McpResourceUri.IsTemplate(resource.Uri))
            .ToList();
    }

    /// <summary>
    /// Lists the merged concrete resources with hash-based URI de-duplication.
    /// </summary>
    /// <returns>The merged resources.</returns>
    [Benchmark]
    public Task<IList<Resource>> ListOptimizedAsync()
    {
        return _service.ListAsync();
    }

    private static McpServerResource CreateServerResource(string name, string uri)
    {
        return McpServerResource.Create(
            (Func<string>)(() => string.Empty),
            new McpServerResourceCreateOptions
            {
                Name = name,
                UriTemplate = uri,
            });
    }

    private sealed class BenchmarkResourceProvider : IMcpResourceProvider
    {
        private readonly IReadOnlyList<McpServerResource> _resources;

        public BenchmarkResourceProvider(IReadOnlyList<McpServerResource> resources)
        {
            _resources = resources;
        }

        public Task<IReadOnlyList<McpServerResource>> GetResourcesAsync()
        {
            return Task.FromResult(_resources);
        }
    }

    private sealed class BenchmarkSourceCatalog : ISourceCatalog<McpResource>
    {
        private readonly IReadOnlyCollection<McpResource> _resources;

        public BenchmarkSourceCatalog(IReadOnlyCollection<McpResource> resources)
        {
            _resources = resources;
        }

        public ValueTask<IReadOnlyCollection<McpResource>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_resources);
        }

        public ValueTask<McpResource> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyCollection<McpResource>> GetAsync(
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyCollection<McpResource>> GetAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<PageResult<McpResource>> PageAsync<TQuery>(
            int page,
            int pageSize,
            TQuery context,
            CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> DeleteAsync(McpResource entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask CreateAsync(McpResource entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask UpdateAsync(McpResource entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
