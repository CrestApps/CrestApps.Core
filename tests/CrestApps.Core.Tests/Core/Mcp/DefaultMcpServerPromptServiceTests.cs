using System.Text.Json;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class DefaultMcpServerPromptServiceTests
{
    [Fact]
    public async Task ListAsync_MergesPromptsInPrecedenceOrderAndRemovesDuplicateNames()
    {
        var catalogPrompts = new[]
        {
            CreateCatalogPrompt("catalog"),
            CreateCatalogPrompt("duplicate"),
        };
        var providerPrompts = new[]
        {
            CreateServerPrompt("duplicate"),
            CreateServerPrompt("provider"),
        };
        var sdkPrompts = new[]
        {
            CreateServerPrompt("provider"),
            CreateServerPrompt("sdk"),
        };

        var service = CreateService(catalogPrompts, providerPrompts, sdkPrompts);

        var prompts = await service.ListAsync();

        Assert.Equal(
            ["catalog", "duplicate", "provider", "sdk"],
            prompts.Select(prompt => prompt.Name));
    }

    [Fact]
    public async Task ListAsync_TreatsPromptNamesAsCaseSensitive()
    {
        var service = CreateService(
            [CreateCatalogPrompt("prompt")],
            [CreateServerPrompt("Prompt")],
            []);

        var prompts = await service.ListAsync();

        Assert.Equal(["prompt", "Prompt"], prompts.Select(prompt => prompt.Name));
    }

    [Fact]
    public async Task GetAsync_CatalogPrompt_ReturnsMessagesWithArgumentSubstitutionAndRoles()
    {
        var prompt = CreateCatalogPrompt("catalog");
        prompt.Messages =
        [
            new McpPromptMessage { Role = "user", Content = "Summarize {{topic}}" },
            new McpPromptMessage { Role = "Assistant", Content = "Sure, about {{topic}}." },
        ];

        var service = CreateService([prompt], [], []);

        var result = await service.GetAsync(CreateRequest("catalog", new Dictionary<string, JsonElement>
        {
            ["topic"] = JsonSerializer.SerializeToElement("cats"),
        }), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Messages.Count);

        Assert.Equal(Role.User, result.Messages[0].Role);
        Assert.Equal("Summarize cats", Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text);

        Assert.Equal(Role.Assistant, result.Messages[1].Role);
        Assert.Equal("Sure, about cats.", Assert.IsType<TextContentBlock>(result.Messages[1].Content).Text);
    }

    [Fact]
    public async Task GetAsync_CatalogPrompt_LeavesUnmatchedPlaceholdersAndDefaultsUnknownRoleToUser()
    {
        var prompt = CreateCatalogPrompt("catalog");
        prompt.Messages =
        [
            new McpPromptMessage { Role = "system", Content = "Hello {{missing}}" },
        ];

        var service = CreateService([prompt], [], []);

        var result = await service.GetAsync(CreateRequest("catalog", arguments: null), TestContext.Current.CancellationToken);

        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        Assert.Equal("Hello {{missing}}", Assert.IsType<TextContentBlock>(message.Content).Text);
    }

    [Fact]
    public async Task GetAsync_CatalogPrompt_WithoutMessages_ReturnsEmptyMessages()
    {
        var prompt = CreateCatalogPrompt("catalog");
        prompt.Messages = [];

        var service = CreateService([prompt], [], []);

        var result = await service.GetAsync(CreateRequest("catalog", arguments: null), TestContext.Current.CancellationToken);

        Assert.Empty(result.Messages);
    }

    private static RequestContext<GetPromptRequestParams> CreateRequest(
        string name,
        IDictionary<string, JsonElement> arguments)
    {
        var server = new Mock<McpServer>().Object;

        return new RequestContext<GetPromptRequestParams>(
            server,
            new JsonRpcRequest { Method = "prompts/get" },
            new GetPromptRequestParams
            {
                Name = name,
                Arguments = arguments,
            });
    }

    private static DefaultMcpServerPromptService CreateService(
        IReadOnlyCollection<McpPrompt> catalogPrompts,
        IReadOnlyList<McpServerPrompt> providerPrompts,
        IReadOnlyList<McpServerPrompt> sdkPrompts)
    {
        var catalog = new Mock<INamedCatalog<McpPrompt>>();
        catalog
            .Setup(instance => instance.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogPrompts);

        var provider = new Mock<IMcpPromptProvider>();
        provider
            .Setup(instance => instance.GetPromptsAsync())
            .ReturnsAsync(providerPrompts);

        return new DefaultMcpServerPromptService(catalog.Object, [provider.Object], sdkPrompts);
    }

    private static McpPrompt CreateCatalogPrompt(string name)
    {
        return new McpPrompt
        {
            ItemId = name,
            Name = name,
            Prompt = new Prompt
            {
                Name = name,
            },
        };
    }

    private static McpServerPrompt CreateServerPrompt(string name)
    {
        return McpServerPrompt.Create(
            (Func<string>)(() => string.Empty),
            new McpServerPromptCreateOptions
            {
                Name = name,
            });
    }
}
