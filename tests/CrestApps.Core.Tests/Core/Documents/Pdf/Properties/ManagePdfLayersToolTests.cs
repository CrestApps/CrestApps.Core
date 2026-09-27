using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class ManagePdfLayersToolTests
{
    [Fact]
    public async Task Layers_ListShowsDefaultsAndPages()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("plan.pdf", PdfPropertiesFixtures.LayersPdf());

        var listing = await host.InvokeAsync(new ManagePdfLayersTool());

        Assert.Contains("has 2 layer(s)", listing, StringComparison.Ordinal);
        Assert.Contains("\"Notes\": shown when the document opens, used on pages 1", listing, StringComparison.Ordinal);
        Assert.Contains("\"Draft\": hidden when the document opens, used on pages 1", listing, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    [Fact]
    public async Task Layers_HideAndShow_ChangeTheDefaultConfiguration()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("plan.pdf", PdfPropertiesFixtures.LayersPdf());

        var hidden = await host.InvokeAsync(new ManagePdfLayersTool(), new { action = "hide", layers = new[] { "notes" } });

        Assert.Contains("Hid \"Notes\"", hidden, StringComparison.Ordinal);
        Assert.Contains("does not remove layer content permanently", hidden, StringComparison.Ordinal);
        Assert.All(Visibility(await host.ReadWorkingPdfAsync("plan")), layer => Assert.False(layer.Visible));

        await host.InvokeAsync(new ManagePdfLayersTool(), new { action = "show", layers = new[] { "all" } });

        Assert.All(Visibility(await host.ReadWorkingPdfAsync("plan")), layer => Assert.True(layer.Visible));

        var unchanged = await host.InvokeAsync(new ManagePdfLayersTool(), new { action = "show", layers = new[] { "Draft" } });

        Assert.Contains("already shown", unchanged, StringComparison.Ordinal);
        Assert.Contains("There is no layer named", await host.InvokeAsync(new ManagePdfLayersTool(), new { action = "hide", layers = new[] { "Missing" } }), StringComparison.Ordinal);
    }

    private static List<PdfLayer> Visibility(byte[] bytes)
    {
        using var document = PdfFiles.OpenForImport(bytes);

        return PdfLayers.List(document);
    }
}
