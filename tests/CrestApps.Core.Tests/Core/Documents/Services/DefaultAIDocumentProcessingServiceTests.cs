using System.Text;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class DefaultAIDocumentProcessingServiceTests
{
    [Fact]
    public async Task ProcessFileAsync_TabularFile_StoresRawContentChunksWithoutEmbeddings()
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".csv", embeddable: false, isTabular: true));
        var service = CreateService(options);
        const string content = "name,amount\nNorth,100\nSouth,200";

        var result = await service.ProcessFileAsync(
            CreateFormFile("sales.csv", "text/csv", content),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null);

        Assert.True(result.Success);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal(content, chunk.Content);
        Assert.Null(chunk.Embedding);
    }

    [Fact]
    public async Task ProcessFileAsync_NonEmbeddableDocument_StoresChunksWithoutEmbeddings()
    {
        var options = new ChatDocumentsOptions();
        options.Add(".log", embeddable: false);
        var service = CreateService(options);

        var result = await service.ProcessFileAsync(
            CreateFormFile("events.log", "text/plain", "line 1\nline 2"),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null);

        Assert.True(result.Success);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal("line 1\nline 2", chunk.Content);
        Assert.Null(chunk.Embedding);
    }

    private static DefaultAIDocumentProcessingService CreateService(ChatDocumentsOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<PlainTextIngestionDocumentReader>();
        services.AddKeyedSingleton<IngestionDocumentReader>(
            ".csv",
            (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());
        services.AddKeyedSingleton<IngestionDocumentReader>(
            ".log",
            (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());
        var serviceProvider = services.BuildServiceProvider();

        return new DefaultAIDocumentProcessingService(
            serviceProvider,
            new DefaultAITextNormalizer(),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<DefaultAIDocumentProcessingService>.Instance);
    }

    private static FormFile CreateFormFile(string fileName, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);

        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }
}
