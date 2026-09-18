using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Pdf.Services;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Tests.Support;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Regression;

/// <summary>
/// Pins the chat document upload path as it behaves today so later ingestion work cannot silently change it.
/// These tests are the regression contract described in the PDF ingestion plan: they are never edited to make
/// a later change pass. If one of them fails, the change under test is wrong.
/// </summary>
public sealed class ChatDocumentRegressionTests
{
    /// <summary>
    /// The line repeated at the top of every fixture page. It is the only text a decoration classifier is
    /// allowed to remove, so it is excluded from the superset comparison rather than asserted present.
    /// </summary>
    private const string RunningHead = "QUARTERLY REVIEW";

    /// <summary>
    /// Verifies that every word the legacy reader produced still reaches the stored chunks, apart from the
    /// repeated running head a decoration classifier may strip. The legacy baseline is read straight from
    /// the raw page text PdfPig exposes, which is exactly what the reader emitted before any layout analysis
    /// existed, so the baseline stays valid no matter how the reader is rewritten.
    /// </summary>
    [Fact]
    public async Task PlainTextPdf_WordSetIsSupersetOfLegacyOutput()
    {
        var pdf = CreateTextPdf();

        var legacyWords = GetLegacyWords(pdf);
        var decorationWords = Tokenize(RunningHead);

        var processed = await ProcessAsync("review.pdf", "application/pdf", pdf, ".pdf", tabular: false);

        var processedWords = Tokenize(processed);
        var missing = legacyWords.Except(decorationWords).Except(processedWords).ToArray();

        Assert.Empty(missing);
        Assert.Contains("charlie", processedWords);
        Assert.Contains("juliett", processedWords);
        Assert.Contains("romeo", processedWords);
    }

    /// <summary>
    /// Verifies that a PDF carrying an undescribed image still produces text only. Without a vision
    /// deployment there is nothing to say about the image, so no figure block may appear in the stored text.
    /// </summary>
    [Fact]
    public async Task PdfWithImages_ProducesTextOnlyBeforePhase3()
    {
        var pdf = CreateImagePdf();

        var processed = await ProcessAsync("chart.pdf", "application/pdf", pdf, ".pdf", tabular: false);

        Assert.Contains("charlie", processed, StringComparison.Ordinal);
        Assert.DoesNotContain("[figure", processed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that tabular uploads are stored exactly as they are read: a workbook flattens to one
    /// tab-separated line per row and a delimited file is stored byte for byte, both as a single raw chunk
    /// because a tabular upload bypasses normalization and chunking.
    /// </summary>
    [Fact]
    public async Task Xlsx_And_Csv_OutputIsByteIdentical()
    {
        var workbook = CreateWorkbook(
            ["region", "amount"],
            ["North", "100"],
            ["South", "200"]);

        var workbookText = await ProcessAsync(
            "sales.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            workbook,
            ".xlsx",
            tabular: true);

        Assert.Equal("region\tamount\nNorth\t100\nSouth\t200", workbookText);

        const string delimited = "region,amount\nNorth,100\nSouth,200";

        var delimitedText = await ProcessAsync("sales.csv", "text/csv", Encoding.UTF8.GetBytes(delimited), ".csv", tabular: true);

        Assert.Equal(delimited, delimitedText);
    }

    private static async Task<string> ProcessAsync(
        string fileName,
        string contentType,
        byte[] content,
        string extension,
        bool tabular)
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(extension, embeddable: !tabular, isTabular: tabular));

        var services = new ServiceCollection();
        services.AddSingleton<PlainTextIngestionDocumentReader>();
        services.AddSingleton<PdfIngestionDocumentReader>();
        services.AddSingleton<OpenXmlIngestionDocumentReader>();
        services.AddKeyedSingleton<IngestionDocumentReader>(".csv", (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());
        services.AddKeyedSingleton<IngestionDocumentReader>(".pdf", (sp, _) => sp.GetRequiredService<PdfIngestionDocumentReader>());
        services.AddKeyedSingleton<IngestionDocumentReader>(".xlsx", (sp, _) => sp.GetRequiredService<OpenXmlIngestionDocumentReader>());

        await using var serviceProvider = services.BuildServiceProvider();

        var service = new DefaultAIDocumentProcessingService(
            new DefaultAIDocumentIngestionPipeline(new DefaultIngestionDocumentReaderResolver(serviceProvider), []),
            new DefaultAITextNormalizer(),
            new RecordingDocumentFileStore(),
            Options.Create(options),
            Mock.Of<IOptionsMonitor<InteractionDocumentOptions>>(monitor =>
                    monitor.CurrentValue == new InteractionDocumentOptions { MaxIndexableCharacters = 0 }),
            TimeProvider.System,
            NullLogger<DefaultAIDocumentProcessingService>.Instance);

        var result = await service.ProcessFileAsync(
            CreateFormFile(fileName, contentType, content),
            "ref-1",
            AIReferenceTypes.Document.ChatInteraction,
            embeddingGenerator: null);

        Assert.True(result.Success, result.Error);

        return string.Join('\n', result.Chunks.OrderBy(chunk => chunk.Index).Select(chunk => chunk.Content));
    }

    /// <summary>
    /// Reads the words the pre-layout-analysis reader would have produced: one paragraph per page holding
    /// the raw page text, joined with a newline.
    /// </summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <returns>The legacy word set.</returns>
    private static HashSet<string> GetLegacyWords(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);

        var pages = new List<string>();

        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            var text = document.GetPage(pageNumber).Text?.Trim();

            if (!string.IsNullOrWhiteSpace(text))
            {
                pages.Add(text);
            }
        }

        return Tokenize(string.Join('\n', pages));
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return Regex
            .Matches(text, "[A-Za-z0-9]+", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds a three page fixture that repeats a running head and carries distinct body text per page.
    /// Every drawn run ends with a space because runs are concatenated without a separator in the raw page
    /// text; without it the last word of one run and the first word of the next would read as one token.
    /// </summary>
    /// <returns>The PDF bytes.</returns>
    private static byte[] CreateTextPdf()
    {
        return new PdfFixtureBuilder()
            .Page(page => page
                .Text(RunningHead + " ", 40, 800, 8)
                .Text("Alpha bravo charlie delta. ", 40, 700, 11)
                .Text("Echo foxtrot golf hotel. ", 40, 680, 11))
            .Page(page => page
                .Text(RunningHead + " ", 40, 800, 8)
                .Text("India juliett kilo lima. ", 40, 700, 11)
                .Text("Mike november oscar papa. ", 40, 680, 11))
            .Page(page => page
                .Text(RunningHead + " ", 40, 800, 8)
                .Text("Quebec romeo sierra tango. ", 40, 700, 11))
            .BuildBytes();
    }

    private static byte[] CreateImagePdf()
    {
        var chart = TestImageFactory.BarChart(200, 120);

        return new PdfFixtureBuilder()
            .Page(page => page
                .Text("Alpha bravo charlie delta. ", 40, 760, 11)
                .Png(chart, 40, 560, 200, 120))
            .BuildBytes();
    }

    private static byte[] CreateWorkbook(params string[][] rows)
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var rowIndex = 1u;

            foreach (var values in rows)
            {
                var row = new Row
                {
                    RowIndex = rowIndex,
                };

                for (var column = 0; column < values.Length; column++)
                {
                    row.AppendChild(new Cell
                    {
                        CellReference = $"{(char)('A' + column)}{rowIndex}",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(values[column])),
                    });
                }

                sheetData.AppendChild(row);
                rowIndex++;
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        return stream.ToArray();
    }

    private static FormFile CreateFormFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);

        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }
}
