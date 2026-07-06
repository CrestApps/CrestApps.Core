using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tools;

public sealed class GetDocumentMetadataToolTests
{
    [Fact]
    public async Task InvokeAsync_HeadersScope_ReturnsTabularHeadersWithoutWorkspaceImport()
    {
        var storedDocument = new AIDocument
        {
            ItemId = "uploaded-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "SkyLineFull 1.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileSize = 128,
        };

        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([storedDocument]);

        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        artifactStore
            .Setup(store => store.GetAsync("uploaded-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TabularDocumentArtifact
            {
                Header = ["First Name", "Signup Date", "Is Active"],
                Rows =
                [
                    ["Ada", "2026-07-01", "true"],
                    ["Grace", "2026-07-02", "false"],
                ],
            });

        var services = BuildServices(documentStore.Object, artifactStore.Object);

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = "interaction-1",
        });

        var tool = new GetDocumentMetadataTool();
        var arguments = CreateArguments(services, new Dictionary<string, object>
        {
            ["scope"] = "headers",
        });

        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        var text = result.ToString();

        Assert.Contains("\"SkyLineFull 1.xlsx\" has 3 headers.", text);
        Assert.Contains("- First Name (inferred type: text)", text);
        Assert.Contains("- Signup Date (inferred type: date)", text);
        Assert.Contains("- Is Active (inferred type: boolean)", text);
    }

    [Fact]
    public async Task InvokeAsync_BasicScope_ReturnsNonTabularMetadata()
    {
        var storedDocument = new AIDocument
        {
            ItemId = "uploaded-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "notes.txt",
            ContentType = "text/plain",
            FileSize = 42,
        };

        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([storedDocument]);

        var services = BuildServices(documentStore.Object, Mock.Of<ITabularDocumentArtifactStore>());

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = "interaction-1",
        });

        var tool = new GetDocumentMetadataTool();
        var arguments = CreateArguments(services, new Dictionary<string, object>
        {
            ["scope"] = "basic",
        });

        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        var text = result.ToString();

        Assert.Contains("\"notes.txt\" metadata:", text);
        Assert.Contains("document_id: uploaded-1", text);
        Assert.Contains("content_type: text/plain", text);
        Assert.Contains("file_size_bytes: 42", text);
    }

    [Fact]
    public async Task InvokeAsync_ColumnsScope_ReturnsNormalizedColumnsWithInferredTypes()
    {
        var storedDocument = new AIDocument
        {
            ItemId = "uploaded-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "SkyLineFull 1.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileSize = 128,
        };

        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([storedDocument]);

        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        artifactStore
            .Setup(store => store.GetAsync("uploaded-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TabularDocumentArtifact
            {
                Header = ["Order ID", "Total Amount", "Updated At"],
                Rows =
                [
                    ["1001", "10.25", "2026-07-06T14:48:16Z"],
                    ["1002", "11.50", "2026-07-06T15:00:00Z"],
                ],
            });

        var services = BuildServices(documentStore.Object, artifactStore.Object);

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = "interaction-1",
        });

        var tool = new GetDocumentMetadataTool();
        var arguments = CreateArguments(services, new Dictionary<string, object>
        {
            ["scope"] = "columns",
        });

        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        var text = result.ToString();

        Assert.Contains("- Order_ID (source header: Order ID) — inferred type: integer", text);
        Assert.Contains("- Total_Amount (source header: Total Amount) — inferred type: decimal", text);
        Assert.Contains("- Updated_At (source header: Updated At) — inferred type: datetime", text);
    }

    private static AIFunctionArguments CreateArguments(IServiceProvider services, Dictionary<string, object> values)
    {
        return new AIFunctionArguments(values)
        {
            Services = services,
        };
    }

    private static ServiceProvider BuildServices(
        IAIDocumentStore documentStore,
        ITabularDocumentArtifactStore artifactStore)
    {
        var options = new ChatDocumentsOptions();
        options.Add(".xlsx", embeddable: false, isTabular: true);
        options.Add(".txt");

        var fileStoreOptions = new DocumentFileSystemFileStoreOptions
        {
            BasePath = Path.Combine(Path.GetTempPath(), "get-document-metadata-tool-tests", Guid.NewGuid().ToString("N")),
        };

        var services = new ServiceCollection();
        services.AddSingleton(documentStore);
        services.AddSingleton(Mock.Of<IAIDocumentChunkStore>());
        services.AddSingleton(artifactStore);
        services.AddSingleton<IOptions<ChatDocumentsOptions>>(Options.Create(options));
        services.AddSingleton<IOptions<DocumentFileSystemFileStoreOptions>>(Options.Create(fileStoreOptions));
        services.AddSingleton<IDocumentFileStore>(_ => new FileSystemFileStore(fileStoreOptions.BasePath));
        services.AddScoped<TabularDocumentArtifactFactory>();
        services.AddSingleton<ILogger<TabularDocumentArtifactFactory>>(NullLogger<TabularDocumentArtifactFactory>.Instance);
        services.AddSingleton<ILogger<GetDocumentMetadataTool>>(NullLogger<GetDocumentMetadataTool>.Instance);

        return services.BuildServiceProvider();
    }
}
