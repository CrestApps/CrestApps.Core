using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.DataIngestion;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Tests.Core.Ingestion.Structure;

/// <summary>
/// Covers a host contributing a rung of its own.
/// </summary>
/// <remarks>
/// A corpus with a convention nobody outside the business knows — a form series whose first line names the
/// section, a ledger split by a rule of its own — is a strategy belonging to that host. The point of the
/// ladder being open is that such a host adds a rung rather than replacing the analyzer and losing the four
/// built-in ones with it.
/// </remarks>
public sealed class CustomStructureStrategyTests
{
    [Fact]
    public void Analyze_UsesAHostStrategyPlacedAboveTheBuiltInOnes()
    {
        // The document states headings, so the built-in ladder would divide it at those. A host rung placed
        // above them wins.
        var document = HtmlIngestionDocumentReader.Read(
            "<html><body><h1>Guide</h1><p>Intro.</p><h2>Install</h2><p>Steps.</p></body></html>",
            "https://example.test/guide");

        var structure = StructureAnalyzers
            .Default(new FirstLineStrategy(DocumentStructureOrders.Outline - 1))
            .Analyze(document);

        Assert.Equal(FirstLineStrategy.SourceName, structure.Source);
        Assert.Equal(["Guide"], structure.Articles.Select(article => article.Title));
    }

    [Fact]
    public void Analyze_FallsThroughToAHostStrategyPlacedBelowTheBuiltInOnes()
    {
        // Nothing here states a heading and nothing infers one, so every built-in rung declines and the
        // host's rung answers instead of the document becoming one undivided division.
        var document = new IngestionDocument("ledger.txt");
        var section = new IngestionDocumentSection();

        section.Elements.Add(new IngestionDocumentParagraph("Opening balance carried forward.")
        {
            Text = "Opening balance carried forward.",
        });

        document.Sections.Add(section);

        var structure = StructureAnalyzers
            .Default(new FirstLineStrategy(DocumentStructureOrders.InferredHeadings + 1))
            .Analyze(document);

        Assert.Equal(FirstLineStrategy.SourceName, structure.Source);
        Assert.Equal(["Opening balance carried forward."], structure.Articles.Select(article => article.Title));
    }

    [Fact]
    public void Analyze_TreatsAFailingStrategyAsHavingDeclined()
    {
        // A rung that throws must cost the document that rung, not its ingest.
        var document = HtmlIngestionDocumentReader.Read(
            "<html><body><h1>Guide</h1><p>Intro.</p><h2>Install</h2><p>Steps.</p></body></html>",
            "https://example.test/guide");

        var structure = StructureAnalyzers
            .Default(new ThrowingStrategy())
            .Analyze(document);

        Assert.Equal(DocumentStructureSources.StatedHeadings, structure.Source);
        Assert.Equal(["Guide", "Install"], structure.Articles.Select(article => article.Title));
    }

    /// <summary>
    /// A host rung that calls the document's first line its only division.
    /// </summary>
    private sealed class FirstLineStrategy : IDocumentStructureStrategy
    {
        public const string SourceName = "firstLine";

        public FirstLineStrategy(int order)
        {
            Order = order;
        }

        public string Source => SourceName;

        public int Order { get; }

        public IReadOnlyList<DocumentArticle> Divide(DocumentStructureContext context)
        {
            var first = context.Document.Sections
                .SelectMany(section => section.Elements)
                .Select(element => element.Text)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

            if (first is null)
            {
                return [];
            }

            return
            [
                new DocumentArticle
                {
                    Ordinal = 1,
                    Title = first,
                    PageStart = 1,
                    PageEnd = context.PageCount,
                },
            ];
        }
    }

    /// <summary>
    /// A host rung that is broken.
    /// </summary>
    private sealed class ThrowingStrategy : IDocumentStructureStrategy
    {
        public string Source => "throwing";

        public int Order => DocumentStructureOrders.Outline - 1;

        public IReadOnlyList<DocumentArticle> Divide(DocumentStructureContext context)
        {
            throw new InvalidOperationException("This strategy is deliberately broken.");
        }
    }
}
