using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.WebMcp;
using CrestApps.Core.AI.WebMcp.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI.Chat;

public sealed class AIChatHubPageToolsTests
{
    [Fact]
    public async Task SetPageTools_RecordsTheValidToolsOfTheCallersConnection()
    {
        var registry = new DefaultWebMcpPageToolRegistry();
        var hub = CreateHub(registry);

        var count = await hub.SetPageTools([Descriptor("page_get_info"), Descriptor("not valid")]);

        Assert.Equal(1, count);
        Assert.Equal(["page_get_info"], registry.GetTools("connection-1").Select(tool => tool.Name));
    }

    [Fact]
    public async Task SetPageTools_RecordsNothingWhenTheHostHasNoPageTools()
    {
        var hub = new TestHub(new ServiceCollection().BuildServiceProvider())
        {
            Context = CreateCallerContext(),
        };

        var count = await hub.SetPageTools([Descriptor("page_get_info")]);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SetPageTools_AcceptsNoTools()
    {
        var registry = new DefaultWebMcpPageToolRegistry();
        var hub = CreateHub(registry);

        await hub.SetPageTools([Descriptor("page_get_info")]);

        var count = await hub.SetPageTools(null);

        Assert.Equal(0, count);
        Assert.Empty(registry.GetTools("connection-1"));
    }

    [Fact]
    public async Task ClearPageTools_ForgetsTheCallersTools()
    {
        var registry = new DefaultWebMcpPageToolRegistry();
        var hub = CreateHub(registry);

        await hub.SetPageTools([Descriptor("page_get_info")]);
        await hub.ClearPageTools();

        Assert.Empty(registry.GetTools("connection-1"));
    }

    [Fact]
    public async Task OnDisconnectedAsync_ForgetsTheConnectionsTools()
    {
        var registry = new DefaultWebMcpPageToolRegistry();
        var hub = CreateHub(registry);

        await hub.SetPageTools([Descriptor("page_get_info")]);
        await hub.OnDisconnectedAsync(null);

        Assert.Empty(registry.GetTools("connection-1"));
    }

    private static TestHub CreateHub(IWebMcpPageToolRegistry registry)
    {
        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        return new TestHub(services)
        {
            Context = CreateCallerContext(),
        };
    }

    private static HubCallerContext CreateCallerContext()
    {
        var context = new Mock<HubCallerContext>();

        context.SetupGet(c => c.ConnectionId).Returns("connection-1");
        context.SetupGet(c => c.Items).Returns(new Dictionary<object, object>());

        return context.Object;
    }

    private static WebMcpPageToolDescriptor Descriptor(string name)
    {
        return new WebMcpPageToolDescriptor
        {
            Name = name,
            Description = "A page tool.",
            InputSchema = """{"type":"object"}""",
        };
    }

    private sealed class TestHub : AIChatHubCore<IAIChatHubClient>
    {
        public TestHub(IServiceProvider services)
            : base(services, TimeProvider.System, NullLogger.Instance)
        {
        }
    }
}
