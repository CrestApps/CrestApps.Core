using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Tools;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tools;

public sealed class ReadTabularDataToolTests
{
    [Fact]
    public async Task InvokeAsync_TabularDocument_ReturnsFormattedTabularData()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        documentStore.Setup(store => store.FindByIdAsync("doc-1", It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AIDocument>(new AIDocument
            {
                ItemId = "doc-1",
                ReferenceId = "chat-1",
                FileName = "survey.xlsx",
            }));

        var chunkStore = new Mock<IAIDocumentChunkStore>();
        chunkStore.Setup(store => store.GetChunksByAIDocumentIdAsync("doc-1"))
            .ReturnsAsync((IReadOnlyCollection<AIDocumentChunk>)
            [
                new AIDocumentChunk
                {
                    AIDocumentId = "doc-1",
                    Index = 0,
                    Content = "Name\tScore",
                },
                new AIDocumentChunk
                {
                    AIDocumentId = "doc-1",
                    Index = 1,
                    Content = "Alice\t42",
                },
            ]);

        var tool = new ReadTabularDataTool();

        using var scope = AIInvocationScope.Begin(new AIInvocationContext
        {
            ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
            {
                ItemId = "chat-1",
            }),
        });

        var result = await tool.InvokeAsync(
            CreateArguments(documentStore.Object, chunkStore.Object, "doc-1"),
            TestContext.Current.CancellationToken);

        var content = result.ToString();

        Assert.Contains("[Tabular data from: survey.xlsx]", content);
        Assert.Contains("Name\tScore", content);
        Assert.Contains("Alice\t42", content);
        documentStore.Verify(store => store.FindByIdAsync("doc-1", It.IsAny<CancellationToken>()), Times.Once);
        chunkStore.Verify(store => store.GetChunksByAIDocumentIdAsync("doc-1"), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NonTabularDocument_ReturnsReadDocumentGuidance()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        documentStore.Setup(store => store.FindByIdAsync("doc-1", It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AIDocument>(new AIDocument
            {
                ItemId = "doc-1",
                ReferenceId = "chat-1",
                FileName = "notes.txt",
            }));

        var chunkStore = new Mock<IAIDocumentChunkStore>(MockBehavior.Strict);
        var tool = new ReadTabularDataTool();

        using var scope = AIInvocationScope.Begin(new AIInvocationContext
        {
            ToolExecutionContext = new AIToolExecutionContext(new ChatInteraction
            {
                ItemId = "chat-1",
            }),
        });

        var result = await tool.InvokeAsync(
            CreateArguments(documentStore.Object, chunkStore.Object, "doc-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Document 'notes.txt' is not a recognized tabular format. Use 'read_document' instead.", result);
        documentStore.Verify(store => store.FindByIdAsync("doc-1", It.IsAny<CancellationToken>()), Times.Once);
        chunkStore.VerifyNoOtherCalls();
    }

    private static AIFunctionArguments CreateArguments(
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        string documentId)
    {
        var services = new ServiceCollection()
            .AddSingleton(documentStore)
            .AddSingleton(chunkStore)
            .AddSingleton<ILogger<ReadTabularDataTool>>(_ => NullLogger<ReadTabularDataTool>.Instance)
            .BuildServiceProvider();

        return new AIFunctionArguments(new Dictionary<string, object>
        {
            ["document_id"] = documentId,
        })
        {
            Services = services,
        };
    }
}
