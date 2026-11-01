using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.WebMcp;
using CrestApps.Core.AI.WebMcp.Services;
using Moq;

namespace CrestApps.Core.Tests.Core.WebMcp;

public sealed class WebMcpPageToolRegistryProviderTests
{
    [Fact]
    public async Task GetToolsAsync_AddsThePageToolsOfTheMessagesConnection()
    {
        var provider = CreateProvider(allowPageTools: true);
        var context = CreateContext("connection-1");

        var entries = await provider.GetToolsAsync(context, TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries, candidate => candidate.Id == "webmcp:page_get_info");
        Assert.Equal("webmcp:page_get_info", entry.Id);
        Assert.Equal("page_get_info", entry.Name);
        Assert.Equal("Describes the page.", entry.Description);
        Assert.Equal(ToolRegistryEntrySource.Local, entry.Source);

        var tool = await entry.CreateAsync(null);
        var function = Assert.IsType<WebMcpPageToolFunction>(tool);

        Assert.Equal("page_get_info", function.Name);
    }

    [Fact]
    public async Task GetToolsAsync_GivesTheModelTheFunctionNameOfADottedWebMcpName()
    {
        var provider = CreateProvider(allowPageTools: true);

        var entries = await provider.GetToolsAsync(CreateContext("connection-1"), TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries, candidate => candidate.Id == "webmcp:store.get_order");
        Assert.Equal("store_get_order", entry.Name);
    }

    [Fact]
    public async Task GetToolsAsync_LeavesOutConsequentialToolsUnlessTheProfileAllowsThem()
    {
        var refusing = CreateProvider(allowPageTools: true);
        var allowing = CreateProvider(allowPageTools: true, allowConsequential: true);

        var refused = await refusing.GetToolsAsync(CreateContext("connection-1"), TestContext.Current.CancellationToken);
        var allowed = await allowing.GetToolsAsync(CreateContext("connection-1"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(refused, entry => entry.Id == "webmcp:store.place_order");
        Assert.Contains(allowed, entry => entry.Id == "webmcp:store.place_order");
    }

    [Fact]
    public async Task GetToolsAsync_AddsNothingWhenTheProfileDoesNotAllowPageTools()
    {
        var provider = CreateProvider(allowPageTools: false);

        Assert.Empty(await provider.GetToolsAsync(CreateContext("connection-1"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetToolsAsync_AddsNothingWhenTheConnectionHasNoPageTools()
    {
        var provider = CreateProvider(allowPageTools: true);

        Assert.Empty(await provider.GetToolsAsync(CreateContext("connection-2"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetToolsAsync_AddsNothingWithoutAConnectionASessionOrWhenToolsAreOff()
    {
        var provider = CreateProvider(allowPageTools: true);
        var withoutSession = new AICompletionContext();

        withoutSession.AdditionalProperties[AICompletionContextKeys.ChatConnectionId] = "connection-1";

        var disabled = CreateContext("connection-1");

        disabled.DisableTools = true;

        Assert.Empty(await provider.GetToolsAsync(CreateContext(null), TestContext.Current.CancellationToken));
        Assert.Empty(await provider.GetToolsAsync(withoutSession, TestContext.Current.CancellationToken));
        Assert.Empty(await provider.GetToolsAsync(disabled, TestContext.Current.CancellationToken));
        Assert.Empty(await provider.GetToolsAsync(null, TestContext.Current.CancellationToken));
    }

    private static AICompletionContext CreateContext(string connectionId)
    {
        var context = new AICompletionContext();

        context.AdditionalProperties[AICompletionContextKeys.Session] = new AIChatSession
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
        };

        if (connectionId is not null)
        {
            context.AdditionalProperties[AICompletionContextKeys.ChatConnectionId] = connectionId;
        }

        return context;
    }

    private static WebMcpPageToolRegistryProvider CreateProvider(bool allowPageTools, bool allowConsequential = false)
    {
        var registry = new DefaultWebMcpPageToolRegistry();

        registry.Register("connection-1",
        [
            new WebMcpPageToolDescriptor
            {
                Name = "page_get_info",
                Description = "Describes the page.",
                InputSchema = """{"type":"object"}""",
            },
            new WebMcpPageToolDescriptor
            {
                Name = "store.get_order",
                Description = "Gets an order.",
                ReadOnly = true,
            },
            new WebMcpPageToolDescriptor
            {
                Name = "store.place_order",
                Description = "Places the order.",
                Consequential = true,
            },
        ]);

        var profile = new AIProfile
        {
            ItemId = "profile-1",
        };

        profile.AlterSettings<WebMcpProfileSettings>(settings =>
        {
            settings.AllowPageTools = allowPageTools;
            settings.AllowConsequentialPageTools = allowConsequential;
        });

        var profileManager = new Mock<IAIProfileManager>();

        profileManager
            .Setup(manager => manager.FindByIdAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        return new WebMcpPageToolRegistryProvider(registry, Mock.Of<IWebMcpPageToolClient>(), profileManager.Object);
    }
}
