using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class PdfReadingToolRegistrationsTests
{
    [Fact]
    public void Register_AddsEveryReadingToolAsAHiddenPdfTool()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        PdfReadingToolRegistrations.Register(services);

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value.Tools;

        string[] names =
        [
            PdfToolNames.ExtractPdfText,
            PdfToolNames.ExtractPdfTables,
            PdfToolNames.ExtractPdfImages,
            PdfToolNames.ExtractPdfStructure,
            PdfToolNames.ExtractPdfLinks,
            PdfToolNames.SearchPdf,
            PdfToolNames.GetPdfPageContent,
            PdfToolNames.AnalyzePdfLayout,
            PdfToolNames.DetectPdfLanguage,
            PdfToolNames.GeneratePdfOutline,
            PdfToolNames.ComparePdfs,
            PdfToolNames.ConvertFromPdf,
        ];

        foreach (var name in names)
        {
            Assert.True(tools.TryGetValue(name, out var entry), $"{name} is registered.");
            Assert.Equal(PdfToolRegistrations.Category, entry.Category);
            Assert.False(entry.IsSelectable(), $"{name} is hidden from the tool picker.");
        }

        Assert.Equal(names.Length, tools.Count);
    }
}
