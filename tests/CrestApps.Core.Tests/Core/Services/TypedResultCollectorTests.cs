using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Verifies what the figures block tells the model about values it must not quote.
/// </summary>
/// <remarks>
/// A chart whose numbers were never read off the file carries a note saying so, and the note alone proved
/// too weak. Asked whether a chart's values were machine-readable, a model shown
/// <c>values: descriptive - not machine-readable</c> replied that they "might not be immediately
/// machine-readable" and offered to extract them — true of a picture in the abstract, and not an answer
/// about what this knowledge base holds, which is nothing.
/// </remarks>
public sealed class TypedResultCollectorTests
{
    [Fact]
    public void Render_ForADescriptiveChart_TellsTheModelNotToQuoteOrOfferToExtract()
    {
        var rendered = Render(CreateChart(ChartValueConfidence.Descriptive));

        Assert.Contains("descriptive or unconfirmed", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not offer to extract", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not call them convertible", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_ForAChartWithNoStatedConfidence_SaysTheSame()
    {
        // Silence is not a claim of exactness: not every provider carries the flag back, so an unmarked
        // chart is treated as read off by eye.
        var rendered = Render(CreateChart(null));

        Assert.Contains("do not offer to extract", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_ForAnExactChart_DoesNotForbidQuotingIt()
    {
        // A chart whose values came from the file's own geometry is exactly the one worth quoting, and the
        // instruction must not reach it.
        var rendered = Render(CreateChart(ChartValueConfidence.Exact));

        Assert.DoesNotContain("do not offer to extract", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_ForAPlainFigure_DoesNotForbidQuotingIt()
    {
        // A photograph carries no values at all; the instruction is about charts that look quotable.
        var figure = CreateChart(null);
        figure.ContentType = KnowledgeObjectTypes.Figure;

        var rendered = Render(figure);

        Assert.DoesNotContain("do not offer to extract", rendered, StringComparison.OrdinalIgnoreCase);
    }

    private static string Render(DataSourceSearchResult result)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var scope = AIInvocationScope.Begin();

        var collector = new TypedResultCollector(
            services,
            "data-source-1",
            AIDataSourceSourceTypes.File,
            AIInvocationScope.Current,
            NullLogger.Instance);

        collector.Collect([result]);

        return collector.Render();
    }

    private static DataSourceSearchResult CreateChart(string confidence)
    {
        var result = new DataSourceSearchResult
        {
            ReferenceId = "figure:abc:4:3",
            ReferenceType = AIDataSourceSourceTypes.File,
            ContentType = KnowledgeObjectTypes.Chart,
            ChunkIndex = 0,
            Title = "2. ábra. Az épületek felszereltsége tipológia alapján",
            Content = "A bar chart of equipment by building typology.",
            Page = 4,
            Score = 0.5f,
        };

        if (confidence is not null)
        {
            result.Filters = new Dictionary<string, object>
            {
                ["valueConfidence"] = confidence,
            };
        }

        return result;
    }
}
