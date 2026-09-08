using CrestApps.Core.AI;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Orchestration;

/// <summary>
/// The scoring shared by the chat and realtime orchestrators. Chat's behaviour is pinned by its own tests; these
/// cover the contract the realtime orchestrator relies on when it scopes against a profile's instructions.
/// </summary>
public sealed class ToolRelevanceScoperTests
{
    [Fact]
    public void ShouldScope_IsFalseAtTheThreshold_AndTrueAbove()
    {
        var scoper = Create(new DefaultOrchestratorOptions { ScopingThreshold = 30 });

        Assert.False(scoper.ShouldScope(30));
        Assert.True(scoper.ShouldScope(31));
    }

    [Fact]
    public void Scope_RanksByRelevanceToTheText_AndHonoursTheBudget()
    {
        var scoper = Create(new DefaultOrchestratorOptions { InitialToolCount = 2 });
        var tools = new[]
        {
            Entry("weather_forecast", "Get the weather forecast for a city."),
            Entry("lookup_customer_account", "Look up a customer account by phone or email."),
            Entry("update_customer_address", "Update the mailing address on a customer account."),
            Entry("generate_chart", "Render a chart from tabular data."),
        };

        var scoped = scoper.Scope("You help customers with their account and billing questions.", [], tools);

        Assert.Equal(2, scoped.Count);
        Assert.Contains(scoped, t => t.Name == "lookup_customer_account");
        Assert.Contains(scoped, t => t.Name == "update_customer_address");
    }

    [Fact]
    public void Scope_AlwaysKeepsMustIncludeTools_EvenWhenTheyScoreNothing()
    {
        // The knowledge base search is marked must-include by the data-source handler. Scoping a profile about
        // shipping must never drop it, or the assistant loses its knowledge base at session open.
        var scoper = Create(new DefaultOrchestratorOptions { InitialToolCount = 1 });
        var tools = new[]
        {
            Entry("track_shipment", "Track a shipment by its tracking number."),
            Entry("search_data_sources", "Semantic vector search over configured data sources."),
            Entry("weather_forecast", "Get the weather forecast for a city."),
        };

        var scoped = scoper.Scope("You help customers track their shipments.", ["search_data_sources"], tools);

        Assert.Contains(scoped, t => t.Name == "search_data_sources");
        Assert.Contains(scoped, t => t.Name == "track_shipment");
        Assert.Equal(2, scoped.Count);
    }

    [Fact]
    public void Scope_WithNothingToScoreAgainst_KeepsOriginalOrderUpToTheCap()
    {
        var scoper = Create(new DefaultOrchestratorOptions { InitialToolCount = 2, MaxToolCount = 3 });
        var tools = Enumerable.Range(1, 6).Select(i => Entry($"tool_{i}", $"Tool number {i}")).ToList();

        var scoped = scoper.Scope(scoringText: null, [], tools);

        // Empty text falls back to the larger of the two caps, in the order the tools were resolved.
        Assert.Equal(["tool_1", "tool_2", "tool_3"], scoped.Select(t => t.Name));
    }

    [Fact]
    public void Scope_WhenNothingMatches_FallsBackToOriginalOrderRatherThanNoTools()
    {
        var scoper = Create(new DefaultOrchestratorOptions { InitialToolCount = 2 });
        var tools = Enumerable.Range(1, 5).Select(i => Entry($"tool_{i}", $"Capability {i}")).ToList();

        var scoped = scoper.Scope("zebra quantum lighthouse", [], tools);

        Assert.Equal(["tool_1", "tool_2"], scoped.Select(t => t.Name));
    }

    private static ToolRelevanceScoper Create(DefaultOrchestratorOptions options)
        => new(new LuceneTextTokenizer(), options);

    private static ToolRegistryEntry Entry(string name, string description)
        => new() { Id = name, Name = name, Description = description, Source = ToolRegistryEntrySource.Local };
}
