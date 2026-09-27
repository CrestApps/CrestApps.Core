using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Intelligence;

public sealed class PdfReadingAssistToolTests
{
    [Fact]
    public async Task Summarize_WithoutModel_TellsTheAgentToSummarizeItself()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("report.pdf", await PdfFixtures.TextAsync(["Revenue rose by twelve percent against plan in the third quarter."]));

        var result = await host.InvokeAsync(new SummarizePdfTool());

        Assert.Contains("No AI model is configured for summaries on this host; use extract_pdf_text", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summarize_SendsPageLabelledTextAndReturnsTheSummary()
    {
        var model = new FakeChatClient("- Revenue rose 12% against plan (p. 1).\n- Costs held flat (p. 2).");

        using var host = PdfFixtures.Host(text: model);
        await host.UploadAsync("report.pdf", await PdfFixtures.TextAsync(
            ["Revenue rose by 12% against plan in the third quarter."],
            ["Operating costs held flat compared with the second quarter."]));

        var result = await host.InvokeAsync(new SummarizePdfTool(), new { length = "short", focus = "costs" });

        Assert.Contains("Summary of uploaded \"report.pdf\", pages 1-2:", result, StringComparison.Ordinal);
        Assert.Contains("Costs held flat (p. 2)", result, StringComparison.Ordinal);

        var request = Assert.Single(model.Requests);

        Assert.Contains("[Page 1]", request.User, StringComparison.Ordinal);
        Assert.Contains("[Page 2]", request.User, StringComparison.Ordinal);
        Assert.Contains("Operating costs held flat", request.User, StringComparison.Ordinal);
        Assert.Contains("Focus: concentrate on costs", request.User, StringComparison.Ordinal);
        Assert.Contains("Use only the document text supplied", request.System, StringComparison.Ordinal);
        Assert.Equal(0.2f, request.Options.Temperature);
    }

    [Fact]
    public async Task Summarize_LongDocument_SummarizesInPartsAndCombines()
    {
        var model = new FakeChatClient(request => request.User.StartsWith("Write concise notes", StringComparison.Ordinal)
            ? "Notes on this part."
            : "Combined summary of the whole document (pp. 1-3).");

        using var host = PdfFixtures.Host(text: model, configure: options => options.MaxModelInputCharacters = 400);
        await host.UploadAsync("long.pdf", await PdfFixtures.TextAsync(
            [Sentences("Alpha", 3)],
            [Sentences("Beta", 3)],
            [Sentences("Gamma", 3)]));

        var result = await host.InvokeAsync(new SummarizePdfTool());

        Assert.Contains("Combined summary of the whole document", result, StringComparison.Ordinal);
        Assert.Contains("summarized in 3 parts", result, StringComparison.Ordinal);

        var requests = model.Requests;

        Assert.Equal(4, requests.Count);
        Assert.Equal(3, requests.Count(request => request.User.StartsWith("Write concise notes", StringComparison.Ordinal)));

        var combine = Assert.Single(requests, request => request.User.StartsWith("These are notes", StringComparison.Ordinal));

        Assert.Contains("[Notes on page 3]", combine.User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ask_ReturnsTheRelevantPassagesWithDocumentAndPage()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("contract.pdf", await PdfFixtures.TextAsync(
            ["The supplier delivers the goods within ten business days of each purchase order."],
            ["Either party may terminate this agreement with sixty days written notice to the other party."]));
        await host.UploadAsync("handbook.pdf", await PdfFixtures.TextAsync(["Employees receive twenty days of paid leave each year."]));

        var result = await host.InvokeAsync(new AskPdfTool(), new { question = "How much notice is needed to terminate the agreement?" });

        Assert.Contains("\"contract.pdf\" (2 pages), \"handbook.pdf\" (1 pages)", result, StringComparison.Ordinal);
        Assert.Contains("[1] \"contract.pdf\", page 2", result, StringComparison.Ordinal);
        Assert.Contains("sixty days written notice", result, StringComparison.Ordinal);
        Assert.Contains("(\"file.pdf\", p. 4)", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Answer (grounded", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ask_WithAnswer_WritesAGroundedAnswer()
    {
        var model = new FakeChatClient("Sixty days' written notice (p. 2).");

        using var host = PdfFixtures.Host(text: model);
        await host.UploadAsync("contract.pdf", await PdfFixtures.TextAsync(
            ["The supplier delivers the goods within ten business days of each purchase order."],
            ["Either party may terminate this agreement with sixty days written notice to the other party."]));

        var result = await host.InvokeAsync(new AskPdfTool(), new { question = "What notice terminates the agreement?", answer = true, pdfs = new[] { "contract.pdf" } });

        Assert.Contains("Answer (grounded in the passages below):\nSixty days' written notice (p. 2).", result, StringComparison.Ordinal);

        var request = Assert.Single(model.Requests);

        Assert.Contains("Question: What notice terminates the agreement?", request.User, StringComparison.Ordinal);
        Assert.Contains("[1] page 2:", request.User, StringComparison.Ordinal);
        Assert.Contains("only the numbered passages", request.System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ask_WithAnswerButNoModel_ReturnsThePassagesAndSaysSo()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("contract.pdf", await PdfFixtures.TextAsync(["Either party may terminate this agreement with sixty days written notice."]));

        var result = await host.InvokeAsync(new AskPdfTool(), new { question = "terminate notice", answer = true });

        Assert.Contains("No AI model is configured to write the answer", result, StringComparison.Ordinal);
        Assert.Contains("[1] \"contract.pdf\", page 1", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ask_WithoutQuestion_AsksForOne()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("contract.pdf", await PdfFixtures.TextAsync(["Some text."]));

        var result = await host.InvokeAsync(new AskPdfTool(), new { top_k = 3 });

        Assert.Contains("Pass 'question'", result, StringComparison.Ordinal);
    }

    private static string Sentences(string word, int count)
    {
        return string.Join(' ', Enumerable.Range(1, count).Select(number => $"{word} sentence number {number} describes part of the quarterly results in some detail."));
    }
}
