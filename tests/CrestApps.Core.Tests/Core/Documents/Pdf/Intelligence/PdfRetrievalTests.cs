using CrestApps.Core.AI.Documents.Pdf.Intelligence;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Intelligence;

public sealed class PdfRetrievalTests
{
    [Fact]
    public void Bm25_RanksThePassageThatSharesTheRarestTermsFirst()
    {
        var index = new PdfBm25Index(
        [
            "The supplier delivers the goods to the warehouse every week.",
            "Payment is due within thirty days of the invoice date.",
            "The warehouse is open on weekdays and the goods are counted weekly.",
            "Late payment incurs interest of two percent per month on the invoice amount.",
        ]);

        var hits = index.Search("When is the invoice payment due?", 4);

        Assert.Equal(1, hits[0].Index);
        Assert.Contains(hits, hit => hit.Index == 3);
        Assert.DoesNotContain(hits, hit => hit.Index is 0 or 2);
        Assert.True(hits[0].Score > hits[1].Score);
    }

    [Fact]
    public void Bm25_IgnoresStopWordsAndFindsNothingForThem()
    {
        var index = new PdfBm25Index(["The cat sat on the mat.", "A dog barked at the door."]);

        Assert.Empty(index.Search("the and of", 5));
    }

    [Fact]
    public void Tokenize_LowercasesFoldsPluralsAndKeepsNumbersWhole()
    {
        var tokens = PdfBm25Index.Tokenize("The Invoices total $1,200.50 for 3 companies.");

        Assert.Equal(["invoice", "total", "1200.50", "3", "company"], tokens);
    }

    [Fact]
    public void Chunk_KeepsWholePagesWithinTheLimitAndSplitsALongPageOnParagraphs()
    {
        var longPage = string.Join("\n\n", Enumerable.Range(1, 30).Select(number => $"Paragraph {number} " + new string('x', 80)));
        var pages = new List<PdfCorpusPage>
        {
            new("a.pdf", 1, "Short first page."),
            new("a.pdf", 2, "Short second page."),
            new("a.pdf", 3, longPage),
            new("a.pdf", 4, string.Empty),
            new("a.pdf", 5, "Closing page."),
        };

        var chunks = PdfCorpus.Chunk(pages, 1000);

        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= 1000, $"A chunk is {chunk.Text.Length} characters."));
        Assert.Equal([1, 2], chunks[0].Pages);
        Assert.StartsWith("[Page 1]\nShort first page.", chunks[0].Text, StringComparison.Ordinal);
        Assert.Contains(chunks, chunk => chunk.Text.Contains("[Page 3, continued]", StringComparison.Ordinal));
        Assert.DoesNotContain(chunks, chunk => chunk.Pages.Contains(4));
        Assert.Contains(5, chunks[^1].Pages);

        // Nothing is lost: every paragraph of the long page is in exactly one chunk.
        for (var number = 1; number <= 30; number++)
        {
            Assert.Single(chunks, chunk => chunk.Text.Contains($"Paragraph {number} x", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Chunk_WithDocumentLabels_NamesTheDocument()
    {
        var chunks = PdfCorpus.Chunk([new PdfCorpusPage("contract.pdf", 2, "Terms.")], 500, withDocument: true);

        Assert.Equal("[Document \"contract.pdf\", page 2]\nTerms.", chunks[0].Text);
    }

    [Fact]
    public void SplitPassages_OverlapsAndNeverSpansTwoPages()
    {
        var words = string.Join(' ', Enumerable.Range(1, 400).Select(number => $"w{number}."));
        var passages = PdfCorpus.SplitPassages(
        [
            new PdfCorpusPage("a.pdf", 1, words),
            new PdfCorpusPage("a.pdf", 2, "Second page text."),
        ], size: 300, overlap: 60);

        var first = passages.Where(passage => passage.Page == 1).ToList();

        Assert.True(first.Count > 3);
        Assert.All(first, passage => Assert.True(passage.Text.Length <= 300));
        Assert.Single(passages, passage => passage.Page == 2 && passage.Text == "Second page text.");

        // Consecutive passages share words, and every word is in some passage.
        Assert.Contains(first[0].Text.Split(' ')[^1], first[1].Text, StringComparison.Ordinal);
        Assert.Contains(first, passage => passage.Text.Contains("w400.", StringComparison.Ordinal));
        Assert.DoesNotContain(first, passage => passage.Text.StartsWith('.'));
    }

    [Fact]
    public void Cite_WritesSingleAndRangedPages()
    {
        Assert.Equal("p. 4", PdfCorpus.Cite([4]));
        Assert.Equal("pp. 1-3, 7", PdfCorpus.Cite([3, 1, 2, 7, 2]));
        Assert.Equal(string.Empty, PdfCorpus.Cite([]));
    }
}
