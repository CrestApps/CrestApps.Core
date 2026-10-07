using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.Templates.Extensions;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

/// <summary>
/// The guidance the primary model gets about the conversation's PDFs.
/// </summary>
public sealed class PdfAvailabilityTemplateTests
{
    [Fact]
    public async Task Template_NamesThePdfsTheAgentMade_AndForbidsWritingPdfsWithGenerateFile()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddTemplating();
        services.AddTemplatesFromAssembly(typeof(PdfServiceCollectionExtensions).Assembly);

        await using var provider = services.BuildServiceProvider();
        var templates = provider.GetRequiredService<ITemplateService>();

        // Without uploads, as when the agent composed the report itself: the model still has to be told
        // that the report exists and that changing it is the agent's work.
        var text = await templates.RenderAsync(
            PdfDocumentOrchestrationHandler.TemplateId,
            new Dictionary<string, object>
            {
                ["pdfDocuments"] = Array.Empty<object>(),
                ["workingDocuments"] = new[]
                {
                    new Dictionary<string, object> { ["Name"] = "Contoso Quarterly Report", ["PageCount"] = 4 },
                },
                ["pdfAgentName"] = PdfAgentProvider.AgentName,
                ["isRealtime"] = false,
            },
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("[Uploaded PDF files]", text, StringComparison.Ordinal);
        Assert.Contains("\"Contoso Quarterly Report\" (4 pages)", text, StringComparison.Ordinal);
        Assert.Contains("make the title blue", text, StringComparison.Ordinal);
        Assert.Contains("not with generate_file", text, StringComparison.Ordinal);
    }
}
