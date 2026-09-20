using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.AI.Models;
using CrestApps.Core.DataIngestion;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Tests.Core.Ingestion.Structure;

/// <summary>
/// Covers what a nested division is stored as.
/// </summary>
/// <remarks>
/// Working out the nesting is only half of it. Storing a section as another article would leave retrieval
/// unable to tell the part from the whole, which is the reason for computing the nesting at all.
/// </remarks>
public sealed class NestedKnowledgeObjectTests
{
    [Fact]
    public void Build_StoresANestedDivisionAsASection()
    {
        var objects = BuildGuide();

        var article = Assert.Single(objects, entry => entry.ObjectType == KnowledgeObjectTypes.Article);
        var sections = objects.Where(entry => entry.ObjectType == KnowledgeObjectTypes.Section).ToList();

        Assert.Equal("Guide", article.Title);
        Assert.Equal(["Install", "Upgrade"], sections.Select(section => section.Title));
    }

    [Fact]
    public void Build_HangsASectionOffTheDivisionContainingIt()
    {
        var objects = BuildGuide();

        var article = Assert.Single(objects, entry => entry.ObjectType == KnowledgeObjectTypes.Article);

        foreach (var section in objects.Where(entry => entry.ObjectType == KnowledgeObjectTypes.Section))
        {
            // A section belongs to its chapter, not to the document. Hanging it off the root would make the
            // nesting the analyzer worked out invisible to anything reading the store back.
            Assert.Equal(article.CanonicalId, section.ParentId);
        }
    }

    [Fact]
    public void Build_KeepsASectionsOwnTextOutOfItsParent()
    {
        var objects = BuildGuide();

        var article = Assert.Single(objects, entry => entry.ObjectType == KnowledgeObjectTypes.Article);
        var install = objects.Single(entry => entry.Title == "Install");

        Assert.Contains("Steps.", install.Content, StringComparison.Ordinal);

        // The chapter keeps its own introduction and nothing belonging to the sections beneath it, or the
        // document's text would be stored once per level of nesting.
        Assert.Contains("Intro.", article.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Steps.", article.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StoresAFlatDocumentAsArticlesOnly()
    {
        // Nothing nested means nothing stored as a section, so a document that was never hierarchical is
        // stored exactly as it was before sections existed.
        var document = new IngestionDocument("notes.txt");
        var section = new IngestionDocumentSection();

        section.Elements.Add(new IngestionDocumentParagraph("Just a paragraph.")
        {
            Text = "Just a paragraph.",
        });

        document.Sections.Add(section);

        var objects = Build(document);

        Assert.DoesNotContain(objects, entry => entry.ObjectType == KnowledgeObjectTypes.Section);
        Assert.Contains(objects, entry => entry.ObjectType == KnowledgeObjectTypes.Article);
    }

    private static IReadOnlyList<KnowledgeObject> BuildGuide()
    {
        var document = HtmlIngestionDocumentReader.Read(
            "<html><body><h1>Guide</h1><p>Intro.</p><h2>Install</h2><p>Steps.</p><h2>Upgrade</h2><p>More.</p></body></html>",
            "https://example.test/guide");

        return Build(document);
    }

    private static IReadOnlyList<KnowledgeObject> Build(IngestionDocument document)
    {
        var structure = StructureAnalyzers.Default().Analyze(document);

        return KnowledgeObjectBuilder.Build(
            document,
            new KnowledgeObjectBuildOptions
            {
                FileKey = "0123456789abcdef",
                DataSourceId = "data-source-1",
                Title = "guide.html",
                ContentHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                Structure = structure,
            },
            []);
    }
}
