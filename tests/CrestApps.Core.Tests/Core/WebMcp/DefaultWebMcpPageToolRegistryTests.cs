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
    [InlineData("slash/name")]
    public void Validate_DropsToolsWhoseNamesWebMcpRefuses(string name)
    {
        Assert.Empty(WebMcpPageToolValidator.Validate([Descriptor(name)]));
    }

    [Fact]
    public void Validate_DropsToolsWithNamesLongerThanWebMcpAllows()
    {
        Assert.Empty(WebMcpPageToolValidator.Validate([Descriptor(new string('a', 129))]));
    }

    [Fact]
    public void Validate_KeepsAWebMcpNameAndGivesTheModelAFunctionNameProvidersAccept()
    {
        var tools = WebMcpPageToolValidator.Validate(
        [
            Descriptor("store.get_order"),
            Descriptor("page_get_info"),
        ]);

        Assert.Equal(["store.get_order", "page_get_info"], tools.Select(tool => tool.Name));
        Assert.Equal(["store_get_order", "page_get_info"], tools.Select(tool => tool.FunctionName));
    }

    [Fact]
    public void Validate_ShortensALongNameToAStableFunctionName()
    {
        var name = "store." + new string('x', 100);

        var first = Assert.Single(WebMcpPageToolValidator.Validate([Descriptor(name)]));
        var second = Assert.Single(WebMcpPageToolValidator.Validate([Descriptor(name)]));

        Assert.Equal(name, first.Name);
        Assert.Equal(64, first.FunctionName.Length);
        Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", first.FunctionName);
        Assert.Equal(first.FunctionName, second.FunctionName);
        Assert.NotEqual(
            first.FunctionName,
            Assert.Single(WebMcpPageToolValidator.Validate([Descriptor("store." + new string('x', 99) + "y")])).FunctionName);
    }

    [Fact]
    public void Validate_KeepsTheAnnotations()
    {
        var descriptor = Descriptor("store.place_order");

        descriptor.ReadOnly = true;
        descriptor.Consequential = true;
        descriptor.UntrustedContent = true;

        var tool = Assert.Single(WebMcpPageToolValidator.Validate([descriptor]));

        Assert.True(tool.ReadOnly);
        Assert.True(tool.Consequential);
        Assert.True(tool.UntrustedContent);
    }

    [Fact]
    public void Validate_KeepsTheFirstToolOfEachFunctionName()
    {
        var tool = Assert.Single(WebMcpPageToolValidator.Validate(
        [
            Descriptor("store.order", description: "First."),
            Descriptor("store_order", description: "Second."),
        ]));

        Assert.Equal("First.", tool.Description);
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
