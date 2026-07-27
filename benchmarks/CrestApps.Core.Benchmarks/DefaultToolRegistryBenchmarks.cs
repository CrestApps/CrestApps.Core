using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures tool-registry relevance ranking with realistic descriptions, stable score ties,
/// and zero-score entries. This class must remain unsealed because BenchmarkDotNet generates
/// a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DefaultToolRegistrySearchBenchmarks
{
    private const string _query = "search customer documents and summarize account activity";

    private AICompletionContext _currentContext;
    private DefaultToolRegistry _currentRegistry;
    private AICompletionContext _legacyContext;
    private LegacyDefaultToolRegistry _legacyRegistry;

    /// <summary>
    /// Gets or sets the number of registry entries to search.
    /// </summary>
    [Params(100, 1000, 10000)]
    public int EntryCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of search results.
    /// </summary>
    [Params(5, 20)]
    public int TopK { get; set; }

    /// <summary>
    /// Creates equivalent legacy and current registries over immutable in-memory providers.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var entries = CreateSearchEntries(EntryCount);
        var providers = CreateProviders(entries);
        var options = new AIToolDefinitionOptions();
        var tokenizer = new LuceneTextTokenizer();
        _legacyContext = new AICompletionContext();
        _currentContext = new AICompletionContext();
        _legacyRegistry = new LegacyDefaultToolRegistry(
            providers,
            Options.Create(options),
            tokenizer,
            NullLogger<LegacyDefaultToolRegistry>.Instance);
        _currentRegistry = new DefaultToolRegistry(
            providers,
            Options.Create(options),
            tokenizer,
            NullLogger<DefaultToolRegistry>.Instance);

        var legacyResult = _legacyRegistry
            .SearchAsync(_query, TopK, _legacyContext)
            .GetAwaiter()
            .GetResult();
        var currentResult = _currentRegistry
            .SearchAsync(_query, TopK, _currentContext)
            .GetAwaiter()
            .GetResult();

        EnsureEquivalent(legacyResult, currentResult);
    }

    /// <summary>
    /// Searches entries with the implementation captured before the optimization experiments.
    /// </summary>
    /// <returns>The ranked entries.</returns>
    [Benchmark(Baseline = true)]
    public Task<IReadOnlyList<ToolRegistryEntry>> SearchLegacyAsync()
    {
        return _legacyRegistry.SearchAsync(_query, TopK, _legacyContext);
    }

    /// <summary>
    /// Searches entries with the production implementation.
    /// </summary>
    /// <returns>The ranked entries.</returns>
    [Benchmark]
    public Task<IReadOnlyList<ToolRegistryEntry>> SearchCurrentAsync()
    {
        return _currentRegistry.SearchAsync(_query, TopK, _currentContext);
    }

    private static IToolRegistryProvider[] CreateProviders(
        List<ToolRegistryEntry> entries)
    {
        const int providerCount = 4;

        var providerEntries = new List<ToolRegistryEntry>[providerCount];

        for (var i = 0; i < providerEntries.Length; i++)
        {
            providerEntries[i] = [];
        }

        for (var i = 0; i < entries.Count; i++)
        {
            providerEntries[i % providerCount].Add(entries[i]);
        }

        return providerEntries
            .Select(entriesForProvider => (IToolRegistryProvider)new BenchmarkToolRegistryProvider(entriesForProvider))
            .ToArray();
    }

    private static List<ToolRegistryEntry> CreateSearchEntries(int entryCount)
    {
        var entries = new List<ToolRegistryEntry>(entryCount);

        for (var i = 0; i < entryCount; i++)
        {
            var source = (ToolRegistryEntrySource)(i % 5);
            var entry = (i % 4) switch
            {
                0 => new ToolRegistryEntry
                {
                    Id = $"search-customer-documents-{i}",
                    Name = $"searchCustomerDocuments{i}",
                    Description = "Search customer documents and summarize account activity with filters, citations, and access controls.",
                    Source = source,
                },
                1 => new ToolRegistryEntry
                {
                    Id = $"review-account-history-{i}",
                    Name = $"reviewAccountHistory{i}",
                    Description = "Review customer account activity, transactions, balances, and recent support interactions.",
                    Source = source,
                },
                2 => new ToolRegistryEntry
                {
                    Id = $"schedule-team-meeting-{i}",
                    Name = $"scheduleTeamMeeting{i}",
                    Description = "Schedules a team meeting, checks attendee availability, and sends calendar invitations.",
                    Source = source,
                },
                _ => new ToolRegistryEntry
                {
                    Id = $"forecast-weather-{i}",
                    Name = $"forecastWeather{i}",
                    Description = "Returns a regional weather forecast with temperature, precipitation, and wind conditions.",
                    Source = source,
                },
            };

            entries.Add(entry);
        }

        return entries;
    }

    private static void EnsureEquivalent(
        IReadOnlyList<ToolRegistryEntry> legacy,
        IReadOnlyList<ToolRegistryEntry> current)
    {
        if (!legacy.Select(entry => entry.Id).SequenceEqual(current.Select(entry => entry.Id)))
        {
            throw new InvalidOperationException("Legacy and current search results differ.");
        }
    }
}

/// <summary>
/// Measures dependency expansion across fan-out, deep-chain, diamond, and shared-root graphs.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DefaultToolRegistryDependencyBenchmarks
{
    private AICompletionContext _currentContext;
    private DefaultToolRegistry _currentRegistry;
    private AICompletionContext _legacyContext;
    private LegacyDefaultToolRegistry _legacyRegistry;

    /// <summary>
    /// Gets or sets the dependency graph shape.
    /// </summary>
    [Params(
        DependencyGraphShape.FanOut,
        DependencyGraphShape.DeepChain,
        DependencyGraphShape.Diamond,
        DependencyGraphShape.ManyRoots)]
    public DependencyGraphShape GraphShape { get; set; }

    /// <summary>
    /// Gets or sets the approximate graph scale.
    /// </summary>
    [Params(100, 1000)]
    public int NodeCount { get; set; }

    /// <summary>
    /// Creates equivalent legacy and current registries for the selected graph.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var (options, entries) = CreateDependencyGraph(GraphShape, NodeCount);
        IToolRegistryProvider[] providers = [new BenchmarkToolRegistryProvider(entries)];
        var tokenizer = new EmptyTextTokenizer();
        _legacyContext = new AICompletionContext();
        _currentContext = new AICompletionContext();
        _legacyRegistry = new LegacyDefaultToolRegistry(
            providers,
            Options.Create(options),
            tokenizer,
            NullLogger<LegacyDefaultToolRegistry>.Instance);
        _currentRegistry = new DefaultToolRegistry(
            providers,
            Options.Create(options),
            tokenizer,
            NullLogger<DefaultToolRegistry>.Instance);

        var legacyResult = _legacyRegistry.GetAllAsync(_legacyContext).GetAwaiter().GetResult();
        var currentResult = _currentRegistry.GetAllAsync(_currentContext).GetAwaiter().GetResult();

        EnsureEquivalent(legacyResult, currentResult, _legacyContext, _currentContext);
    }

    /// <summary>
    /// Expands dependencies with the implementation captured before the optimization experiments.
    /// </summary>
    /// <returns>The expanded entries.</returns>
    [Benchmark(Baseline = true)]
    public Task<IReadOnlyList<ToolRegistryEntry>> ExpandLegacyAsync()
    {
        return _legacyRegistry.GetAllAsync(_legacyContext);
    }

    /// <summary>
    /// Expands dependencies with the production implementation.
    /// </summary>
    /// <returns>The expanded entries.</returns>
    [Benchmark]
    public Task<IReadOnlyList<ToolRegistryEntry>> ExpandCurrentAsync()
    {
        return _currentRegistry.GetAllAsync(_currentContext);
    }

    private static (AIToolDefinitionOptions Options, IReadOnlyList<ToolRegistryEntry> Entries)
        CreateDependencyGraph(
            DependencyGraphShape graphShape,
            int nodeCount)
    {
        return graphShape switch
        {
            DependencyGraphShape.FanOut => CreateFanOutGraph(nodeCount),
            DependencyGraphShape.DeepChain => CreateDeepChainGraph(nodeCount),
            DependencyGraphShape.Diamond => CreateDiamondGraph(nodeCount),
            DependencyGraphShape.ManyRoots => CreateManyRootsGraph(nodeCount),
            _ => throw new ArgumentOutOfRangeException(nameof(graphShape)),
        };
    }

    private static (AIToolDefinitionOptions Options, IReadOnlyList<ToolRegistryEntry> Entries)
        CreateDeepChainGraph(int nodeCount)
    {
        var options = new AIToolDefinitionOptions();
        var entries = new List<ToolRegistryEntry>(nodeCount);

        for (var i = 0; i < nodeCount; i++)
        {
            var name = $"chain-{i}";
            var definition = AddDefinition(options, name);

            if (i + 1 < nodeCount)
            {
                definition.AddDependency($"chain-{i + 1}");
            }

            entries.Add(CreateDependencyEntry(name));
        }

        return (options, entries);
    }

    private static (AIToolDefinitionOptions Options, IReadOnlyList<ToolRegistryEntry> Entries)
        CreateDiamondGraph(int nodeCount)
    {
        var diamondCount = Math.Max(1, nodeCount / 3);
        var options = new AIToolDefinitionOptions();
        var entries = new List<ToolRegistryEntry>((diamondCount * 3) + 1);
        var root = AddDefinition(options, "diamond-root");
        entries.Add(CreateDependencyEntry("diamond-root"));

        for (var i = 0; i < diamondCount; i++)
        {
            var leftName = $"diamond-left-{i}";
            var rightName = $"diamond-right-{i}";
            var sharedName = $"diamond-shared-{i}";
            root.AddDependency(leftName);
            root.AddDependency(rightName);
            AddDefinition(options, leftName).AddDependency(sharedName);
            AddDefinition(options, rightName).AddDependency(sharedName);
            AddDefinition(options, sharedName);
            entries.Add(CreateDependencyEntry(rightName));
            entries.Add(CreateDependencyEntry(sharedName));
            entries.Add(CreateDependencyEntry(leftName));
        }

        return (options, entries);
    }

    private static (AIToolDefinitionOptions Options, IReadOnlyList<ToolRegistryEntry> Entries)
        CreateFanOutGraph(int nodeCount)
    {
        var options = new AIToolDefinitionOptions();
        var entries = new List<ToolRegistryEntry>(nodeCount + 1);
        var root = AddDefinition(options, "fan-out-root");
        entries.Add(CreateDependencyEntry("fan-out-root"));

        for (var i = 0; i < nodeCount; i++)
        {
            var dependencyName = $"fan-out-dependency-{i}";
            root.AddDependency(dependencyName);
            AddDefinition(options, dependencyName);
            entries.Add(CreateDependencyEntry(dependencyName));
        }

        return (options, entries);
    }

    private static (AIToolDefinitionOptions Options, IReadOnlyList<ToolRegistryEntry> Entries)
        CreateManyRootsGraph(int nodeCount)
    {
        const int sharedDependencyCount = 10;

        var options = new AIToolDefinitionOptions();
        var entries = new List<ToolRegistryEntry>(nodeCount + sharedDependencyCount);
        var sharedNames = new string[sharedDependencyCount];

        for (var i = 0; i < sharedNames.Length; i++)
        {
            var sharedName = $"shared-dependency-{i}";
            sharedNames[i] = sharedName;
            AddDefinition(options, sharedName);
        }

        for (var i = 0; i < nodeCount; i++)
        {
            var rootName = $"shared-root-{i}";
            var root = AddDefinition(options, rootName);

            foreach (var sharedName in sharedNames)
            {
                root.AddDependency(sharedName);
            }

            entries.Add(CreateDependencyEntry(rootName));
        }

        foreach (var sharedName in sharedNames)
        {
            entries.Add(CreateDependencyEntry(sharedName));
        }

        return (options, entries);
    }

    private static AIToolDefinitionEntry AddDefinition(
        AIToolDefinitionOptions options,
        string name)
    {
        var definition = new AIToolDefinitionEntry(typeof(object))
        {
            Name = name,
        };
        options.SetTool(name, definition);

        return definition;
    }

    private static ToolRegistryEntry CreateDependencyEntry(string name)
    {
        return new ToolRegistryEntry
        {
            Id = name,
            Name = name,
            Description = $"Executes the {name} benchmark operation.",
            Source = ToolRegistryEntrySource.Local,
        };
    }

    private static void EnsureEquivalent(
        IReadOnlyList<ToolRegistryEntry> legacy,
        IReadOnlyList<ToolRegistryEntry> current,
        AICompletionContext legacyContext,
        AICompletionContext currentContext)
    {
        if (!legacy.Select(entry => entry.Id).SequenceEqual(current.Select(entry => entry.Id)))
        {
            throw new InvalidOperationException("Legacy and current dependency results differ.");
        }

        var legacyDependencies = GetDependencyNames(legacyContext);
        var currentDependencies = GetDependencyNames(currentContext);

        if (!legacyDependencies.SequenceEqual(currentDependencies))
        {
            throw new InvalidOperationException("Legacy and current dependency side effects differ.");
        }
    }

    private static IReadOnlyList<string> GetDependencyNames(AICompletionContext context)
    {
        if (!context.AdditionalProperties.TryGetValue(
            AICompletionContextKeys.DependencyToolNames,
            out var value))
        {
            return [];
        }

        return (IReadOnlyList<string>)value;
    }

    /// <summary>
    /// Identifies the dependency graph generated for a benchmark case.
    /// </summary>
    public enum DependencyGraphShape
    {
        /// <summary>
        /// One root with many direct dependencies.
        /// </summary>
        FanOut,

        /// <summary>
        /// A long single dependency chain.
        /// </summary>
        DeepChain,

        /// <summary>
        /// Repeated left/right branches that converge on shared dependencies.
        /// </summary>
        Diamond,

        /// <summary>
        /// Many roots that reference the same dependency set.
        /// </summary>
        ManyRoots,
    }
}

/// <summary>
/// Captures the pre-experiment tool registry so legacy and production paths run in one process.
/// </summary>
internal sealed class LegacyDefaultToolRegistry : IToolRegistry
{
    private readonly IEnumerable<IToolRegistryProvider> _providers;
    private readonly AIToolDefinitionOptions _toolOptions;
    private readonly ITextTokenizer _tokenizer;
    private readonly ILogger<LegacyDefaultToolRegistry> _logger;

    /// <summary>
    /// Initializes the legacy registry.
    /// </summary>
    /// <param name="providers">The registry providers.</param>
    /// <param name="toolOptions">The tool definitions.</param>
    /// <param name="tokenizer">The text tokenizer.</param>
    /// <param name="logger">The logger.</param>
    public LegacyDefaultToolRegistry(
        IEnumerable<IToolRegistryProvider> providers,
        IOptions<AIToolDefinitionOptions> toolOptions,
        ITextTokenizer tokenizer,
        ILogger<LegacyDefaultToolRegistry> logger)
    {
        _providers = providers;
        _toolOptions = toolOptions.Value;
        _tokenizer = tokenizer;
        _logger = logger;
    }

    /// <summary>
    /// Gets all entries with the pre-experiment dependency implementation.
    /// </summary>
    /// <param name="context">The completion context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The expanded entries.</returns>
    public async Task<IReadOnlyList<ToolRegistryEntry>> GetAllAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var availableEntries = await GetAvailableEntriesAsync(context, cancellationToken);

        if (availableEntries.Count == 0)
        {
            if (context is not null)
            {
                context.AdditionalProperties.Remove(AICompletionContextKeys.DependencyToolNames);
            }

            return availableEntries;
        }

        var entriesByName = availableEntries
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var resolvedEntries = new List<ToolRegistryEntry>();
        var resolvedEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencyToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in availableEntries)
        {
            AddEntryWithDependencies(
                entry,
                entriesByName,
                resolvedEntryIds,
                resolvedEntries,
                dependencyToolNames);
        }

        if (context is not null)
        {
            if (dependencyToolNames.Count > 0)
            {
                context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames] =
                    dependencyToolNames.ToArray();
            }
            else
            {
                context.AdditionalProperties.Remove(AICompletionContextKeys.DependencyToolNames);
            }
        }

        return resolvedEntries;
    }

    /// <summary>
    /// Searches entries with the pre-experiment ranking implementation.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="topK">The maximum result count.</param>
    /// <param name="context">The completion context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ranked entries.</returns>
    public async Task<IReadOnlyList<ToolRegistryEntry>> SearchAsync(
        string query,
        int topK,
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var allEntries = await GetAllAsync(context, cancellationToken);

        if (allEntries.Count == 0)
        {
            return [];
        }

        var queryTokens = _tokenizer.Tokenize(query);

        if (queryTokens.Count == 0)
        {
            return allEntries.Take(topK).ToList();
        }

        var scored = new List<(ToolRegistryEntry Entry, double Score)>();

        foreach (var entry in allEntries)
        {
            var score = ComputeRelevanceScore(queryTokens, entry);

            scored.Add((entry, score));
        }

        return scored
            .OrderByDescending(scoredEntry => scoredEntry.Score)
            .Take(topK)
            .Select(scoredEntry => scoredEntry.Entry)
            .ToList();
    }

    private double ComputeRelevanceScore(
        HashSet<string> queryTokens,
        ToolRegistryEntry entry)
    {
        var entryTokens = _tokenizer.Tokenize(
            entry.Name + " " + (entry.Description ?? string.Empty));

        if (entryTokens.Count == 0)
        {
            return 0;
        }

        var matchCount = 0;

        foreach (var queryToken in queryTokens)
        {
            if (entryTokens.Contains(queryToken))
            {
                matchCount++;
            }
        }

        if (matchCount == 0)
        {
            return 0;
        }

        var forwardScore = (double)matchCount / queryTokens.Count;
        var reverseScore = (double)matchCount / entryTokens.Count;

        return Math.Max(forwardScore, reverseScore);
    }

    private async Task<List<ToolRegistryEntry>> GetAvailableEntriesAsync(
        AICompletionContext context,
        CancellationToken cancellationToken)
    {
        var availableEntries = new List<ToolRegistryEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            try
            {
                var entries = await provider.GetToolsAsync(context, cancellationToken);

                if (entries is null || entries.Count == 0)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    var deduplicationKey = entry.Id ?? entry.Name;

                    if (string.IsNullOrWhiteSpace(deduplicationKey) ||
                        seen.Add(deduplicationKey))
                    {
                        availableEntries.Add(entry);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Tool registry provider {ProviderType} failed. Skipping.",
                    provider.GetType().Name);
            }
        }

        return availableEntries;
    }

    private void AddEntryWithDependencies(
        ToolRegistryEntry entry,
        IReadOnlyDictionary<string, List<ToolRegistryEntry>> entriesByName,
        ISet<string> resolvedEntryIds,
        ICollection<ToolRegistryEntry> resolvedEntries,
        ISet<string> dependencyToolNames)
    {
        var entryId = entry.Id ?? entry.Name;

        if (string.IsNullOrWhiteSpace(entryId) || !resolvedEntryIds.Add(entryId))
        {
            return;
        }

        resolvedEntries.Add(entry);

        if (!_toolOptions.Tools.TryGetValue(entry.Name, out var definition))
        {
            return;
        }

        foreach (var dependencyName in definition.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependencyName) ||
                !entriesByName.TryGetValue(dependencyName, out var dependencyEntries))
            {
                continue;
            }

            dependencyToolNames.Add(dependencyName);

            foreach (var dependencyEntry in dependencyEntries)
            {
                AddEntryWithDependencies(
                    dependencyEntry,
                    entriesByName,
                    resolvedEntryIds,
                    resolvedEntries,
                    dependencyToolNames);
            }
        }
    }
}

/// <summary>
/// Returns immutable in-memory entries for registry benchmarks.
/// </summary>
internal sealed class BenchmarkToolRegistryProvider : IToolRegistryProvider
{
    private readonly Task<IReadOnlyList<ToolRegistryEntry>> _entriesTask;

    /// <summary>
    /// Initializes a benchmark provider.
    /// </summary>
    /// <param name="entries">The entries returned by the provider.</param>
    public BenchmarkToolRegistryProvider(IReadOnlyList<ToolRegistryEntry> entries)
    {
        _entriesTask = Task.FromResult(entries);
    }

    /// <summary>
    /// Gets the prebuilt entry task.
    /// </summary>
    /// <param name="context">The completion context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The in-memory entries.</returns>
    public Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        return _entriesTask;
    }
}

/// <summary>
/// Returns no tokens for dependency-only benchmarks.
/// </summary>
internal sealed class EmptyTextTokenizer : ITextTokenizer
{
    /// <summary>
    /// Returns an empty token set.
    /// </summary>
    /// <param name="text">The ignored text.</param>
    /// <returns>An empty token set.</returns>
    public HashSet<string> Tokenize(string text)
    {
        return [];
    }
}
