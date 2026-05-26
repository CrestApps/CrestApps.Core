using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DocumentOrchestrationHandlerTests
{
    private static DocumentOrchestrationHandler CreateHandler(
        AIToolDefinitionOptions toolOptions = null,
        ITemplateService templateService = null,
        IAIDocumentStore documentStore = null,
        IDocumentFileStore fileStore = null,
        IAIDeploymentManager deploymentManager = null,
        ChatDocumentsOptions documentOptions = null)
    {
        toolOptions ??= new AIToolDefinitionOptions();

        return new DocumentOrchestrationHandler(
            Options.Create(toolOptions),
            Options.Create(documentOptions ?? new ChatDocumentsOptions()),
            templateService ?? new FakeAITemplateService(),
            documentStore ?? Mock.Of<IAIDocumentStore>(),
            fileStore ?? Mock.Of<IDocumentFileStore>(),
            deploymentManager ?? Mock.Of<IAIDeploymentManager>(),
            NullLogger<DocumentOrchestrationHandler>.Instance);
    }

    private static AIToolDefinitionOptions CreateToolOptionsWithDocTools()
    {
        var options = new AIToolDefinitionOptions();
        options.SetTool("read_document", new AIToolDefinitionEntry(typeof(object)) { Name = "read_document", Description = "Reads document content", Purpose = AIToolPurposes.DocumentProcessing, });

        return options;
    }

    [Fact]
    public async Task BuildingAsync_ChatInteractionWithDocuments_PopulatesContext()
    {
        var handler = CreateHandler();
        var context = new OrchestrationContext();
        var interaction = new ChatInteraction
        {
            ItemId = "interaction1",
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc1",
                FileName = "report.pdf",
                ContentType = "application/pdf",
                FileSize = 1024,
            }, ],
        };
        await handler.BuildingAsync(new OrchestrationContextBuildingContext(interaction, context), TestContext.Current.CancellationToken);
        Assert.Single(context.Documents);
        Assert.Equal("doc1", context.Documents[0].DocumentId);
        Assert.Equal("report.pdf", context.Documents[0].FileName);
    }

    [Fact]
    public async Task BuildingAsync_ChatInteractionWithNoDocuments_LeavesEmpty()
    {
        var handler = CreateHandler();
        var context = new OrchestrationContext();
        var interaction = new ChatInteraction
        {
            Documents = []
        };
        await handler.BuildingAsync(new OrchestrationContextBuildingContext(interaction, context), TestContext.Current.CancellationToken);
        Assert.Empty(context.Documents);
    }

    [Fact]
    public async Task BuildingAsync_ChatInteractionWithNullDocuments_LeavesEmpty()
    {
        var handler = CreateHandler();
        var context = new OrchestrationContext();
        var interaction = new ChatInteraction
        {
            Documents = null
        };
        await handler.BuildingAsync(new OrchestrationContextBuildingContext(interaction, context), TestContext.Current.CancellationToken);
        Assert.Empty(context.Documents);
    }

    [Fact]
    public async Task BuildingAsync_NonChatInteractionResource_LeavesEmpty()
    {
        var handler = CreateHandler();
        var context = new OrchestrationContext();
        var profile = new AIProfile
        {
            DisplayText = "Test Profile"
        };
        await handler.BuildingAsync(new OrchestrationContextBuildingContext(profile, context), TestContext.Current.CancellationToken);
        Assert.Empty(context.Documents);
    }

    [Fact]
    public async Task BuildingAsync_MultipleDocuments_AllPopulated()
    {
        var handler = CreateHandler();
        var context = new OrchestrationContext();
        var interaction = new ChatInteraction
        {
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc1",
                FileName = "file1.pdf"
            }, new ChatDocumentInfo
            {
                DocumentId = "doc2",
                FileName = "file2.csv"
            }, new ChatDocumentInfo
            {
                DocumentId = "doc3",
                FileName = "file3.xlsx"
            }, ],
        };
        await handler.BuildingAsync(new OrchestrationContextBuildingContext(interaction, context), TestContext.Current.CancellationToken);
        Assert.Equal(3, context.Documents.Count);
    }

    [Fact]
    public async Task BuiltAsync_WithDocuments_EnrichesSystemMessage()
    {
        var handler = CreateHandler(CreateToolOptionsWithDocTools());
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc1",
                FileName = "report.pdf",
                ContentType = "application/pdf",
                FileSize = 2048,
            }, ],
        };
        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new ChatInteraction(), context), TestContext.Current.CancellationToken);
        var systemMessage = context.SystemMessageBuilder.ToString();
        Assert.Contains("report.pdf", systemMessage);
        Assert.Contains("read_document", systemMessage);
        // chat_interaction_id is NOT in the system message — it is resolved server-side.
        Assert.DoesNotContain("chat_interaction_id", systemMessage);
    }

    [Fact]
    public async Task BuiltAsync_WithStrictScope_PassesStrictScopeToTemplate()
    {
        var handler = CreateHandler(CreateToolOptionsWithDocTools());
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc1",
                FileName = "report.pdf",
            }, ],
        };
        var interaction = new ChatInteraction
        {
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc1",
                FileName = "report.pdf",
            }, ],
        };
        interaction.Put(new AIDataSourceRagMetadata { IsInScope = true });
        await handler.BuiltAsync(new OrchestrationContextBuiltContext(interaction, context), TestContext.Current.CancellationToken);
        var systemMessage = context.SystemMessageBuilder.ToString();
        Assert.Contains("Scope mode: strict", systemMessage);
    }

    [Fact]
    public async Task BuiltAsync_ProfileKnowledgeBaseWithStrictScope_IncludesDocumentToolGuidance()
    {
        var handler = CreateHandler(CreateToolOptionsWithDocTools());
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
        };
        var profile = new AIProfile();
        profile.Put(new AIDataSourceRagMetadata { IsInScope = true });
        profile.Put(new DocumentsMetadata { Documents = [new ChatDocumentInfo { DocumentId = "doc1", FileName = "report.pdf", ContentType = "application/pdf", FileSize = 2048, },], });
        await handler.BuiltAsync(new OrchestrationContextBuiltContext(profile, context), TestContext.Current.CancellationToken);
        var systemMessage = context.SystemMessageBuilder.ToString();
        Assert.Contains("Background knowledge is available for this profile.", systemMessage);
        Assert.Contains("Search the profile knowledge documents first", systemMessage);
        Assert.Contains("read_document", systemMessage);
        Assert.Contains("Scope mode: strict", systemMessage);
    }

    [Fact]
    public async Task BuiltAsync_WithoutDocuments_NoChanges()
    {
        var handler = CreateHandler();
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
        };
        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new AIProfile(), context), TestContext.Current.CancellationToken);
        Assert.Null(context.CompletionContext.SystemMessage);
    }

    [Fact]
    public async Task BuiltAsync_WithDocuments_DoesNotModifyToolNames()
    {
        var handler = CreateHandler(CreateToolOptionsWithDocTools());
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext
            {
                ToolNames = ["existing_tool"],
            },
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc1",
                FileName = "data.csv",
                ContentType = "text/csv",
                FileSize = 512,
            }, ],
        };
        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new ChatInteraction(), context), TestContext.Current.CancellationToken);
        // Document tools are system tools — the orchestrator always includes them.
        // The handler should NOT inject tool names.
        Assert.Single(context.CompletionContext.ToolNames);
        Assert.Contains("existing_tool", context.CompletionContext.ToolNames);
    }

    [Fact]
    public async Task BuiltAsync_WithDocuments_SetsHasDocumentsFlag()
    {
        var handler = CreateHandler(CreateToolOptionsWithDocTools());
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc1",
                FileName = "report.pdf",
                ContentType = "application/pdf",
                FileSize = 1024,
            }, ],
        };
        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new ChatInteraction(), context), TestContext.Current.CancellationToken);
        Assert.True(context.CompletionContext.AdditionalProperties.ContainsKey(AICompletionContextKeys.HasDocuments));
        Assert.Equal(true, context.CompletionContext.AdditionalProperties[AICompletionContextKeys.HasDocuments]);
    }

    [Fact]
    public async Task BuiltAsync_WithoutDocuments_DoesNotSetHasDocumentsFlag()
    {
        var handler = CreateHandler();
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
        };
        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new AIProfile(), context), TestContext.Current.CancellationToken);
        Assert.False(context.CompletionContext.AdditionalProperties.ContainsKey(AICompletionContextKeys.HasDocuments));
    }

    [Fact]
    public async Task BuiltAsync_WithVisionImageDocuments_AddsVisionUserContents()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var fileStore = new Mock<IDocumentFileStore>();
        var deploymentManager = new Mock<IAIDeploymentManager>();

        deploymentManager
            .Setup(manager => manager.ResolveOrDefaultAsync(
                AIDeploymentPurpose.Chat,
                "vision-chat",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AIDeployment>(new AIDeployment
            {
                Name = "vision-chat",
                Purpose = AIDeploymentPurpose.Chat | AIDeploymentPurpose.Vision,
            }));

        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([
                new AIDocument
                {
                    ItemId = "doc1",
                    FileName = "image.png",
                    FileSize = 3,
                    ContentType = "image/png",
                    StoredFilePath = "documents/interaction1/image.png",
                },
            ]);

        fileStore
            .Setup(store => store.GetFileAsync("documents/interaction1/image.png"))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));

        var handler = CreateHandler(
            CreateToolOptionsWithDocTools(),
            null,
            documentStore.Object,
            fileStore.Object,
            deploymentManager.Object);
        var interaction = new ChatInteraction
        {
            ItemId = "interaction1",
        };
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext
            {
                ChatDeploymentName = "vision-chat",
            },
            Documents = [
                new ChatDocumentInfo
                {
                    DocumentId = "doc1",
                    FileName = "image.png",
                    ContentType = "image/png",
                    FileSize = 3,
                },
            ],
        };

        await handler.BuiltAsync(new OrchestrationContextBuiltContext(interaction, context), TestContext.Current.CancellationToken);

        Assert.True(context.Properties.TryGetValue(OrchestrationPropertyKeys.VisionUserContents, out var value));

        var contents = Assert.IsType<List<AIContent>>(value);
        var imageContent = Assert.IsType<DataContent>(Assert.Single(contents));
        Assert.Equal("image/png", imageContent.MediaType);
        Assert.Equal([1, 2, 3], imageContent.Data.ToArray());
    }

    [Fact]
    public async Task BuiltAsync_WithOversizedVisionImage_SkipsVisionUserContents()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var fileStore = new Mock<IDocumentFileStore>();
        var deploymentManager = new Mock<IAIDeploymentManager>();
        var documentOptions = new ChatDocumentsOptions
        {
            MaxVisionInputBytesPerRequest = 2,
        };

        deploymentManager
            .Setup(manager => manager.ResolveOrDefaultAsync(
                AIDeploymentPurpose.Chat,
                "vision-chat",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AIDeployment>(new AIDeployment
            {
                Name = "vision-chat",
                Purpose = AIDeploymentPurpose.Chat | AIDeploymentPurpose.Vision,
            }));

        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([
                new AIDocument
                {
                    ItemId = "doc1",
                    FileName = "image.png",
                    FileSize = 3,
                    ContentType = "image/png",
                    StoredFilePath = "documents/interaction1/image.png",
                },
            ]);

        var handler = CreateHandler(
            CreateToolOptionsWithDocTools(),
            null,
            documentStore.Object,
            fileStore.Object,
            deploymentManager.Object,
            documentOptions);
        var interaction = new ChatInteraction
        {
            ItemId = "interaction1",
        };
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext
            {
                ChatDeploymentName = "vision-chat",
            },
            Documents = [
                new ChatDocumentInfo
                {
                    DocumentId = "doc1",
                    FileName = "image.png",
                    ContentType = "image/png",
                    FileSize = 3,
                },
            ],
        };

        await handler.BuiltAsync(new OrchestrationContextBuiltContext(interaction, context), TestContext.Current.CancellationToken);

        Assert.False(context.Properties.ContainsKey(OrchestrationPropertyKeys.VisionUserContents));
        fileStore.Verify(store => store.GetFileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BuiltAsync_WithMixedUserUploads_SplitsVisionAndSearchableDocumentsInTemplateArguments()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var fileStore = new Mock<IDocumentFileStore>();
        var deploymentManager = new Mock<IAIDeploymentManager>();
        var templateService = new CaptureTemplateService();

        deploymentManager
            .Setup(manager => manager.ResolveOrDefaultAsync(
                AIDeploymentPurpose.Chat,
                "vision-chat",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AIDeployment>(new AIDeployment
            {
                Name = "vision-chat",
                Purpose = AIDeploymentPurpose.Chat | AIDeploymentPurpose.Vision,
            }));

        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction-1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([
                new AIDocument
                {
                    ItemId = "image-doc",
                    FileName = "players_logo.jpg",
                    FileSize = 8974,
                    ContentType = "image/jpeg",
                    StoredFilePath = "documents/interaction-1/players_logo.jpg",
                },
            ]);

        fileStore
            .Setup(store => store.GetFileAsync("documents/interaction-1/players_logo.jpg"))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));

        var handler = CreateHandler(
            CreateToolOptionsWithDocTools(),
            templateService,
            documentStore.Object,
            fileStore.Object,
            deploymentManager.Object);
        var interaction = new ChatInteraction
        {
            ItemId = "interaction-1",
            Documents =
            [
                new ChatDocumentInfo
                {
                    DocumentId = "image-doc",
                    FileName = "players_logo.jpg",
                    ContentType = "image/jpeg",
                    FileSize = 8974,
                },
                new ChatDocumentInfo
                {
                    DocumentId = "pdf-doc",
                    FileName = "rules.pdf",
                    ContentType = "application/pdf",
                    FileSize = 4096,
                },
            ],
        };
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext
            {
                ChatDeploymentName = "vision-chat",
            },
            Documents = interaction.Documents,
        };

        await handler.BuiltAsync(new OrchestrationContextBuiltContext(interaction, context), TestContext.Current.CancellationToken);

        Assert.NotNull(templateService.Arguments);

        var searchableDocuments = Assert.IsAssignableFrom<IEnumerable<ChatDocumentInfo>>(templateService.Arguments["userSuppliedDocuments"]);
        var visionDocuments = Assert.IsAssignableFrom<IEnumerable<ChatDocumentInfo>>(templateService.Arguments["visionUserSuppliedDocuments"]);

        Assert.Collection(
            searchableDocuments,
            document => Assert.Equal("pdf-doc", document.DocumentId));
        Assert.Collection(
            visionDocuments,
            document => Assert.Equal("image-doc", document.DocumentId));
    }

    [Fact]
    public async Task BuiltAsync_WithPerFileLimitExceeded_SkipsVisionDocument()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        var fileStore = new Mock<IDocumentFileStore>();
        var deploymentManager = new Mock<IAIDeploymentManager>();
        var documentOptions = new ChatDocumentsOptions
        {
            MaxVisionImageBytesPerFile = 5,
            MaxVisionInputBytesPerRequest = 100,
        };

        deploymentManager
            .Setup(manager => manager.ResolveOrDefaultAsync(
                AIDeploymentPurpose.Chat,
                "vision-chat",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AIDeployment>(new AIDeployment
            {
                Name = "vision-chat",
                Purpose = AIDeploymentPurpose.Chat | AIDeploymentPurpose.Vision,
            }));

        documentStore
            .Setup(store => store.GetDocumentsAsync("interaction1", AIReferenceTypes.Document.ChatInteraction))
            .ReturnsAsync([
                new AIDocument
                {
                    ItemId = "doc1",
                    FileName = "large-image.png",
                    FileSize = 10,
                    ContentType = "image/png",
                    StoredFilePath = "documents/interaction1/large-image.png",
                },
            ]);

        var handler = CreateHandler(
            CreateToolOptionsWithDocTools(),
            null,
            documentStore.Object,
            fileStore.Object,
            deploymentManager.Object,
            documentOptions);
        var interaction = new ChatInteraction
        {
            ItemId = "interaction1",
        };
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext
            {
                ChatDeploymentName = "vision-chat",
            },
            Documents = [
                new ChatDocumentInfo
                {
                    DocumentId = "doc1",
                    FileName = "large-image.png",
                    ContentType = "image/png",
                    FileSize = 10,
                },
            ],
        };

        await handler.BuiltAsync(new OrchestrationContextBuiltContext(interaction, context), TestContext.Current.CancellationToken);

        Assert.False(context.Properties.ContainsKey(OrchestrationPropertyKeys.VisionUserContents));
        fileStore.Verify(store => store.GetFileAsync(It.IsAny<string>()), Times.Never);
    }

    private sealed class FakeAITemplateService : ITemplateService
    {
        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Template>>([]);
        }

        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Template>(null);
        }

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            var hasVisionUserSuppliedDocuments = arguments != null &&
                arguments.TryGetValue("visionUserSuppliedDocuments", out var visionDocumentsObj) &&
                visionDocumentsObj is IEnumerable<object> visionDocuments &&
                visionDocuments.Any();

            if (arguments != null && arguments.TryGetValue("tools", out var toolsObj) && toolsObj is IEnumerable<object> tools)
            {
                var isInScope = arguments.TryGetValue("isInScope", out var isInScopeObj) && isInScopeObj is true;
                var hasKnowledgeBaseDocuments = arguments.TryGetValue("knowledgeBaseDocuments", out var knowledgeBaseDocumentsObj) && knowledgeBaseDocumentsObj is IEnumerable<object> knowledgeBaseDocuments && knowledgeBaseDocuments.Any();
                var lines = new List<string>
                {
                    "[Available Documents or attachments]",
                    hasKnowledgeBaseDocuments ? "Background knowledge is available for this profile." : "The user has uploaded the following documents as supplementary context.",
                    $"Scope mode: {(isInScope ? "strict" : "relaxed")}",
                    "",
                };

                if (hasVisionUserSuppliedDocuments)
                {
                    lines.Add("The current user message already includes uploaded image attachments as multimodal inputs.");
                    lines.Add(string.Empty);
                }

                lines.Add(hasKnowledgeBaseDocuments ? "Search the profile knowledge documents first using the available document tools before answering." : "Search the uploaded documents first using the document tools before answering.");
                lines.Add(string.Empty);
                lines.Add("Available document tools:");

                foreach (dynamic tool in tools)
                {
                    lines.Add($"- {tool.Name}: {tool.Description}");
                }

                if (arguments.TryGetValue("availableDocuments", out var docsObj) && docsObj is IEnumerable<object> docs)
                {
                    lines.Add("");
                    lines.Add("Available documents:");
                    foreach (dynamic doc in docs)
                    {
                        lines.Add($"- {doc.FileName} ({doc.ContentType}, {doc.FileSize} bytes)");
                    }
                }

                return Task.FromResult(string.Join(Environment.NewLine, lines));
            }

            return Task.FromResult($"[Template: {id}]");
        }

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Join(separator, ids.Select(id => $"[Template: {id}]")));
        }
    }

    private sealed class CaptureTemplateService : ITemplateService
    {
        public Dictionary<string, object> Arguments { get; private set; }

        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Template>>([]);
        }

        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Template>(null);
        }

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            Arguments = arguments == null
                ? null
                : new Dictionary<string, object>(arguments);

            return Task.FromResult(string.Empty);
        }

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }
    }
}
