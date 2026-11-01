using System.Text.Json;
using CrestApps.Core.AI.WebMcp;
using CrestApps.Core.AI.WebMcp.Services;
using Microsoft.Extensions.AI;
using Moq;

namespace CrestApps.Core.Tests.Core.WebMcp;

public sealed class WebMcpPageToolFunctionTests
{
    private static readonly WebMcpPageTool _tool = new()
    {
        Name = "page_set_form_field",
        Title = "Set form field",
        Description = "Sets a field.",
        InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"field":{"type":"string"}}}""").RootElement.Clone(),
    };

    [Fact]
    public void Function_DescribesThePageTool()
    {
        var function = new WebMcpPageToolFunction(_tool, "connection-1", Mock.Of<IWebMcpPageToolClient>());

        Assert.Equal("page_set_form_field", function.Name);
        Assert.Equal("Sets a field.", function.Description);
        Assert.Equal("object", function.JsonSchema.GetProperty("type").GetString());
    }

    [Fact]
    public async Task InvokeAsync_RunsTheToolInTheTabWithTheArgumentsAsJson()
    {
        string sentArguments = null;
        var client = new Mock<IWebMcpPageToolClient>();

        client
            .Setup(c => c.ExecuteAsync("connection-1", "page_set_form_field", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, string argumentsJson, CancellationToken _) => sentArguments = argumentsJson)
            .ReturnsAsync(WebMcpPageToolResult.Success("Done."));

        var function = new WebMcpPageToolFunction(_tool, "connection-1", client.Object);
        var arguments = new AIFunctionArguments
        {
            ["field"] = "Title",
            ["value"] = JsonDocument.Parse("42").RootElement,
        };

        var result = await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.Equal("Done.", result);

        using var document = JsonDocument.Parse(sentArguments);

        Assert.Equal("Title", document.RootElement.GetProperty("field").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_TellsTheModelWhenThePageToolFails()
    {
        var client = new Mock<IWebMcpPageToolClient>();

        client
            .Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebMcpPageToolResult.Failure("The page did not answer in time."));

        var function = new WebMcpPageToolFunction(_tool, "connection-1", client.Object);

        var result = await function.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal("The page tool failed: The page did not answer in time.", result);
    }
}
