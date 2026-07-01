using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public class DelimitedDataParserTests
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

    [Fact]
    public void Parse_QuotedFields_HandlesDelimitersAndNewlines()
    {
        var content = "name,note\n\"Doe, John\",\"line1\nline2\"\n\"quote \"\"x\"\"\",ok";

        var (header, rows) = DelimitedDataParser.Parse(content, "people.csv");

        Assert.Equal(["name", "note"], header);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Doe, John", rows[0][0]);
        Assert.Equal("line1\nline2", rows[0][1]);
        Assert.Equal("quote \"x\"", rows[1][0]);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmpty()
    {
        var (header, rows) = DelimitedDataParser.Parse("   ", "empty.csv");

        Assert.Empty(header);
        Assert.Empty(rows);
    }
}
