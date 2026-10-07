using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class OptimizePdfToolTests
{
    [Fact]
    public async Task Optimize_MergesDuplicatesRemovesThumbnailsAndShrinks()
    {
        using var host = new PdfToolTestHost();

        var original = PdfPropertiesFixtures.DuplicatedStreamsPdf();

        await host.UploadAsync("assembled.pdf", original);

        var answer = await host.InvokeAsync(new OptimizePdfTool());

        Assert.Contains("merged 2 duplicate object(s)", answer, StringComparison.Ordinal);
        Assert.Contains("removed 3 page thumbnail(s)", answer, StringComparison.Ordinal);
        Assert.Contains("compressed", answer, StringComparison.Ordinal);
        Assert.Contains("not downsampled or re-encoded", answer, StringComparison.Ordinal);

        var optimized = await host.ReadWorkingPdfAsync("assembled");

        Assert.True(optimized.Length < original.Length / 3, $"{optimized.Length} bytes is not much smaller than {original.Length}.");

        using var pdf = PdfDocument.Open(optimized);

        Assert.Equal(3, pdf.NumberOfPages);

        var forms = new HashSet<long>();

        foreach (var page in pdf.GetPages())
        {
            Assert.False(page.Dictionary.ContainsKey(NameToken.Create("Thumb")));

            var resources = PdfPigTokens.GetDictionary(pdf, page.Dictionary, "Resources");
            var objects = PdfPigTokens.GetDictionary(pdf, resources, "XObject");

            Assert.True(objects.TryGet(NameToken.Create("Fm9"), out var form));

            forms.Add(Assert.IsType<IndirectReferenceToken>(form).Data.ObjectNumber);
            Assert.Contains("repeated drawing", page.Text, StringComparison.Ordinal);
        }

        // Every page now uses the one copy of the form.
        Assert.Single(forms);
    }

    [Fact]
    public async Task Optimize_NeverKeepsALargerFile()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("assembled.pdf", PdfPropertiesFixtures.DuplicatedStreamsPdf());
        await host.InvokeAsync(new OptimizePdfTool(), new { level = "maximum" });

        var first = await host.ReadWorkingPdfAsync("assembled");
        var again = await host.InvokeAsync(new OptimizePdfTool(), new { level = "maximum" });
        var second = await host.ReadWorkingPdfAsync("assembled");

        Assert.True(second.Length <= first.Length);

        if (again.Contains("nothing was saved", StringComparison.Ordinal))
        {
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public async Task Optimize_Maximum_RemovesMetadata()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("assembled.pdf", PdfPropertiesFixtures.DuplicatedStreamsPdf());

        var answer = await host.InvokeAsync(new OptimizePdfTool(), new { level = "maximum" });

        Assert.Contains("removed the document information", answer, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("assembled"));

        Assert.True(string.IsNullOrEmpty(pdf.Information.Title));
        Assert.True(string.IsNullOrEmpty(pdf.Information.Author));
    }
}
