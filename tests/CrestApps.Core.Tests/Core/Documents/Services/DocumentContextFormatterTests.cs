using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class DocumentContextFormatterTests
{
    [Fact]
    public async Task FormatDocumentTextFromChunksAsync_WhenStoreIsUnavailable_ReturnsNoContentMessage()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var document = new AIDocument
        {
            ItemId = "document",
            FileName = "document.txt",
        };

        var result = await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(services, document);

        Assert.Equal("Document 'document.txt' has no extractable text content.", result);
    }

    [Fact]
    public async Task FormatDocumentTextFromChunksAsync_OrdersChunksByIndex()
    {
        var document = new AIDocument
        {
            ItemId = "document",
            FileName = "document.txt",
        };
        var services = CreateServices(
            new AIDocumentChunk { Index = 2, Content = "third" },
            new AIDocumentChunk { Index = 0, Content = "first" },
            new AIDocumentChunk { Index = 1, Content = "second" });

        var result = await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(services, document);

        Assert.Equal($"[Document: document.txt]\n\nfirst{Environment.NewLine}second{Environment.NewLine}third", result);
    }

    [Fact]
    public async Task FormatDocumentTextFromChunksAsync_TruncatesJoinedContentAtMaximumLength()
    {
        var document = new AIDocument
        {
            ItemId = "document",
            FileName = "document.txt",
        };
        var services = CreateServices(
            new AIDocumentChunk { Index = 1, Content = "def" },
            new AIDocumentChunk { Index = 0, Content = "abc" });

        var maximumLength = 3 + Environment.NewLine.Length + 1;
        var result = await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(services, document, maximumLength);

        Assert.Equal($"[Document: document.txt]\n\nabc{Environment.NewLine}d\n\n... [content truncated]", result);
    }

    [Fact]
    public async Task FormatDocumentTextFromChunksAsync_WhenJoinedContentMatchesMaximumLength_DoesNotTruncate()
    {
        var document = new AIDocument
        {
            ItemId = "document",
            FileName = "document.txt",
        };
        var services = CreateServices(
            new AIDocumentChunk { Index = 1, Content = "def" },
            new AIDocumentChunk { Index = 0, Content = "abc" });

        var maximumLength = 6 + Environment.NewLine.Length;
        var result = await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(services, document, maximumLength);

        Assert.Equal($"[Document: document.txt]\n\nabc{Environment.NewLine}def", result);
    }

    [Fact]
    public async Task FormatDocumentTextFromChunksAsync_WhenChunksContainOnlyWhitespace_ReturnsNoContentMessage()
    {
        var document = new AIDocument
        {
            ItemId = "document",
            FileName = "document.txt",
        };
        var services = CreateServices(
            new AIDocumentChunk { Index = 0, Content = null },
            new AIDocumentChunk { Index = 1, Content = "   " });

        var result = await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(services, document, 1);

        Assert.Equal("Document 'document.txt' has no extractable text content.", result);
    }

    private static ServiceProvider CreateServices(params AIDocumentChunk[] chunks)
    {
        var store = new Mock<IAIDocumentChunkStore>();
        store
            .Setup(instance => instance.GetChunksByAIDocumentIdAsync(It.IsAny<string>()))
            .ReturnsAsync(chunks);

        return new ServiceCollection()
            .AddSingleton(store.Object)
            .BuildServiceProvider();
    }
}
