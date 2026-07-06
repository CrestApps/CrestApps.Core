using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularToolContextTests
{
    [Fact]
    public async Task ResolveAsync_ExcludesGeneratedDocumentsFromWorkspaceSources()
    {
        var uploaded = new AIDocument
        {
            ItemId = "uploaded-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "data.xlsx",
        };

        var generated = new AIDocument
        {
            ItemId = "generated-1",
            ReferenceId = "interaction-1",
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = "data-export.xlsx",
        };

        generated.Properties[DefaultGeneratedDocumentService.GeneratedPropertyName] = true;

        var documentStore = new Mock<IAIDocumentStore>();
        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([uploaded, generated]);

        var services = BuildServices(documentStore.Object);

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
        {
            ItemId = "interaction-1",
        });

        var context = await TabularToolContext.ResolveAsync(services, TestContext.Current.CancellationToken);

        Assert.NotNull(context);

        var document = Assert.Single(context.Documents);
        Assert.Equal("uploaded-1", document.DocumentId);
    }

    [Fact]
    public async Task LoadArtifactAsync_LoadsMissingArtifactFromStoredTabularFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "tabular-tool-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var storedDocument = new AIDocument
            {
                ItemId = "uploaded-1",
                ReferenceId = "interaction-1",
                ReferenceType = AIReferenceTypes.Document.ChatInteraction,
                FileName = "data.csv",
                StoredFilePath = "documents/chat-interaction/interaction-1/data.csv",
                ContentType = "text/csv",
            };

            var documentStore = new Mock<IAIDocumentStore>();
            documentStore
                .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
                .ReturnsAsync([storedDocument]);
            documentStore
                .Setup(store => store.FindByIdAsync("uploaded-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(storedDocument);

            var chunkStore = new Mock<IAIDocumentChunkStore>(MockBehavior.Strict);
            var artifactStore = new Mock<ITabularDocumentArtifactStore>();
            artifactStore
                .Setup(store => store.GetAsync("uploaded-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync((TabularDocumentArtifact)null);
            artifactStore
                .Setup(store => store.SaveAsync("uploaded-1", It.IsAny<TabularDocumentArtifact>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var services = BuildServices(documentStore.Object, chunkStore.Object, artifactStore.Object, tempRoot);
            var fileStore = services.GetRequiredService<IDocumentFileStore>();
            await using (var stream = new MemoryStream("name,amount\nNorth,100\nSouth,200"u8.ToArray()))
            {
                await fileStore.SaveFileAsync(storedDocument.StoredFilePath, stream);
            }

            using var scope = AIInvocationScope.Begin();
            scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
            {
                ItemId = "interaction-1",
            });

            var context = await TabularToolContext.ResolveAsync(services, TestContext.Current.CancellationToken);

            Assert.NotNull(context);

            var artifact = await context.LoadArtifactAsync(context.Documents[0], TestContext.Current.CancellationToken);

            Assert.Equal(["name", "amount"], artifact.Header);
            Assert.Collection(
                artifact.Rows,
                row => Assert.Equal(["North", "100"], row),
                row => Assert.Equal(["South", "200"], row));

            chunkStore.Verify(store => store.GetChunksByAIDocumentIdAsync(It.IsAny<string>()), Times.Never);
            artifactStore.Verify(
                store => store.SaveAsync("uploaded-1", It.IsAny<TabularDocumentArtifact>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadArtifactAsync_XlsxFile_UsesSpreadsheetRowsWithoutChunkFallback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "tabular-tool-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var storedDocument = new AIDocument
            {
                ItemId = "uploaded-1",
                ReferenceId = "interaction-1",
                ReferenceType = AIReferenceTypes.Document.ChatInteraction,
                FileName = "survey.xlsx",
                StoredFilePath = "documents/chat-interaction/interaction-1/survey.xlsx",
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            };

            var documentStore = new Mock<IAIDocumentStore>();
            documentStore
                .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
                .ReturnsAsync([storedDocument]);
            documentStore
                .Setup(store => store.FindByIdAsync("uploaded-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(storedDocument);

            var chunkStore = new Mock<IAIDocumentChunkStore>(MockBehavior.Strict);
            var artifactStore = new Mock<ITabularDocumentArtifactStore>();
            artifactStore
                .Setup(store => store.GetAsync("uploaded-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync((TabularDocumentArtifact)null);
            artifactStore
                .Setup(store => store.SaveAsync("uploaded-1", It.IsAny<TabularDocumentArtifact>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var services = BuildServices(
                documentStore.Object,
                chunkStore.Object,
                artifactStore.Object,
                tempRoot,
                xlsxArtifactBuilder: new SpreadsheetLikeTabularDocumentArtifactBuilder());
            var fileStore = services.GetRequiredService<IDocumentFileStore>();
            await using (var stream = new MemoryStream([1, 2, 3]))
            {
                await fileStore.SaveFileAsync(storedDocument.StoredFilePath, stream);
            }

            using var scope = AIInvocationScope.Begin();
            scope.Context.ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
            {
                ItemId = "interaction-1",
            });

            var context = await TabularToolContext.ResolveAsync(services, TestContext.Current.CancellationToken);

            Assert.NotNull(context);

            var artifact = await context.LoadArtifactAsync(context.Documents[0], TestContext.Current.CancellationToken);

            Assert.Equal(["Name", "Amount"], artifact.Header);
            Assert.Collection(
                artifact.Rows,
                row => Assert.Equal(["North", "100"], row),
                row => Assert.Equal(["South", "200"], row));

            chunkStore.Verify(store => store.GetChunksByAIDocumentIdAsync(It.IsAny<string>()), Times.Never);
            artifactStore.Verify(
                store => store.SaveAsync("uploaded-1", It.IsAny<TabularDocumentArtifact>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static ServiceProvider BuildServices(
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore = null,
        ITabularDocumentArtifactStore artifactStore = null,
        string basePath = null,
        IngestionDocumentReader xlsxReader = null,
        ITabularDocumentArtifactBuilder xlsxArtifactBuilder = null)
    {
        var options = new ChatDocumentsOptions();
        options.Add(".xlsx", embeddable: false, isTabular: true);
        options.Add(".csv", embeddable: false, isTabular: true);

        var fileStoreOptions = new DocumentFileSystemFileStoreOptions
        {
            BasePath = basePath ?? Path.Combine(Path.GetTempPath(), "tabular-tool-context-tests"),
        };

        var services = new ServiceCollection();

        services.AddSingleton(documentStore);
        services.AddSingleton(chunkStore ?? new Mock<IAIDocumentChunkStore>().Object);
        services.AddSingleton(artifactStore ?? new Mock<ITabularDocumentArtifactStore>().Object);
        services.AddSingleton<IOptions<ChatDocumentsOptions>>(Options.Create(options));
        services.AddSingleton<IOptions<DocumentFileSystemFileStoreOptions>>(Options.Create(fileStoreOptions));
        services.AddSingleton<IDocumentFileStore>(_ => new FileSystemFileStore(fileStoreOptions.BasePath));
        services.AddSingleton<PlainTextIngestionDocumentReader>();
        services.AddKeyedSingleton<IngestionDocumentReader>(
            ".csv",
            (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());
        if (xlsxReader != null)
        {
            services.AddSingleton(xlsxReader);
            services.AddKeyedSingleton<IngestionDocumentReader>(
                ".xlsx",
                (_, _) => xlsxReader);
        }
        if (xlsxArtifactBuilder != null)
        {
            services.AddSingleton(xlsxArtifactBuilder);
            services.AddKeyedSingleton<ITabularDocumentArtifactBuilder>(
                ".xlsx",
                (_, _) => xlsxArtifactBuilder);
        }
        services.AddScoped<TabularDocumentArtifactFactory>();
        services.AddSingleton<ILogger<TabularDocumentArtifactFactory>>(NullLogger<TabularDocumentArtifactFactory>.Instance);

        return services.BuildServiceProvider();
    }

    private sealed class SpreadsheetLikeIngestionDocumentReader : IngestionDocumentReader
    {
        public override Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            var document = new IngestionDocument(identifier);
            var section = new IngestionDocumentSection();
            section.Elements.Add(new IngestionDocumentParagraph("Name\tAmount") { Text = "Name\tAmount" });
            section.Elements.Add(new IngestionDocumentParagraph("North\t100") { Text = "North\t100" });
            section.Elements.Add(new IngestionDocumentParagraph("South\t200") { Text = "South\t200" });
            document.Sections.Add(section);

            return Task.FromResult(document);
        }
    }

    private sealed class SpreadsheetLikeTabularDocumentArtifactBuilder : ITabularDocumentArtifactBuilder
    {
        public Task<TabularDocumentArtifact> CreateAsync(
            Stream source,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TabularDocumentArtifact
            {
                Header = ["Name", "Amount"],
                Rows =
                [
                    ["North", "100"],
                    ["South", "200"],
                ],
            });
        }
    }
}
