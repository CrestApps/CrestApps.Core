using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tools;

/// <summary>
/// Covers how a model gets at a figure read out of an uploaded document: what was printed with it, which page
/// it was on, and a plain answer when the identifier names nothing.
/// </summary>
public sealed class ViewDocumentFigureToolTests
{
    private const string InteractionId = "interaction-1";
    private const string DocumentId = "document-1";

    /// <summary>
    /// Verifies that a figure the document lists comes back with its caption and page. Without a host route
    /// there is no link, and the tool says what it knows rather than nothing.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_KnownFigure_ReturnsCaptionAndPage()
    {
        var result = await InvokeAsync(CreateDocument(), "report.pdf-p3-1");

        var text = Assert.IsType<string>(result);

        Assert.Contains("report.pdf-p3-1", text, StringComparison.Ordinal);
        Assert.Contains("page 3", text, StringComparison.Ordinal);
        Assert.Contains("Figure 1. The measurements.", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that an identifier the document does not list is answered with the identifiers it does, so
    /// the model can correct itself instead of guessing again.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_UnknownFigure_ListsTheFiguresTheDocumentHas()
    {
        var result = await InvokeAsync(CreateDocument(), "report.pdf-p9-9");

        var text = Assert.IsType<string>(result);

        Assert.Contains("has no figure 'report.pdf-p9-9'", text, StringComparison.Ordinal);
        Assert.Contains("report.pdf-p3-1", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a document belonging to another conversation is not found, whatever its identifier.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_DocumentFromAnotherConversation_IsNotFound()
    {
        var document = CreateDocument();

        document.ReferenceId = "someone-else";

        var result = await InvokeAsync(document, "report.pdf-p3-1");

        Assert.Contains("was not found in this session", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    private static AIDocument CreateDocument()
    {
        var document = new AIDocument
        {
            ItemId = DocumentId,
            ReferenceId = InteractionId,
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "report.pdf",
            ContentType = "application/pdf",
        };

        document.Put(new DocumentFigureList
        {
            Figures =
            [
                new DocumentFigure
                {
                    FigureId = "report.pdf-p3-1",
                    Page = 3,
                    Caption = "Figure 1. The measurements.",
                    StoragePath = "figures/report.pdf-p3-1.png",
                    MediaType = "image/png",
                },
            ],
        });

        return document;
    }

    private static async Task<object> InvokeAsync(AIDocument document, string figureId)
    {
        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.FindByIdAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var services = new ServiceCollection();

        services.AddSingleton(documentStore.Object);
        services.AddSingleton<IDocumentFileStore>(new RecordingDocumentFileStore());
        services.AddSingleton<ILogger<ViewDocumentFigureTool>>(NullLogger<ViewDocumentFigureTool>.Instance);

        await using var provider = services.BuildServiceProvider();

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = InteractionId,
        });

        var tool = new ViewDocumentFigureTool();

        return await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["document_id"] = DocumentId,
                ["figure_id"] = figureId,
            })
            {
                Services = provider,
            },
            TestContext.Current.CancellationToken);
    }
}
