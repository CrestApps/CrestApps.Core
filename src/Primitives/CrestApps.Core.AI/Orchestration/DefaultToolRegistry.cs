using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Aggregates tool entries from all registered <see cref="IToolRegistryProvider"/> instances
/// and provides relevance-based search using the shared <see cref="ITextTokenizer"/>.
/// </summary>
internal sealed class DefaultToolRegistry : IToolRegistry
{
    private readonly IEnumerable<IToolRegistryProvider> _providers;
    private readonly ITextTokenizer _tokenizer;
    private readonly ILogger<DefaultToolRegistry> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultToolRegistry"/> class.
    /// </summary>
    /// <param name="providers">The providers.</param>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="logger">The logger.</param>
    public DefaultToolRegistry(
        IEnumerable<IToolRegistryProvider> providers,
        ITextTokenizer tokenizer,
        ILogger<DefaultToolRegistry> logger)
    {
        _providers = providers;
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
        var allEntries = new List<ToolRegistryEntry>();

        foreach (var provider in _providers)
        {
            try
            {
                var entries = await provider.GetToolsAsync(context, cancellationToken);

                if (entries is not null && entries.Count > 0)
                {
                    allEntries.AddRange(entries);
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

        return allEntries;
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
}
