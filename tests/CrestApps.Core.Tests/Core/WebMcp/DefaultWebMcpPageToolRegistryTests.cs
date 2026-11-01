using CrestApps.Core.AI.WebMcp;
using CrestApps.Core.AI.WebMcp.Services;

namespace CrestApps.Core.Tests.Core.WebMcp;

public sealed class DefaultWebMcpPageToolRegistryTests
{
    [Fact]
    public void Register_KeepsAValidToolForItsConnection()
    {
        var registry = new DefaultWebMcpPageToolRegistry();

        var count = registry.Register("connection-1", [Descriptor("page_get_info", inputSchema: """{"type":"object","properties":{}}""")]);

        Assert.Equal(1, count);
        var tool = Assert.Single(registry.GetTools("connection-1"));
        Assert.Equal("page_get_info", tool.Name);
        Assert.Equal("Title", tool.Title);
        Assert.Equal("Does something.", tool.Description);
        Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
        Assert.Empty(registry.GetTools("connection-2"));
    }

    [Fact]
    public void Register_ReplacesWhatTheConnectionReportedBefore()
    {
        var registry = new DefaultWebMcpPageToolRegistry();

        registry.Register("connection-1", [Descriptor("page_one")]);
        registry.Register("connection-1", [Descriptor("page_two")]);

        Assert.Equal(["page_two"], registry.GetTools("connection-1").Select(tool => tool.Name));
    }

    [Fact]
    public void Register_ForgetsTheToolsWhenNoneIsValid()
    {
        var registry = new DefaultWebMcpPageToolRegistry();

        registry.Register("connection-1", [Descriptor("page_one")]);

        var count = registry.Register("connection-1", [Descriptor("not valid")]);

        Assert.Equal(0, count);
        Assert.Empty(registry.GetTools("connection-1"));
    }

    [Fact]
    public void Remove_ForgetsTheConnectionsTools()
    {
        var registry = new DefaultWebMcpPageToolRegistry();

        registry.Register("connection-1", [Descriptor("page_one")]);
        registry.Remove("connection-1");
        registry.Remove("unknown");
        registry.Remove(null);

        Assert.Empty(registry.GetTools("connection-1"));
    }

    [Fact]
    public void GetTools_AnswersNothingForAnEmptyConnection()
    {
        var registry = new DefaultWebMcpPageToolRegistry();

        Assert.Empty(registry.GetTools(null));
        Assert.Empty(registry.GetTools(string.Empty));
    }

    [Fact]
    public void Register_RequiresAConnection()
    {
        var registry = new DefaultWebMcpPageToolRegistry();

        Assert.ThrowsAny<ArgumentException>(() => registry.Register(null, [Descriptor("page_one")]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("dotted.name")]
    [InlineData("a-name-that-is-far-too-long-for-an-ai-provider-to-accept-as-a-function-1")]
    public void Validate_DropsToolsWithNamesProvidersRefuse(string name)
    {
        Assert.Empty(WebMcpPageToolValidator.Validate([Descriptor(name)]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Validate_DropsToolsWithoutADescription(string description)
    {
        Assert.Empty(WebMcpPageToolValidator.Validate([Descriptor("page_tool", description: description)]));
    }

    [Fact]
    public void Validate_DropsToolsWithADescriptionLongerThanTheLimit()
    {
        Assert.Empty(WebMcpPageToolValidator.Validate([Descriptor("page_tool", description: new string('x', WebMcpPageToolValidator.MaxDescriptionLength + 1))]));
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("{broken")]
    [InlineData("\"text\"")]
    public void Validate_DropsToolsWhoseSchemaIsNotAJsonObject(string inputSchema)
    {
        Assert.Empty(WebMcpPageToolValidator.Validate([Descriptor("page_tool", inputSchema: inputSchema)]));
    }

    [Fact]
    public void Validate_DropsToolsWithASchemaLargerThanTheLimit()
    {
        var inputSchema = $$"""{"type":"object","description":"{{new string('x', WebMcpPageToolValidator.MaxSchemaLength)}}"}""";

        Assert.Empty(WebMcpPageToolValidator.Validate([Descriptor("page_tool", inputSchema: inputSchema)]));
    }

    [Fact]
    public void Validate_GivesAToolWithoutASchemaAnEmptyObjectSchema()
    {
        var tool = Assert.Single(WebMcpPageToolValidator.Validate([Descriptor("page_tool", inputSchema: null)]));

        Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
    }

    [Fact]
    public void Validate_KeepsTheFirstToolOfEachName()
    {
        var tool = Assert.Single(WebMcpPageToolValidator.Validate(
        [
            Descriptor("page_tool", description: "First."),
            Descriptor("PAGE_TOOL", description: "Second."),
        ]));

        Assert.Equal("First.", tool.Description);
    }

    [Fact]
    public void Validate_KeepsAtMostTheLimitOfTools()
    {
        var descriptors = Enumerable.Range(0, WebMcpPageToolValidator.MaxTools + 10)
            .Select(index => Descriptor($"page_tool_{index}"));

        Assert.Equal(WebMcpPageToolValidator.MaxTools, WebMcpPageToolValidator.Validate(descriptors).Count);
    }

    [Fact]
    public void Validate_AcceptsNothing()
    {
        Assert.Empty(WebMcpPageToolValidator.Validate(null));
        Assert.Empty(WebMcpPageToolValidator.Validate([null]));
    }

    private static WebMcpPageToolDescriptor Descriptor(string name, string description = "Does something.", string inputSchema = """{"type":"object"}""")
    {
        return new WebMcpPageToolDescriptor
        {
            Name = name,
            Title = "Title",
            Description = description,
            InputSchema = inputSchema,
        };
    }
}
