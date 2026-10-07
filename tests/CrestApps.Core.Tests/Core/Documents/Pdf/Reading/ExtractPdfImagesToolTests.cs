using System.IO.Compression;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class ExtractPdfImagesToolTests
{
    [Fact]
    public async Task ExtractImages_ListsEachPictureWithWhatTheFileRecords()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfImagesTool());

        Assert.Contains("1 picture(s) in \"report.pdf\"", result, StringComparison.Ordinal);
        Assert.Contains("1. page 2, image 1: 64×48 px, 8-bit DeviceRGB, png", result, StringComparison.Ordinal);
        Assert.Contains("at x=", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractImages_ShowKeepAndDownload_ReturnMarkersAssetsAndArchive()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfImagesTool(), new { show = true, keep = true, download = true });

        Assert.Contains("image 1 [fig:1]", result, StringComparison.Ordinal);
        Assert.Contains("image 1 → asset:img1", result, StringComparison.Ordinal);
        Assert.Contains("[doc:1]", result, StringComparison.Ordinal);

        var state = await host.LoadWorkspaceAsync();
        var asset = Assert.Single(state.Assets);

        Assert.Equal("img1", asset.Id);
        Assert.Equal("image/png", asset.MediaType);

        var (document, bytes) = await host.ReadMarkerAsync("[doc:1]");

        Assert.Equal("report-images.zip", document.FileName);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);

        Assert.Equal("report-p2-img1.png", entry.Name);
    }

    [Fact]
    public async Task ExtractImages_MinSize_SkipsSmallPictures()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfImagesTool(), new { min_size = 100 });

        Assert.Contains("No pictures found", result, StringComparison.Ordinal);
        Assert.Contains("1 smaller than 100 px skipped", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractImages_HostWithoutPictures_SaysSoInsteadOfAMarker()
    {
        using var host = new PdfToolTestHost(canShowFigures: false);
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfImagesTool(), new { show = true });

        Assert.Contains("cannot show pictures", result, StringComparison.Ordinal);
        Assert.DoesNotContain("[fig:", result, StringComparison.Ordinal);
    }
}
