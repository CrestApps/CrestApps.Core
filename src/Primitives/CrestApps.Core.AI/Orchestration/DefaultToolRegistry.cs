using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Provides dependency-aware tool registry lookups and relevance-based search using the shared
/// <see cref="ITextTokenizer"/>.
/// </summary>
internal sealed class DefaultToolRegistry : IToolRegistry
{
    private readonly IEnumerable<IToolRegistryProvider> _providers;
    private readonly AIToolDefinitionOptions _toolOptions;
    private readonly ITextTokenizer _tokenizer;
    private readonly ILogger<DefaultToolRegistry> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultToolRegistry"/> class.
    /// </summary>
    /// <param name="providers">The tool registry providers.</param>
    /// <param name="toolOptions">The tool definition options.</param>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="logger">The logger.</param>
    public DefaultToolRegistry(
        IEnumerable<IToolRegistryProvider> providers,
        IOptions<AIToolDefinitionOptions> toolOptions,
        ITextTokenizer tokenizer,
        ILogger<DefaultToolRegistry> logger)
    {
        _providers = providers;
        _toolOptions = toolOptions.Value;
        _tokenizer = tokenizer;
        _logger = logger;
    }

    /// <summary>
    /// Gets all.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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
            AddEntryWithDependencies(entry, entriesByName, resolvedEntryIds, resolvedEntries, dependencyToolNames);
        }

        if (context is not null)
        {
            if (dependencyToolNames.Count > 0)
            {
                context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames] = dependencyToolNames.ToArray();
            }
            else
            {
                context.AdditionalProperties.Remove(AICompletionContextKeys.DependencyToolNames);
            }
        }

        return resolvedEntries;
    }

    /// <summary>
    /// Searchs the operation.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="topK">The top k.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => s.Entry)
            .ToList();
    }

    private double ComputeRelevanceScore(HashSet<string> queryTokens, ToolRegistryEntry entry)
    {
        var entryTokens = _tokenizer.Tokenize(entry.Name + " " + (entry.Description ?? string.Empty));

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

        // Use max of forward and reverse ratios for better recall.
        // Forward measures how well the query covers the entry;
        // reverse measures how well the entry covers the query.
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

                    if (string.IsNullOrWhiteSpace(deduplicationKey) || seen.Add(deduplicationKey))
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
                _logger.LogWarning(ex, "Tool registry provider {ProviderType} failed. Skipping.", provider.GetType().Name);
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
