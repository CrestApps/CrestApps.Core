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
