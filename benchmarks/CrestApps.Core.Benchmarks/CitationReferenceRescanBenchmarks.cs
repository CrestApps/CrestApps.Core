using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Quantifies the cost of the <see cref="CitationReferenceCollector"/> full-dictionary rescan against
/// a hypothetical incremental rescan that resolves only newly added references. Streaming responses
/// invoke the collector once per tool-reference add-event, so the production path re-scans the whole
/// reference map on each event. This benchmark isolates where a material gain would exist (references
/// whose link never resolves) from where the current path is already optimal (references that resolve
/// on their first pass). This class must remain unsealed because BenchmarkDotNet generates a derived
/// benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class CitationReferenceRescanBenchmarks
{
    private const string BenchReferenceType = "benchmark-reference";

    private CompositeAIReferenceLinkResolver _composite;
    private CitationReferenceCollector _collector;
    private (string Key, AICompletionReference Reference)[] _events;

    /// <summary>
    /// Gets or sets whether the resolver returns a link (resolved) or <c>null</c> (unresolved).
    /// </summary>
    [Params(ReferenceResolution.Resolved, ReferenceResolution.Unresolved)]
    public ReferenceResolution Resolution { get; set; }

    /// <summary>
    /// Gets or sets the number of references that accumulate across streaming add-events.
    /// </summary>
    [Params(16, 64, 256)]
    public int ReferenceCount { get; set; }

    /// <summary>
    /// Builds stable benchmark inputs and verifies the incremental candidate produces output that is
    /// identical to the production collector for a deterministic resolver.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var returnsLink = Resolution == ReferenceResolution.Resolved;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IAIReferenceLinkResolver>(
            BenchReferenceType,
            new BenchmarkReferenceLinkResolver(returnsLink));
        services.AddSingleton<CompositeAIReferenceLinkResolver>();
        var serviceProvider = services.BuildServiceProvider();

        _composite = serviceProvider.GetRequiredService<CompositeAIReferenceLinkResolver>();
        _collector = new CitationReferenceCollector(_composite);
        _events = new (string, AICompletionReference)[ReferenceCount];

        for (var index = 0; index < ReferenceCount; index++)
        {
            var key = $"[ref-{index}]";
            _events[index] = (key, new AICompletionReference
            {
                Index = index + 1,
                ReferenceId = $"ref-{index}",
                ReferenceType = BenchReferenceType,
                Title = $"Reference {index}",
            });
        }

        EnsureEquivalent(Current(), Candidate());
    }

    /// <summary>
    /// Reproduces the production streaming path: add one tool reference per event and re-run the
    /// collector, which rescans the entire reference map on every event.
    /// </summary>
    /// <returns>The accumulated references keyed by citation token.</returns>
    [Benchmark(Baseline = true)]
    public Dictionary<string, AICompletionReference> Current()
    {
        var references = new Dictionary<string, AICompletionReference>();
        var contentItemIds = new HashSet<string>();

        using var scope = AIInvocationScope.Begin();
        var toolReferences = scope.Context.ToolReferences;

        for (var index = 0; index < _events.Length; index++)
        {
            var (key, template) = _events[index];
            toolReferences[key] = Clone(template);
            _collector.CollectToolReferences(references, contentItemIds);
        }

        return references;
    }

    /// <summary>
    /// Models an incremental rescan that resolves only the newly added reference. This changes the
    /// resolver call-count for unresolved references from quadratic to linear.
    /// </summary>
    /// <returns>The accumulated references keyed by citation token.</returns>
    [Benchmark]
    public Dictionary<string, AICompletionReference> Candidate()
    {
        var references = new Dictionary<string, AICompletionReference>();
        var contentItemIds = new HashSet<string>();

        for (var index = 0; index < _events.Length; index++)
        {
            var (key, template) = _events[index];

            if (references.TryAdd(key, Clone(template)))
            {
                ResolveSingle(references[key], contentItemIds);
            }
        }

        return references;
    }

    private static AICompletionReference Clone(AICompletionReference template)
    {
        return new AICompletionReference
        {
            Index = template.Index,
            ReferenceId = template.ReferenceId,
            ReferenceType = template.ReferenceType,
            Title = template.Title,
        };
    }

    private void ResolveSingle(AICompletionReference reference, HashSet<string> contentItemIds)
    {
        if (string.IsNullOrEmpty(reference.Link) &&
            !string.IsNullOrEmpty(reference.ReferenceId) &&
            !string.IsNullOrEmpty(reference.ReferenceType))
        {
            reference.Link = _composite.ResolveLink(
                reference.ReferenceId,
                reference.ReferenceType,
                new Dictionary<string, object>
                {
                    ["Title"] = reference.Title,
                });
        }

        if (!string.IsNullOrEmpty(reference.ReferenceId) &&
            string.Equals(reference.ReferenceType, IndexProfileTypes.Articles, StringComparison.OrdinalIgnoreCase))
        {
            contentItemIds.Add(reference.ReferenceId);
        }
    }

    private static void EnsureEquivalent(
        Dictionary<string, AICompletionReference> current,
        Dictionary<string, AICompletionReference> candidate)
    {
        if (current.Count != candidate.Count)
        {
            throw new InvalidOperationException(
                $"Reference count mismatch: current={current.Count}, candidate={candidate.Count}.");
        }

        foreach (var (key, reference) in current)
        {
            if (!candidate.TryGetValue(key, out var other) ||
                !string.Equals(reference.Link, other.Link, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Resolved link mismatch for reference '{key}'.");
            }
        }
    }

    /// <summary>
    /// Identifies whether the benchmark resolver produces a link or leaves the reference unresolved.
    /// </summary>
    public enum ReferenceResolution
    {
        /// <summary>
        /// The resolver returns a non-empty link, so each reference is resolved exactly once.
        /// </summary>
        Resolved,

        /// <summary>
        /// The resolver returns <c>null</c>, so the production path re-attempts the reference on
        /// every subsequent rescan.
        /// </summary>
        Unresolved,
    }

    private sealed class BenchmarkReferenceLinkResolver : IAIReferenceLinkResolver
    {
        private readonly bool _returnsLink;

        public BenchmarkReferenceLinkResolver(bool returnsLink)
        {
            _returnsLink = returnsLink;
        }

        public string ResolveLink(string referenceId, IDictionary<string, object> metadata)
        {
            return _returnsLink
                ? $"/link/{referenceId}"
                : null;
        }
    }
}
