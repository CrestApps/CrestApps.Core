using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfFormToolsTests
{
    [Fact]
    public async Task CreateListFillValidateFlatten_RoundTripsTheForm()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("application.pdf", await PdfToolFixtures.SimplePdfAsync("Application form"));

        var created = await host.InvokeAsync(new EditPdfFormTool(), new
        {
            changes = new object[]
            {
                new { action = "add", type = "text", name = "full_name", page = 1, x = 150, y = 150, required = true, tooltip = "Full name" },
                new { action = "add", type = "text", name = "email", page = 1, x = 150, y = 180 },
                new { action = "add", type = "checkbox", name = "agree", page = 1, x = 150, y = 210 },
                new { action = "add", type = "radio", name = "plan", page = 1, x = 150, y = 240, options = new[] { "Basic", "Pro" } },
                new { action = "add", type = "dropdown", name = "country", page = 1, x = 150, y = 300, options = new[] { "Canada", "Mexico", "United States" } },
                new { action = "add", type = "signature", name = "signature", page = 1, x = 150, y = 340 },
            },
        });

        Assert.Contains("with 6 form field(s)", created, StringComparison.Ordinal);

        var listed = await host.InvokeAsync(new GetPdfFormFieldsTool(), new { });

        Assert.Contains("\"full_name\" text", listed, StringComparison.Ordinal);
        Assert.Contains("(required)", listed, StringComparison.Ordinal);
        Assert.Contains("choices: Basic, Pro", listed, StringComparison.Ordinal);
        Assert.Contains("\"signature\" signature", listed, StringComparison.Ordinal);

        var invalid = await host.InvokeAsync(new ValidatePdfFormTool(), new
        {
            values = new { email = "not-an-email" },
            rules = new object[] { new { field = "email", format = "email" } },
        });

        Assert.Contains("\"full_name\" is required but empty", invalid, StringComparison.Ordinal);
        Assert.Contains("not a valid email", invalid, StringComparison.Ordinal);

        var filled = await host.InvokeAsync(new FillPdfFormTool(), new
        {
            values = new Dictionary<string, object>
            {
                ["full_name"] = "Avery Contoso",
                ["email"] = "avery@example.com",
                ["agree"] = true,
                ["plan"] = "Pro",
                ["country"] = "Canada",
                ["fullname"] = "typo",
                ["nickname"] = "x",
            },
        });

        Assert.Contains("Filled 5 field(s)", filled, StringComparison.Ordinal);
        Assert.Contains("already filled in this call", filled, StringComparison.Ordinal);
        Assert.Contains("no field named \"nickname\"", filled, StringComparison.Ordinal);

        using (var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("application")))
        {
            Assert.True(pdf.TryGetForm(out var form));

            var fields = form.Fields.ToDictionary(field => field.Information.PartialName, field => field);

            Assert.Equal("Avery Contoso", Assert.IsType<AcroTextField>(fields["full_name"]).Value);
            Assert.True(Assert.IsType<AcroCheckboxField>(fields["agree"]).IsChecked);
        }

        var valid = await host.InvokeAsync(new ValidatePdfFormTool(), new { });
        Assert.StartsWith("Valid:", valid, StringComparison.Ordinal);

        var flattened = await host.InvokeAsync(new FlattenPdfTool(), new { scope = "forms" });
        Assert.Contains("Flattened 7 form field widget(s)", flattened, StringComparison.Ordinal);

        using var flat = PdfDocument.Open(await host.ReadWorkingPdfAsync("application"));

        Assert.False(flat.TryGetForm(out _));

        var letters = string.Concat(flat.GetPage(1).Letters.Select(letter => letter.Value));

        Assert.Contains("AveryContoso", letters.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("Canada", letters, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FillPdfForm_BadChoice_ReportsTheChoices()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("survey.pdf", await PdfToolFixtures.SimplePdfAsync("Survey"));
        await host.InvokeAsync(new EditPdfFormTool(), new
        {
            changes = new object[] { new { action = "add", type = "dropdown", name = "size", options = new[] { "Small", "Large" } } },
        });

        var result = await host.InvokeAsync(new FillPdfFormTool(), new { pdf = "survey", values = new { size = "Huge" } });

        Assert.Contains("not one of the choices", result, StringComparison.Ordinal);
        Assert.Contains("Small, Large", result, StringComparison.Ordinal);
    }
}
