using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Intelligence;

public sealed class PdfJudgementToolTests
{
    private static readonly string[] _invoice =
    [
        "INVOICE",
        "Invoice number: INV-4471",
        "Bill to: Contoso Retail",
        "Widget 3 $10.00 $30.00",
        "Gadget 1 $20.00 $20.00",
        "Cable 2 $5.00 $10.00",
        "Subtotal: $60.00",
        "Amount due: $60.00",
        "Due date: 2026-10-15",
    ];

    [Fact]
    public async Task Classify_WithoutModel_ReturnsTheHeuristicAndSaysSo()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("doc.pdf", await PdfFixtures.TextAsync(_invoice));

        var result = await host.InvokeAsync(new ClassifyPdfTool());

        Assert.Contains("heuristic, because no AI model is configured on this host: invoice (confidence", result, StringComparison.Ordinal);
        Assert.Contains("tables likely", result, StringComparison.Ordinal);
        Assert.Contains("\"amount due\" ×1", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Classify_WithModel_UsesItsLabelWithinTheCategories()
    {
        var model = new FakeChatClient("""{"labels": [{"label": "Quote", "confidence": 88}], "rationale": "It offers prices valid for 30 days (p. 1)."}""");

        using var host = PdfFixtures.Host(text: model);
        await host.UploadAsync("doc.pdf", await PdfFixtures.TextAsync(["Quotation for Contoso. Prices valid for 30 days."]));

        var result = await host.InvokeAsync(new ClassifyPdfTool(), new { categories = new[] { "purchase order", "quote", "other" } });

        Assert.Contains("by the AI model: quote (confidence 0.88)", result, StringComparison.Ordinal);
        Assert.Contains("Rationale: It offers prices valid for 30 days (p. 1).", result, StringComparison.Ordinal);

        var request = Assert.Single(model.Requests);

        Assert.Contains("Categories: purchase order, quote, other", request.User, StringComparison.Ordinal);
        Assert.Contains("Give exactly one label", request.User, StringComparison.Ordinal);
        Assert.Contains("[Page 1]", request.User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Classify_ModelAnswersOutsideTheCategories_FallsBackToTheHeuristic()
    {
        using var host = PdfFixtures.Host(text: new FakeChatClient("""{"labels": ["spaceship blueprint"]}"""));
        await host.UploadAsync("doc.pdf", await PdfFixtures.TextAsync(_invoice));

        var result = await host.InvokeAsync(new ClassifyPdfTool());

        Assert.Contains("— heuristic: invoice", result, StringComparison.Ordinal);
        Assert.Contains("named no valid category", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractFacts_ReadsLabelledValuesInBothForms()
    {
        var facts = CrossReferencePdfsTool.ExtractFacts(
        [
            new PdfCorpusPage("a.pdf", 1, "Contract value: $50,000\nThe payment term is 30 days from delivery.\nPage 3\nDate: 2026-01-01"),
        ]);

        Assert.Contains(facts, fact => fact.Label == "contract value" && fact.Value == "50000" && fact.Display == "$50,000");
        Assert.Contains(facts, fact => fact.Label == "payment term" && fact.Value == "30");
        Assert.DoesNotContain(facts, fact => fact.Label is "page" or "date");
    }

    [Fact]
    public void FindConflicts_ReportsDifferentValuesAndAgreements()
    {
        var facts = CrossReferencePdfsTool.ExtractFacts(
        [
            new PdfCorpusPage("a.pdf", 1, "Contract value: $50,000\nStart date: March 1, 2026"),
            new PdfCorpusPage("b.pdf", 2, "The total contract value is $55,000.\nStart date: 2026-03-01"),
        ]);

        var conflicts = CrossReferencePdfsTool.FindConflicts(facts, out var agreements);

        var conflict = Assert.Single(conflicts);

        Assert.Equal("contract value", conflict.Label);
        Assert.Equal(["a.pdf", "b.pdf"], conflict.Documents.Keys.Order());
        Assert.Contains(agreements, agreement => agreement.Label == "start date");
    }

    [Fact]
    public async Task CrossReference_FindsConflictsSharedValuesAndTerms()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("order.pdf", await PdfFixtures.TextAsync(
            ["Purchase order for Northwind Traders.", "Contract value: $50,000", "Payment is due within 30 days.", "Questions go to legal@northwind.example."],
            ["Delivery date: 2026-04-01"]));
        await host.UploadAsync("contract.pdf", await PdfFixtures.TextAsync(
            ["Master agreement with Northwind Traders.", "The total contract value is $55,000.", "Payment is due within 45 days.", "Notices go to legal@northwind.example.", "Delivery date: April 1, 2026"]));

        var result = await host.InvokeAsync(new CrossReferencePdfsTool());

        Assert.Contains("Cross-reference of 2 PDFs", result, StringComparison.Ordinal);
        Assert.Contains("- \"contract value\": \"order.pdf\" $50,000 (p. 1) · \"contract.pdf\" $55,000 (p. 1)", result, StringComparison.Ordinal);
        Assert.Contains("- \"payment\": \"order.pdf\" 30 (p. 1) · \"contract.pdf\" 45 (p. 1)", result, StringComparison.Ordinal);
        Assert.Contains("\"delivery date\" = 2026-04-01", result, StringComparison.Ordinal);
        Assert.Contains("- legal@northwind.example (email): \"order.pdf\" p. 1; \"contract.pdf\" p. 1", result, StringComparison.Ordinal);
        Assert.Contains("- 2026-04-01 (date): \"order.pdf\" p. 2; \"contract.pdf\" p. 1", result, StringComparison.Ordinal);
        Assert.Contains("- Northwind Traders:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Model review", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossReference_WithModel_ReviewsTheConflictingPassagesFirst()
    {
        var model = new FakeChatClient("- Pair 1 — CONTRADICTS: $50,000 against $55,000 (\"order.pdf\" p. 1 vs \"contract.pdf\" p. 1)");

        using var host = PdfFixtures.Host(text: model);
        await host.UploadAsync("order.pdf", await PdfFixtures.TextAsync(["Contract value: $50,000"]));
        await host.UploadAsync("contract.pdf", await PdfFixtures.TextAsync(["The total contract value is $55,000."]));

        var result = await host.InvokeAsync(new CrossReferencePdfsTool(), new { pdfs = new[] { "order.pdf", "contract.pdf" }, topic = "contract value" });

        Assert.Contains("## Model review of the 1 most closely related passage pairs", result, StringComparison.Ordinal);
        Assert.Contains("CONTRADICTS: $50,000 against $55,000", result, StringComparison.Ordinal);

        var request = Assert.Single(model.Requests);

        Assert.Contains("Topic: contract value", request.User, StringComparison.Ordinal);
        Assert.Contains("A: Document \"order.pdf\", page 1: Contract value: $50,000", request.User, StringComparison.Ordinal);
        Assert.Contains("B: Document \"contract.pdf\", page 1", request.User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossReference_NeedsTwoPdfs()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("only.pdf", await PdfFixtures.TextAsync(["Just one document."]));

        var result = await host.InvokeAsync(new CrossReferencePdfsTool());

        Assert.Contains("needs at least two PDFs, and this conversation has 1", result, StringComparison.Ordinal);
    }
}
