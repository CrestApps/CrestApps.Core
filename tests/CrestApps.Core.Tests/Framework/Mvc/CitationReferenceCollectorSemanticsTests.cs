using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Characterizes the exact <see cref="IAIReferenceLinkResolver"/> call-count semantics of
/// <see cref="CitationReferenceCollector"/> when references accumulate across many streaming
/// add-events. These tests intentionally pin the current rescan behavior so any future refactor
/// that changes how often the resolver is invoked (for example, an "incremental" rescan that only
/// resolves newly added references) is caught, because such a change is only observationally safe
/// for a deterministic, side-effect-free resolver.
/// </summary>
public sealed class CitationReferenceCollectorSemanticsTests
{
    private const string ResolvableType = "custom-resolvable";

    [Fact]
    public void ResolvedReference_IsResolvedExactlyOnce_AcrossSubsequentAddEvents()
    {
        var resolver = new CountingReferenceLinkResolver(id => $"/link/{id}");
        var collector = CreateCollector(resolver);
        var references = new Dictionary<string, AICompletionReference>();
        var contentItemIds = new HashSet<string>();

        using var scope = AIInvocationScope.Begin();
        var toolReferences = scope.Context.ToolReferences;

        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "a");
        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "b");
        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "c");

        // A resolved reference has its Link populated on the first pass, so the empty-Link guard
        // skips it on every later rescan: each reference is resolved exactly once.
        Assert.Equal(1, resolver.CallCountFor("a"));
        Assert.Equal(1, resolver.CallCountFor("b"));
        Assert.Equal(1, resolver.CallCountFor("c"));
        Assert.Equal(3, resolver.TotalCalls);
        Assert.Equal("/link/a", references["[a]"].Link);
        Assert.Equal("/link/b", references["[b]"].Link);
        Assert.Equal("/link/c", references["[c]"].Link);
    }

    [Fact]
    public void UnresolvedReference_IsReattemptedOnEachSubsequentAddEvent()
    {
        // A resolver that returns null (no keyed resolver, or a resolver that cannot build a link)
        // leaves Link empty, so the empty-Link guard re-invokes the resolver on every later rescan.
        var resolver = new CountingReferenceLinkResolver(_ => null);
        var collector = CreateCollector(resolver);
        var references = new Dictionary<string, AICompletionReference>();
        var contentItemIds = new HashSet<string>();

        using var scope = AIInvocationScope.Begin();
        var toolReferences = scope.Context.ToolReferences;

        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "a");
        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "b");
        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "c");

        // "a" is present for three rescans, "b" for two, "c" for one. This quadratic retry pattern
        // is the exact behavior an incremental rescan would change, and is only output-equivalent
        // when the resolver is deterministic and side-effect-free.
        Assert.Equal(3, resolver.CallCountFor("a"));
        Assert.Equal(2, resolver.CallCountFor("b"));
        Assert.Equal(1, resolver.CallCountFor("c"));
        Assert.Equal(6, resolver.TotalCalls);
        Assert.Null(references["[a]"].Link);
        Assert.Null(references["[b]"].Link);
        Assert.Null(references["[c]"].Link);
    }

    [Fact]
    public void PreemptiveUnresolvedReference_IsReattempted_WhenToolReferenceIsAdded()
    {
        var resolver = new CountingReferenceLinkResolver(_ => null);
        var collector = CreateCollector(resolver);
        var references = new Dictionary<string, AICompletionReference>();
        var contentItemIds = new HashSet<string>();

        var context = new OrchestrationContext();
        context.Properties["DataSourceReferences"] = new Dictionary<string, AICompletionReference>
        {
            ["[p]"] = CreateReference("p"),
        };
        collector.CollectPreemptiveReferences(context, references, contentItemIds);

        Assert.Equal(1, resolver.CallCountFor("p"));

        using var scope = AIInvocationScope.Begin();
        var toolReferences = scope.Context.ToolReferences;
        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "t");

        // Adding a new tool reference triggers a full rescan that re-attempts the still-unresolved
        // preemptive reference as well.
        Assert.Equal(2, resolver.CallCountFor("p"));
        Assert.Equal(1, resolver.CallCountFor("t"));
    }

    [Fact]
    public void CollectToolReferences_WithoutNewReferences_DoesNotRescanOrReinvokeResolver()
    {
        var resolver = new CountingReferenceLinkResolver(_ => null);
        var collector = CreateCollector(resolver);
        var references = new Dictionary<string, AICompletionReference>();
        var contentItemIds = new HashSet<string>();

        using var scope = AIInvocationScope.Begin();
        var toolReferences = scope.Context.ToolReferences;
        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "a");

        Assert.Equal(1, resolver.CallCountFor("a"));

        // Re-collecting with no newly added tool references returns false and performs no rescan,
        // so the still-unresolved reference is not re-attempted until a new reference appears.
        var addedAgain = collector.CollectToolReferences(references, contentItemIds);

        Assert.False(addedAgain);
        Assert.Equal(1, resolver.CallCountFor("a"));
    }

    [Fact]
    public void ResolverOutput_IsIndependentOfCallCount_ForDeterministicResolver()
    {
        var resolver = new CountingReferenceLinkResolver(id => $"/link/{id}");
        var collector = CreateCollector(resolver);
        var references = new Dictionary<string, AICompletionReference>();
        var contentItemIds = new HashSet<string>();

        var context = new OrchestrationContext();
        context.Properties["DataSourceReferences"] = new Dictionary<string, AICompletionReference>
        {
            ["[article]"] = new()
            {
                Index = 1,
                ReferenceId = "article-1",
                ReferenceType = IndexProfileTypes.Articles,
                Title = "Intro",
            },
        };
        collector.CollectPreemptiveReferences(context, references, contentItemIds);

        using var scope = AIInvocationScope.Begin();
        var toolReferences = scope.Context.ToolReferences;
        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "a");
        AddToolReferenceAndCollect(collector, toolReferences, references, contentItemIds, "b");

        // Regardless of how many times the resolver was invoked, the observable output is stable
        // for a deterministic resolver: resolved links are set and article ids are recorded once.
        Assert.Equal("/link/article-1", references["[article]"].Link);
        Assert.Equal("/link/a", references["[a]"].Link);
        Assert.Equal("/link/b", references["[b]"].Link);
        Assert.Single(contentItemIds);
        Assert.Contains("article-1", contentItemIds);
    }

    private static void AddToolReferenceAndCollect(
        CitationReferenceCollector collector,
        Dictionary<string, AICompletionReference> toolReferences,
        Dictionary<string, AICompletionReference> references,
        HashSet<string> contentItemIds,
        string id)
    {
        toolReferences[$"[{id}]"] = CreateReference(id);
        collector.CollectToolReferences(references, contentItemIds);
    }

    private static AICompletionReference CreateReference(string id)
    {
        return new AICompletionReference
        {
            ReferenceId = id,
            ReferenceType = ResolvableType,
            Title = id,
        };
    }

    private static CitationReferenceCollector CreateCollector(IAIReferenceLinkResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(ResolvableType, resolver);
        services.AddKeyedSingleton(IndexProfileTypes.Articles, resolver);
        services.AddSingleton<CompositeAIReferenceLinkResolver>();
        var serviceProvider = services.BuildServiceProvider();

        return new CitationReferenceCollector(serviceProvider.GetRequiredService<CompositeAIReferenceLinkResolver>());
    }

    private sealed class CountingReferenceLinkResolver : IAIReferenceLinkResolver
    {
        private readonly Func<string, string> _resolve;
        private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);

        public CountingReferenceLinkResolver(Func<string, string> resolve)
        {
            _resolve = resolve;
        }

        public int TotalCalls { get; private set; }

        public int CallCountFor(string referenceId)
        {
            return _calls.TryGetValue(referenceId, out var count)
                ? count
                : 0;
        }

        public string ResolveLink(string referenceId, IDictionary<string, object> metadata)
        {
            TotalCalls++;
            _calls[referenceId] = CallCountFor(referenceId) + 1;

            return _resolve(referenceId);
        }
    }
}
