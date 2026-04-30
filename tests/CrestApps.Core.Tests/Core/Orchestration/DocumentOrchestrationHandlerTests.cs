using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DocumentOrchestrationHandlerTests
{
    private static DocumentOrchestrationHandler CreateHandler(AIToolDefinitionOptions toolOptions = null)
    {
        toolOptions ??= new AIToolDefinitionOptions();

        return new DocumentOrchestrationHandler(Options.Create(toolOptions), new FakeAITemplateService(), NullLogger<DocumentOrchestrationHandler>.Instance);
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
                    hasKnowledgeBaseDocuments ? "Search the profile knowledge documents first using the available document tools before answering." : "Search the uploaded documents first using the document tools before answering.",
                    "",
                    "Available document tools:",
                };
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
}
