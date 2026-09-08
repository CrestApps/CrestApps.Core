using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Trims a tool set that is too large to hand a model wholesale down to the entries most relevant to a piece
/// of text, keeping the ones a request cannot do without.
/// </summary>
/// <remarks>
/// <para>
/// This is the lightweight (no model call) tier of the chat orchestrator's tool scoping, extracted so the
/// realtime orchestrator can apply the same judgement. The two callers differ only in what they score against:
/// chat scores against the user's message, because it has one; a realtime session is configured before anyone
/// has spoken, so it scores against the profile's own instructions — what the assistant is <em>for</em> — which
/// is the best available proxy for what it will be asked.
/// </para>
/// <para>
/// Relevance is token overlap between the scoring text and each tool's name and description, using the same
/// tokenizer as tool search, so a tool the model would find by searching is a tool scoping keeps.
/// </para>
/// </remarks>
public sealed class ToolRelevanceScoper
{
    private readonly ITextTokenizer _tokenizer;
    private readonly DefaultOrchestratorOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRelevanceScoper"/> class.
    /// </summary>
    /// <param name="tokenizer">The tokenizer shared with tool search.</param>
    /// <param name="options">The orchestrator options carrying the thresholds and budgets.</param>
    public ToolRelevanceScoper(ITextTokenizer tokenizer, DefaultOrchestratorOptions options)
    {
        _tokenizer = tokenizer;
        _options = options;
    }

    /// <summary>
    /// Gets the tool count above which a set is scoped rather than passed through whole.
    /// </summary>
    public int ScopingThreshold => _options.ScopingThreshold;

    /// <summary>
    /// Gets whether a tool set of the given size should be scoped.
    /// </summary>
    /// <param name="toolCount">The number of tools resolved for the request.</param>
    public bool ShouldScope(int toolCount) => toolCount > _options.ScopingThreshold;

    /// <summary>
    /// Selects the tools most relevant to <paramref name="scoringText"/>, always including
    /// <paramref name="mustIncludeToolNames"/>.
    /// </summary>
    /// <param name="scoringText">The text to score relevance against; when empty, tools are kept in their original order up to the cap.</param>
    /// <param name="mustIncludeToolNames">Tools that are kept regardless of relevance, such as the knowledge base search and any tool another tool depends on.</param>
    /// <param name="allTools">The full resolved tool set.</param>
    public IReadOnlyList<ToolRegistryEntry> Scope(
        string scoringText,
        IEnumerable<string> mustIncludeToolNames,
        IReadOnlyList<ToolRegistryEntry> allTools)
    {
        ArgumentNullException.ThrowIfNull(mustIncludeToolNames);
        ArgumentNullException.ThrowIfNull(allTools);

        var mustInclude = mustIncludeToolNames
            .Where(toolName => !string.IsNullOrWhiteSpace(toolName))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var mustIncludeEntries = allTools
            .Where(tool => mustInclude.Contains(tool.Name))
            .ToList();
        var candidates = allTools
            .Where(tool => !mustInclude.Contains(tool.Name))
            .ToList();

        // All tools are subject to relevance scoring; no source gets special treatment.
        var budget = _options.InitialToolCount;

        if (string.IsNullOrWhiteSpace(scoringText))
        {
            // Nothing to score against: keep the original order, up to the cap.
            return candidates
                .Take(Math.Max(budget, _options.MaxToolCount))
                .Concat(mustIncludeEntries)
                .ToList();
        }

        var scoringTokens = _tokenizer.Tokenize(scoringText);

        if (scoringTokens.Count == 0)
        {
            return candidates
                .Take(budget)
                .Concat(mustIncludeEntries)
                .ToList();
        }

        var scored = new List<(ToolRegistryEntry Entry, double Score)>(candidates.Count);

        foreach (var tool in candidates)
        {
            var title = tool.Name;

            if (!string.IsNullOrWhiteSpace(tool.Description))
            {
                title += ' ' + tool.Description;
            }

            var toolTokens = _tokenizer.Tokenize(title);

            if (toolTokens.Count == 0)
            {
                scored.Add((tool, 0));
                continue;
            }

            var matchCount = 0;

            foreach (var scoringToken in scoringTokens)
            {
                if (toolTokens.Contains(scoringToken))
                {
                    matchCount++;
                }
            }

            if (matchCount == 0)
            {
                scored.Add((tool, 0));
                continue;
            }

            // Max of forward and reverse ratios: forward measures how much of the text the tool covers, reverse
            // how much of the tool the text covers. Either alone under-ranks short tool names or long texts.
            var forwardScore = (double)matchCount / scoringTokens.Count;
            var reverseScore = (double)matchCount / toolTokens.Count;
            scored.Add((tool, Math.Max(forwardScore, reverseScore)));
        }

        var scoped = scored
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .Take(budget)
            .Select(s => s.Entry)
            .ToList();

        // Nothing matched at all: fall back to the original order rather than handing the model no tools.
        if (scoped.Count == 0 && budget > 0)
        {
            scoped = candidates.Take(budget).ToList();
        }

        foreach (var entry in mustIncludeEntries)
        {
            if (scoped.Any(existing => string.Equals(existing.Id, entry.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            scoped.Add(entry);
        }

        return scoped;
    }
}
