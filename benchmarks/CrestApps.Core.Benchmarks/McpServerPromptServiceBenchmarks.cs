using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures MCP prompt aggregation and name de-duplication.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class McpServerPromptServiceBenchmarks
{
    private BenchmarkNamedCatalog _catalog;
    private BenchmarkPromptProvider _provider;
    private DefaultMcpServerPromptService _service;
    private IReadOnlyList<McpServerPrompt> _sdkPrompts;

    /// <summary>
    /// Gets or sets the number of prompts supplied by each source.
    /// </summary>
    [Params(100, 1000)]
    public int PromptCount { get; set; }

    /// <summary>
    /// Creates catalog, provider, and SDK prompts with overlapping names.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var catalogPrompts = Enumerable.Range(0, PromptCount)
            .Select(index => new McpPrompt
            {
                ItemId = $"catalog-{index}",
                Name = $"prompt-{index}",
                Prompt = new Prompt
                {
                    Name = $"prompt-{index}",
                },
            })
            .ToArray();

        var providerPrompts = Enumerable.Range(PromptCount / 2, PromptCount)
            .Select(index => CreateServerPrompt($"prompt-{index}"))
            .ToArray();
        _sdkPrompts = Enumerable.Range(PromptCount, PromptCount)
            .Select(index => CreateServerPrompt($"prompt-{index}"))
            .ToArray();
        _catalog = new BenchmarkNamedCatalog(catalogPrompts);
        _provider = new BenchmarkPromptProvider(providerPrompts);
        _service = new DefaultMcpServerPromptService(_catalog, [_provider], _sdkPrompts);
    }

    /// <summary>
    /// Lists merged prompts with the original repeated linear name scans.
    /// </summary>
    /// <returns>The merged prompts.</returns>
    [Benchmark(Baseline = true)]
    public async Task<IList<Prompt>> ListBufferedAsync()
    {
        var prompts = (await _catalog.GetAllAsync())
            .Where(entry => entry.Prompt != null)
            .Select(entry => entry.Prompt)
            .ToList();

        foreach (var prompt in await _provider.GetPromptsAsync())
        {
            if (!prompts.Any(existing => existing.Name == prompt.ProtocolPrompt.Name))
            {
                prompts.Add(prompt.ProtocolPrompt);
            }
        }

        foreach (var prompt in _sdkPrompts)
        {
            if (!prompts.Any(existing => existing.Name == prompt.ProtocolPrompt.Name))
            {
                prompts.Add(prompt.ProtocolPrompt);
            }
        }

        return prompts;
    }

    /// <summary>
    /// Lists merged prompts with hash-based name de-duplication.
    /// </summary>
    /// <returns>The merged prompts.</returns>
    [Benchmark]
    public Task<IList<Prompt>> ListOptimizedAsync()
    {
        return _service.ListAsync();
    }

    private static McpServerPrompt CreateServerPrompt(string name)
    {
        return McpServerPrompt.Create(
            (Func<string>)(() => string.Empty),
            new McpServerPromptCreateOptions
            {
                Name = name,
            });
    }

    private sealed class BenchmarkPromptProvider : IMcpPromptProvider
    {
        private readonly IReadOnlyList<McpServerPrompt> _prompts;

        public BenchmarkPromptProvider(IReadOnlyList<McpServerPrompt> prompts)
        {
            _prompts = prompts;
        }

        public Task<IReadOnlyList<McpServerPrompt>> GetPromptsAsync()
        {
            return Task.FromResult(_prompts);
        }
    }

    private sealed class BenchmarkNamedCatalog : INamedCatalog<McpPrompt>
    {
        private readonly IReadOnlyCollection<McpPrompt> _prompts;

        public BenchmarkNamedCatalog(IReadOnlyCollection<McpPrompt> prompts)
        {
            _prompts = prompts;
        }

        public ValueTask<IReadOnlyCollection<McpPrompt>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_prompts);
        }

        public ValueTask<McpPrompt> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<McpPrompt> FindByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyCollection<McpPrompt>> GetAsync(
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<PageResult<McpPrompt>> PageAsync<TQuery>(
            int page,
            int pageSize,
            TQuery context,
            CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> DeleteAsync(McpPrompt entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask CreateAsync(McpPrompt entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask UpdateAsync(McpPrompt entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
