using System.Text;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class DefaultAIDocumentProcessingServiceTests
{
    private const string ExcelMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PlainTextMediaType = "text/plain";

    [Fact]
    public async Task ProcessFileAsync_EmbeddableTextFile_GeneratesEmbeddingsForEachChunk()
    {
        var expectedChunks = new[] { "First line", "Second line" };
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IngestionDocumentReader>(".txt", new PlainTextIngestionDocumentReader());

        var serviceProvider = services.BuildServiceProvider();
        var options = new ChatDocumentsOptions();
        options.Add(".txt");

        var embeddingGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        embeddingGenerator.Setup(generator => generator.GenerateAsync(
                It.Is<IEnumerable<string>>(values => values.SequenceEqual(expectedChunks)),
                It.IsAny<EmbeddingGenerationOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmbeddings([1f, 2f], [3f, 4f]));

        var service = new DefaultAIDocumentProcessingService(
            serviceProvider,
            new TestTextNormalizer(),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<DefaultAIDocumentProcessingService>.Instance);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("First line\nSecond line"));
        var file = new FormFile(stream, 0, stream.Length, "files", "notes.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = PlainTextMediaType,
        };

        var result = await service.ProcessFileAsync(
            file,
            "chat-1",
            "ChatInteraction",
            embeddingGenerator.Object);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Equal("notes.txt", result.Document.FileName);
        Assert.Equal(2, result.Chunks.Count);
        Assert.Collection(
            result.Chunks.OrderBy(chunk => chunk.Index),
            chunk =>
            {
                Assert.Equal("First line", chunk.Content);
                Assert.Equal([1f, 2f], chunk.Embedding);
            },
            chunk =>
            {
                Assert.Equal("Second line", chunk.Content);
                Assert.Equal([3f, 4f], chunk.Embedding);
            });

        embeddingGenerator.Verify(generator => generator.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        embeddingGenerator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessFileAsync_NonEmbeddableSpreadsheet_StoresChunksWithoutGeneratingEmbeddings()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IngestionDocumentReader>(".xlsx", new OpenXmlIngestionDocumentReader());

        var serviceProvider = services.BuildServiceProvider();
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".xlsx", false));

        var embeddingGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        var service = new DefaultAIDocumentProcessingService(
            serviceProvider,
            new TestTextNormalizer(),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<DefaultAIDocumentProcessingService>.Instance);

        using var stream = CreateExcelDocument(
            ["Name", "Score"],
            ["Alice", "42"]);
        var file = new FormFile(stream, 0, stream.Length, "files", "survey.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = ExcelMediaType,
        };

        var result = await service.ProcessFileAsync(
            file,
            "chat-1",
            "ChatInteraction",
            embeddingGenerator.Object);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Equal("survey.xlsx", result.Document.FileName);
        Assert.Equal(2, result.Chunks.Count);
        Assert.Collection(
            result.Chunks.OrderBy(chunk => chunk.Index),
            chunk =>
            {
                Assert.Equal("Name\tScore", chunk.Content);
                Assert.Null(chunk.Embedding);
            },
            chunk =>
            {
                Assert.Equal("Alice\t42", chunk.Content);
                Assert.Null(chunk.Embedding);
            });

        embeddingGenerator.VerifyNoOtherCalls();
    }

    private static GeneratedEmbeddings<Embedding<float>> CreateEmbeddings(params float[][] vectors)
    {
        var embeddings = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var vector in vectors)
        {
            embeddings.Add(new Embedding<float>(vector));
        }

        return embeddings;
    }

    private static MemoryStream CreateExcelDocument(params string[][] rows)
    {
        var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            foreach (var values in rows)
            {
                var row = new Row();

                foreach (var value in values)
                {
                    row.AppendChild(new Cell
                    {
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(value)),
                    });
                }

                sheetData.AppendChild(row);
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
            workbookPart.Workbook.Save();
        }

        stream.Position = 0;

        return stream;
    }

    private sealed class TestTextNormalizer : IAITextNormalizer
    {
        public Task<string> NormalizeContentAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text);
        }

        public Task<List<string>> NormalizeAndChunkAsync(string text, CancellationToken cancellationToken = default)
        {
            var chunks = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            return Task.FromResult(chunks);
        }

        public string NormalizeTitle(string title)
        {
            return title;
        }
    }
}
