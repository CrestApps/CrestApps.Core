using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Intelligence;

public sealed class PdfExtractionToolTests
{
    // Short paragraphs, so no value is broken across the lines a page wraps its text into.
    private static readonly string[] _entityParagraphs =
    [
        "Contact jane.doe@example.com today.",
        "Card 4242 4242 4242 4242 was charged $1,250.00 on 2026-03-15.",
        "Then jane.doe@example.com was notified.",
        "Growth was 12.5% this year.",
        "Visit https://example.com/help for details.",
        "Acme Holdings Inc. is located at",
        "100 Main Street, Springfield, IL 62701",
    ];

    [Fact]
    public async Task Entities_WithoutModel_GroupsPatternValuesMasksIdentifiersAndSaysWhatIsMissing()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("notice.pdf", await PdfFixtures.TextAsync(_entityParagraphs, ["A second copy goes to jane.doe@example.com."]));

        var result = await host.InvokeAsync(new ExtractPdfEntitiesTool());

        Assert.Contains("## emails — 1 distinct, 3 mention(s)", result, StringComparison.Ordinal);
        Assert.Contains("- jane.doe@example.com — 3× (pp. 1-2)", result, StringComparison.Ordinal);
        Assert.Contains("•••• 4242 (credit_card)", result, StringComparison.Ordinal);
        Assert.DoesNotContain("4242 4242", result, StringComparison.Ordinal);
        Assert.Contains("- $1,250.00 — 1× (p. 1)", result, StringComparison.Ordinal);
        Assert.Contains("- 2026-03-15 — 1× (p. 1)", result, StringComparison.Ordinal);
        Assert.Contains("- 12.5% — 1× (p. 1)", result, StringComparison.Ordinal);
        Assert.Contains("https://example.com/help", result, StringComparison.Ordinal);
        Assert.Contains("## phones — none found", result, StringComparison.Ordinal);
        Assert.Contains("Acme Holdings Inc", result, StringComparison.Ordinal);
        Assert.Contains("100 Main Street, Springfield, IL 62701", result, StringComparison.Ordinal);
        Assert.Contains("people and locations cannot be found without a model", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entities_WithModel_KeepsOnlyNamesTheTextContains()
    {
        var model = new FakeChatClient("""
            ```json
            {"people": [{"name": "Jane Doe", "pages": [1]}, {"name": "Imaginary Person", "pages": [1]}],
             "Organizations": ["Acme Holdings Inc."],
             "locations": [{"name": "Springfield"}]}
            ```
            """);

        using var host = PdfFixtures.Host(text: model);
        await host.UploadAsync("letter.pdf", await PdfFixtures.TextAsync(
            ["Jane Doe of Acme Holdings Inc. wrote from Springfield."],
            ["Regards, Jane Doe."]));

        var result = await host.InvokeAsync(new ExtractPdfEntitiesTool(), new { types = new[] { "people", "organizations", "locations", "emails" } });

        Assert.Contains("found with patterns and the AI model", result, StringComparison.Ordinal);
        Assert.Contains("- Jane Doe — 2× (pp. 1-2)", result, StringComparison.Ordinal);
        Assert.Contains("- Acme Holdings Inc. — 1× (p. 1)", result, StringComparison.Ordinal);
        Assert.Contains("- Springfield — 1× (p. 1)", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Imaginary Person", result, StringComparison.Ordinal);
        Assert.Contains("1 name(s) the model gave were not found", result, StringComparison.Ordinal);
        Assert.Contains("## emails — none found", result, StringComparison.Ordinal);
        Assert.DoesNotContain("## dates", result, StringComparison.Ordinal);

        var request = Assert.Single(model.Requests);

        Assert.Contains("Entity types: people, organizations, locations", request.User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entities_UnknownType_ListsTheValidOnes()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("letter.pdf", await PdfFixtures.TextAsync(["Some text."]));

        var result = await host.InvokeAsync(new ExtractPdfEntitiesTool(), new { types = new[] { "colours" } });

        Assert.Contains("\"colours\" is not an entity type", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_WithoutModel_IsAClearError()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("invoice.pdf", await PdfFixtures.TextAsync(["Invoice INV-4471. Total due: $1,250.00"]));

        var result = await host.InvokeAsync(new ExtractPdfDataTool(), new { schema = new { fields = new[] { new { name = "total", type = "number" } } } });

        Assert.Contains("No AI model is configured for structured extraction", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_WithoutSchema_ExplainsTheShape()
    {
        using var host = PdfFixtures.Host(text: new FakeChatClient("{}"));
        await host.UploadAsync("invoice.pdf", await PdfFixtures.TextAsync(["Invoice INV-4471."]));

        var result = await host.InvokeAsync(new ExtractPdfDataTool(), new { instructions = "anything" });

        Assert.Contains("Pass 'schema'", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_ExtractsCoercesAndCitesValues()
    {
        var model = new FakeChatClient("""
            {"values": {"invoice_number": "INV-4471", "total": "$1,250.00", "paid": "no", "po_number": null, "vendor_rating": 5},
             "pages": {"invoice_number": [1], "total": [2]}}
            """);

        using var host = PdfFixtures.Host(text: model);
        await host.UploadAsync("invoice.pdf", await PdfFixtures.TextAsync(["Invoice INV-4471 from Acme."], ["Total due: $1,250.00. Unpaid."]));

        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["invoice_number"] = new { type = "string", description = "The invoice number" },
                ["total"] = new { type = new[] { "number", "null" } },
                ["paid"] = new { type = "boolean" },
                ["po_number"] = new { type = "string" },
            },
        };

        var result = await host.InvokeAsync(new ExtractPdfDataTool(), new { schema });
        var json = JsonNode.Parse(Between(result, "```json\n", "\n```"))!.AsObject();

        Assert.Equal("INV-4471", json["invoice_number"]!.GetValue<string>());
        Assert.Equal(1250d, json["total"]!.GetValue<double>());
        Assert.False(json["paid"]!.GetValue<bool>());
        Assert.Null(json["po_number"]);
        Assert.False(json.ContainsKey("vendor_rating"));
        Assert.Contains("invoice_number p. 1; total p. 2", result, StringComparison.Ordinal);
        Assert.Contains("Not stated in the document (null): po_number", result, StringComparison.Ordinal);
        Assert.Contains("dropped: vendor_rating", result, StringComparison.Ordinal);

        var request = Assert.Single(model.Requests);

        Assert.Contains("- invoice_number (string): The invoice number", request.User, StringComparison.Ordinal);
        Assert.Contains("- total (number)", request.User, StringComparison.Ordinal);
        Assert.Contains("[Page 2]", request.User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_LongDocument_MergesParts()
    {
        var model = new FakeChatClient(request => request.User.Contains("[Page 1]", StringComparison.Ordinal)
            ? """{"values": {"invoice_number": "INV-1", "total": null, "tags": ["urgent"]}}"""
            : """{"values": {"invoice_number": "INV-2", "total": 50, "tags": ["urgent", "export"]}}""");

        using var host = PdfFixtures.Host(text: model, configure: options => options.MaxModelInputCharacters = 400);
        await host.UploadAsync("invoice.pdf", await PdfFixtures.TextAsync([Filler("Invoice INV-1 header")], [Filler("Total 50")]));

        var result = await host.InvokeAsync(new ExtractPdfDataTool(), new
        {
            schema = new { fields = new object[] { "invoice_number", new { name = "total", type = "number" }, new { name = "tags", type = "array" } } },
        });

        var json = JsonNode.Parse(Between(result, "```json\n", "\n```"))!.AsObject();

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal("INV-1", json["invoice_number"]!.GetValue<string>());
        Assert.Equal(50d, json["total"]!.GetValue<double>());
        Assert.Equal(["urgent", "export"], json["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()));
        Assert.Contains("invoice_number p. 1; total p. 2; tags pp. 1-2", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Data_Multiple_ReturnsDistinctRecordsWithPages()
    {
        var model = new FakeChatClient("""
            {"records": [
              {"item": "Widget", "quantity": "3", "_pages": [1]},
              {"item": "Gadget", "quantity": 1, "_pages": [1]},
              {"item": "Widget", "quantity": 3, "_pages": [1]}
            ]}
            """);

        using var host = PdfFixtures.Host(text: model);
        await host.UploadAsync("invoice.pdf", await PdfFixtures.TextAsync(["Widget 3. Gadget 1."]));

        var result = await host.InvokeAsync(new ExtractPdfDataTool(), new
        {
            multiple = true,
            schema = new { fields = new[] { new { name = "item", type = "string" }, new { name = "quantity", type = "integer" } } },
        });

        var records = JsonNode.Parse(Between(result, "```json\n", "\n```"))!.AsArray();

        Assert.Contains("Extracted 2 record(s)", result, StringComparison.Ordinal);
        Assert.Equal(2, records.Count);
        Assert.Equal(3, records[0]!["quantity"]!.GetValue<long>());
        Assert.Contains("Pages by record: 1: p. 1; 2: p. 1", result, StringComparison.Ordinal);
        Assert.Contains("\"records\"", Assert.Single(model.Requests).User, StringComparison.Ordinal);
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        var to = text.IndexOf(end, from, StringComparison.Ordinal);

        return text[from..to];
    }

    private static string Filler(string lead)
    {
        return lead + ". " + string.Join(' ', Enumerable.Repeat("This sentence pads the page so it fills a part of its own.", 5));
    }
}
