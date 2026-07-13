using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class DelimitedDataParserTests
{
    [Fact]
    public void Parse_Csv_ReturnsHeaderAndRows()
    {
        var content = "region,amount\nNorth,100\nSouth,200";

        var (header, rows) = DelimitedDataParser.Parse(content, "sales.csv");

        Assert.Equal(["region", "amount"], header);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["North", "100"], rows[0]);
        Assert.Equal(["South", "200"], rows[1]);
    }

    [Fact]
    public void Parse_Tsv_UsesTabDelimiter()
    {
        var content = "a\tb\tc\n1\t2\t3";

        var (header, rows) = DelimitedDataParser.Parse(content, "data.tsv");

        Assert.Equal(["a", "b", "c"], header);
        Assert.Single(rows);
        Assert.Equal(["1", "2", "3"], rows[0]);
    }

    [Theory]
    [InlineData("people.csv", ',', "Doe, John")]
    [InlineData("people.tsv", '\t', "Doe\tJohn")]
    public void Parse_QuotedDelimiter_PreservesDelimiterInField(
        string fileName,
        char delimiter,
        string expectedName)
    {
        var content = $"name{delimiter}note\n\"{expectedName}\"{delimiter}ok";

        var (header, rows) = DelimitedDataParser.Parse(content, fileName);

        Assert.Equal(["name", "note"], header);
        var row = Assert.Single(rows);
        Assert.Equal([expectedName, "ok"], row);
    }

    [Theory]
    [InlineData("people.csv", ',')]
    [InlineData("people.tsv", '\t')]
    public void Parse_EscapedQuotes_UnescapesDoubleQuotes(string fileName, char delimiter)
    {
        var content = $"name{delimiter}note\n\"quote \"\"x\"\"\"{delimiter}\"He said \"\"hello\"\"\"";

        var (header, rows) = DelimitedDataParser.Parse(content, fileName);

        Assert.Equal(["name", "note"], header);
        var row = Assert.Single(rows);
        Assert.Equal(["quote \"x\"", "He said \"hello\""], row);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_QuotedLineEnding_PreservesExactCharacters(string lineEnding)
    {
        var content = $"id,note\n1,\"before{lineEnding}after\"";

        var (header, rows) = DelimitedDataParser.Parse(content, "notes.csv");

        Assert.Equal(["id", "note"], header);
        var row = Assert.Single(rows);
        Assert.Equal("1", row[0]);
        Assert.Equal($"before{lineEnding}after", row[1]);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_RecordLineEnding_SeparatesRecords(string lineEnding)
    {
        var content = $"id,note{lineEnding}1,first{lineEnding}2,second";

        var (header, rows) = DelimitedDataParser.Parse(content, "notes.csv");

        Assert.Equal(["id", "note"], header);
        Assert.Collection(
            rows,
            row => Assert.Equal(["1", "first"], row),
            row => Assert.Equal(["2", "second"], row));
    }

    [Fact]
    public void Parse_CarriageReturnWithoutLineFeed_IsIgnoredOutsideQuotes()
    {
        var (header, rows) = DelimitedDataParser.Parse("a,b\r1,2", "data.csv");

        Assert.Equal(["a", "b1", "2"], header);
        Assert.Empty(rows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t\r\n ")]
    public void Parse_EmptyOrWhitespaceContent_ReturnsEmpty(string content)
    {
        var (header, rows) = DelimitedDataParser.Parse(content, "empty.csv");

        Assert.Empty(header);
        Assert.Empty(rows);
    }

    [Theory]
    [InlineData("name,amount", "header.csv", "name", "amount")]
    [InlineData("name\tamount", "header.tsv", "name", "amount")]
    public void Parse_HeaderOnly_ReturnsHeaderWithoutRows(
        string content,
        string fileName,
        string firstHeader,
        string secondHeader)
    {
        var (header, rows) = DelimitedDataParser.Parse(content, fileName);

        Assert.Equal([firstHeader, secondHeader], header);
        Assert.Empty(rows);
    }

    [Theory]
    [InlineData("a,b,c\n1,2,\n,,\n\n", "data.csv")]
    [InlineData("a\tb\tc\n1\t2\t\n\t\t\n\n", "data.tsv")]
    public void Parse_TrailingEmptyFieldsAndExplicitEmptyRow_PreservesValues(
        string content,
        string fileName)
    {
        var (header, rows) = DelimitedDataParser.Parse(content, fileName);

        Assert.Equal(["a", "b", "c"], header);
        Assert.Collection(
            rows,
            row => Assert.Equal(["1", "2", string.Empty], row),
            row => Assert.Equal([string.Empty, string.Empty, string.Empty], row));
    }

    [Fact]
    public void Parse_UnevenRows_PreservesEachOriginalFieldCountAndOrder()
    {
        var content = "a,b,c\n1,2\n3,4,5,6\n7";

        var (header, rows) = DelimitedDataParser.Parse(content, "uneven.csv");

        Assert.Equal(["a", "b", "c"], header);
        Assert.Collection(
            rows,
            row => Assert.Equal(["1", "2"], row),
            row => Assert.Equal(["3", "4", "5", "6"], row),
            row => Assert.Equal(["7"], row));
    }

    [Fact]
    public void Parse_BomCharacterInString_PreservesItInFirstHeader()
    {
        var content = "\uFEFFname,amount\nNorth,100";

        var (header, rows) = DelimitedDataParser.Parse(content, "data.csv");

        Assert.Equal(["\uFEFFname", "amount"], header);
        var row = Assert.Single(rows);
        Assert.Equal(["North", "100"], row);
    }

    [Theory]
    [InlineData(';')]
    [InlineData('|')]
    [InlineData('\t')]
    public void Parse_NonTsvFile_InfersMostFrequentUnquotedDelimiter(char delimiter)
    {
        var content = $"name{delimiter}note{delimiter}value\nalpha{delimiter}\"contains,comma\"{delimiter}1";

        var (header, rows) = DelimitedDataParser.Parse(content, "data.csv");

        Assert.Equal(["name", "note", "value"], header);
        var row = Assert.Single(rows);
        Assert.Equal(["alpha", "contains,comma", "1"], row);
    }

    [Fact]
    public void Parse_DelimiterInferenceTie_PrefersComma()
    {
        var content = "a,b;c\n1,2;3";

        var (header, rows) = DelimitedDataParser.Parse(content, "data.txt");

        Assert.Equal(["a", "b;c"], header);
        var row = Assert.Single(rows);
        Assert.Equal(["1", "2;3"], row);
    }

    [Fact]
    public void Parse_TsvExtension_ForcesTabDelimiter()
    {
        var content = "a,b\tc\n1,2\t3";

        var (header, rows) = DelimitedDataParser.Parse(content, "DATA.TSV");

        Assert.Equal(["a,b", "c"], header);
        var row = Assert.Single(rows);
        Assert.Equal(["1,2", "3"], row);
    }

    [Fact]
    public void Parse_Whitespace_PreservesFieldWhitespaceAndWhitespaceOnlyRecord()
    {
        var content = "   \n name , value \n North ,\" padded \"";

        var (header, rows) = DelimitedDataParser.Parse(content, "data.csv");

        Assert.Equal(["   "], header);
        Assert.Collection(
            rows,
            row => Assert.Equal([" name ", " value "], row),
            row => Assert.Equal([" North ", " padded "], row));
    }

    [Fact]
    public void Parse_LargeQuotedRow_PreservesEntireField()
    {
        var segment = new string('x', 128 * 1024);
        var expected = $"{segment},middle\n{segment}";
        var content = $"id,payload\n1,\"{expected}\"";

        var (header, rows) = DelimitedDataParser.Parse(content, "large.csv");

        Assert.Equal(["id", "payload"], header);
        var row = Assert.Single(rows);
        Assert.Equal("1", row[0]);
        Assert.Equal(expected, row[1]);
    }

    [Theory]
    [InlineData("a,b\n1,\"unterminated\nnext", "unterminated\nnext")]
    [InlineData("a,b\n1,\"quoted\"tail", "quotedtail")]
    [InlineData("a,b\n1,bare\"quote", "bare\"quote")]
    public void Parse_MalformedQuotes_UsesExistingPermissiveBehavior(
        string content,
        string expectedSecondField)
    {
        var exception = Record.Exception(() =>
        {
            var (header, rows) = DelimitedDataParser.Parse(content, "malformed.csv");

            Assert.Equal(["a", "b"], header);
            var row = Assert.Single(rows);
            Assert.Equal(["1", expectedSecondField], row);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Parse_NullContentAndFileName_ReturnsEmpty()
    {
        var exception = Record.Exception(() =>
        {
            var (header, rows) = DelimitedDataParser.Parse(null, null);

            Assert.Empty(header);
            Assert.Empty(rows);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void FromDelimitedContent_PreservesExactHeaderRowsAndOrder()
    {
        var artifact = TabularDocumentArtifact.FromDelimitedContent(
            "second,first,blank\n2,1,\n4,3,last",
            "ordered.csv");

        Assert.Equal(["second", "first", "blank"], artifact.Header);
        Assert.Collection(
            artifact.Rows,
            row => Assert.Equal(["2", "1", string.Empty], row),
            row => Assert.Equal(["4", "3", "last"], row));
    }
}
