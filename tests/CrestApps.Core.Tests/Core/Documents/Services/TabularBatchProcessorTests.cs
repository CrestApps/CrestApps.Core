using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class TabularBatchProcessorTests
{
    [Fact]
    public void SplitIntoBatches_NullContent_ReturnsEmptyList()
    {
        var processor = CreateProcessor();

        var batches = processor.SplitIntoBatches(null, "data.csv");

        Assert.Empty(batches);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void SplitIntoBatches_EmptyOrWhitespaceContent_ReturnsEmptyList(string content)
    {
        var processor = CreateProcessor();

        var batches = processor.SplitIntoBatches(content, "data.csv");

        Assert.Empty(batches);
    }

    [Fact]
    public void SplitIntoBatches_HeaderOnly_ReturnsEmptyList()
    {
        var processor = CreateProcessor();

        var batches = processor.SplitIntoBatches("id,name", "data.csv");

        Assert.Empty(batches);
    }

    [Fact]
    public void SplitIntoBatches_SingleDataRow_CreatesExactBatch()
    {
        var processor = CreateProcessor();

        var batches = processor.SplitIntoBatches("id,name\n1,Ada", "data.csv");

        var batch = Assert.Single(batches);
        AssertBatch(
            batch,
            expectedBatchIndex: 0,
            expectedHeader: "id,name",
            expectedRows: ["1,Ada"],
            expectedRowStartIndex: 1,
            expectedRowEndIndex: 1,
            expectedContent: "id,name\n1,Ada");
    }

    [Fact]
    public void SplitIntoBatches_MultipleDataRowsWithinBatch_PreservesExactOrder()
    {
        var processor = CreateProcessor(rowBatchSize: 10);

        var batches = processor.SplitIntoBatches("id,name\n1,Ada\n2,Grace\n3,Linus", "data.csv");

        var batch = Assert.Single(batches);
        AssertBatch(
            batch,
            expectedBatchIndex: 0,
            expectedHeader: "id,name",
            expectedRows: ["1,Ada", "2,Grace", "3,Linus"],
            expectedRowStartIndex: 1,
            expectedRowEndIndex: 3,
            expectedContent: "id,name\n1,Ada\n2,Grace\n3,Linus");
    }

    [Fact]
    public void SplitIntoBatches_LfContent_UsesLfWhenJoiningContent()
    {
        var processor = CreateProcessor(rowBatchSize: 2);

        var batches = processor.SplitIntoBatches("header\nrow-1\nrow-2\nrow-3", "data.csv");

        Assert.Collection(
            batches,
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 0,
                expectedHeader: "header",
                expectedRows: ["row-1", "row-2"],
                expectedRowStartIndex: 1,
                expectedRowEndIndex: 2,
                expectedContent: "header\nrow-1\nrow-2"),
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 1,
                expectedHeader: "header",
                expectedRows: ["row-3"],
                expectedRowStartIndex: 3,
                expectedRowEndIndex: 3,
                expectedContent: "header\nrow-3"));
    }

    [Fact]
    public void SplitIntoBatches_CrlfContent_PreservesCarriageReturns()
    {
        var processor = CreateProcessor(rowBatchSize: 2);

        var batches = processor.SplitIntoBatches("header\r\nrow-1\r\nrow-2", "data.csv");

        var batch = Assert.Single(batches);
        AssertBatch(
            batch,
            expectedBatchIndex: 0,
            expectedHeader: "header\r",
            expectedRows: ["row-1\r", "row-2"],
            expectedRowStartIndex: 1,
            expectedRowEndIndex: 2,
            expectedContent: "header\r\nrow-1\r\nrow-2");
    }

    [Fact]
    public void SplitIntoBatches_RowBatchSizeOne_RepeatsHeaderAndUsesExactIndexes()
    {
        var processor = CreateProcessor(rowBatchSize: 1);

        var batches = processor.SplitIntoBatches("header\nrow-1\nrow-2\nrow-3", "data.csv");

        Assert.Collection(
            batches,
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 0,
                expectedHeader: "header",
                expectedRows: ["row-1"],
                expectedRowStartIndex: 1,
                expectedRowEndIndex: 1,
                expectedContent: "header\nrow-1"),
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 1,
                expectedHeader: "header",
                expectedRows: ["row-2"],
                expectedRowStartIndex: 2,
                expectedRowEndIndex: 2,
                expectedContent: "header\nrow-2"),
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 2,
                expectedHeader: "header",
                expectedRows: ["row-3"],
                expectedRowStartIndex: 3,
                expectedRowEndIndex: 3,
                expectedContent: "header\nrow-3"));
    }

    [Fact]
    public void SplitIntoBatches_BatchSizeTwo_CreatesExactFinalPartialBatch()
    {
        var processor = CreateProcessor(rowBatchSize: 2);

        var batches = processor.SplitIntoBatches("header\nrow-1\nrow-2\nrow-3\nrow-4\nrow-5", "data.csv");

        Assert.Collection(
            batches,
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 0,
                expectedHeader: "header",
                expectedRows: ["row-1", "row-2"],
                expectedRowStartIndex: 1,
                expectedRowEndIndex: 2,
                expectedContent: "header\nrow-1\nrow-2"),
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 1,
                expectedHeader: "header",
                expectedRows: ["row-3", "row-4"],
                expectedRowStartIndex: 3,
                expectedRowEndIndex: 4,
                expectedContent: "header\nrow-3\nrow-4"),
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 2,
                expectedHeader: "header",
                expectedRows: ["row-5"],
                expectedRowStartIndex: 5,
                expectedRowEndIndex: 5,
                expectedContent: "header\nrow-5"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void SplitIntoBatches_NonPositiveBatchSize_UsesTwentyFiveRowFallback(int rowBatchSize)
    {
        var processor = CreateProcessor(rowBatchSize);
        var content = CreateContent(dataRowCount: 26);

        var batches = processor.SplitIntoBatches(content, "data.csv");

        Assert.Collection(
            batches,
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 0,
                expectedHeader: "header",
                expectedRows: Enumerable.Range(1, 25).Select(index => $"row-{index}").ToArray(),
                expectedRowStartIndex: 1,
                expectedRowEndIndex: 25,
                expectedContent: string.Join('\n', Enumerable.Range(0, 26).Select(index => index == 0 ? "header" : $"row-{index}"))),
            batch => AssertBatch(
                batch,
                expectedBatchIndex: 1,
                expectedHeader: "header",
                expectedRows: ["row-26"],
                expectedRowStartIndex: 26,
                expectedRowEndIndex: 26,
                expectedContent: "header\nrow-26"));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(-1, 4)]
    [InlineData(int.MinValue, 4)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    public void SplitIntoBatches_MaxRows_PreservesCurrentLimitSemantics(int maxRows, int expectedRowCount)
    {
        var processor = CreateProcessor(rowBatchSize: 2, maxRows: maxRows);

        var batches = processor.SplitIntoBatches("header\nrow-1\nrow-2\nrow-3\nrow-4", "data.csv");

        Assert.Equal(expectedRowCount, batches.Sum(batch => batch.RowCount));
        Assert.Equal(
            Enumerable.Range(1, expectedRowCount).Select(index => $"row-{index}"),
            batches.SelectMany(batch => batch.DataRows));
        Assert.Equal(1, batches[0].RowStartIndex);
        Assert.Equal(expectedRowCount, batches[^1].RowEndIndex);
    }

    [Fact]
    public void SplitIntoBatches_BlankRowsAndTrailingNewline_AreDataRows()
    {
        var processor = CreateProcessor(rowBatchSize: 10);

        var batches = processor.SplitIntoBatches("header\n\nrow-2\n", "data.csv");

        var batch = Assert.Single(batches);
        AssertBatch(
            batch,
            expectedBatchIndex: 0,
            expectedHeader: "header",
            expectedRows: ["", "row-2", ""],
            expectedRowStartIndex: 1,
            expectedRowEndIndex: 3,
            expectedContent: "header\n\nrow-2\n");
    }

    [Fact]
    public void SplitIntoBatches_HeaderFollowedByTrailingNewline_CreatesBlankDataRow()
    {
        var processor = CreateProcessor();

        var batches = processor.SplitIntoBatches("header\n", "data.csv");

        var batch = Assert.Single(batches);
        AssertBatch(
            batch,
            expectedBatchIndex: 0,
            expectedHeader: "header",
            expectedRows: [""],
            expectedRowStartIndex: 1,
            expectedRowEndIndex: 1,
            expectedContent: "header\n");
    }

    [Fact]
    public void SplitIntoBatches_EmptyHeader_PreservesHeaderButContentOmitsIt()
    {
        var processor = CreateProcessor();

        var batches = processor.SplitIntoBatches("\nrow-1", "data.csv");

        var batch = Assert.Single(batches);
        AssertBatch(
            batch,
            expectedBatchIndex: 0,
            expectedHeader: "",
            expectedRows: ["row-1"],
            expectedRowStartIndex: 1,
            expectedRowEndIndex: 1,
            expectedContent: "row-1");
    }

    [Fact]
    public void SplitIntoBatches_ReturnsMaterializedMutableListsWithIndependentRows()
    {
        var processor = CreateProcessor(rowBatchSize: 2);

        var batches = processor.SplitIntoBatches("header\nrow-1\nrow-2\nrow-3", "data.csv");

        var batchList = Assert.IsType<List<TabularBatch>>(batches);
        var firstRows = Assert.IsType<List<string>>(batchList[0].DataRows);
        var secondRows = Assert.IsType<List<string>>(batchList[1].DataRows);

        firstRows[0] = "changed";
        firstRows.Add("added");

        Assert.Equal(["changed", "row-2", "added"], firstRows);
        Assert.Equal(["row-3"], secondRows);
        Assert.Equal("header\nchanged\nrow-2\nadded", batchList[0].GetContent());
        Assert.Equal("header\nrow-3", batchList[1].GetContent());
    }

    private static TabularBatchProcessor CreateProcessor(int rowBatchSize = 25, int maxRows = 1000)
    {
        return new TabularBatchProcessor(
            Mock.Of<IAICompletionService>(),
            Mock.Of<IAIDeploymentManager>(),
            Mock.Of<ITemplateService>(),
            Options.Create(new RowLevelTabularBatchOptions
            {
                RowBatchSize = rowBatchSize,
                MaxRowsPerDocument = maxRows,
            }),
            NullLogger<TabularBatchProcessor>.Instance);
    }

    private static string CreateContent(int dataRowCount)
    {
        return string.Join(
            '\n',
            Enumerable.Range(0, dataRowCount + 1)
                .Select(index => index == 0 ? "header" : $"row-{index}"));
    }

    private static void AssertBatch(
        TabularBatch batch,
        int expectedBatchIndex,
        string expectedHeader,
        string[] expectedRows,
        int expectedRowStartIndex,
        int expectedRowEndIndex,
        string expectedContent)
    {
        Assert.Equal(expectedBatchIndex, batch.BatchIndex);
        Assert.Equal("data.csv", batch.FileName);
        Assert.Equal(expectedHeader, batch.HeaderRow);
        Assert.Equal(expectedRows, batch.DataRows);
        Assert.Equal(expectedRows.Length, batch.RowCount);
        Assert.Equal(expectedRowStartIndex, batch.RowStartIndex);
        Assert.Equal(expectedRowEndIndex, batch.RowEndIndex);
        Assert.Equal(expectedContent, batch.GetContent());
    }
}
