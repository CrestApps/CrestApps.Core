using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.WebMcp.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.WebMcp;

public sealed class WebMcpToolResultFormatterTests
{
    [Fact]
    public void Format_AnswersAnEmptyTextForNothing()
    {
        Assert.Equal(string.Empty, WebMcpToolResultFormatter.Format(null));
    }

    [Fact]
    public void Format_KeepsTextAsItIs()
    {
        Assert.Equal("Hello", WebMcpToolResultFormatter.Format("Hello"));
    }

    [Fact]
    public void Format_ReadsAJsonStringAsItsText()
    {
        Assert.Equal("Hello", WebMcpToolResultFormatter.Format(JsonDocument.Parse("\"Hello\"").RootElement));
    }

    [Fact]
    public void Format_WritesOtherJsonAsJson()
    {
        Assert.Equal("""{"count":2}""", WebMcpToolResultFormatter.Format(JsonDocument.Parse("""{"count":2}""").RootElement));
        Assert.Equal("[1,2]", WebMcpToolResultFormatter.Format(JsonNode.Parse("[1,2]")));
    }

    [Fact]
    public void Format_JoinsTheTextOfAIContents()
    {
        var contents = new List<AIContent>
        {
            new TextContent("one"),
            new TextContent("two"),
        };

        Assert.Equal("one", WebMcpToolResultFormatter.Format(new TextContent("one")));
        Assert.Equal("one\ntwo", WebMcpToolResultFormatter.Format(contents));
    }

    [Fact]
    public void Format_SerializesOtherObjectsAsJson()
    {
        Assert.Equal("""{"Name":"x","Count":3}""", WebMcpToolResultFormatter.Format(new { Name = "x", Count = 3 }));
    }
}
