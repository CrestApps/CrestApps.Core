using CrestApps.Core.AI.WebMcp;
using CrestApps.Core.AI.WebMcp.Models;
using CrestApps.Core.AI.WebMcp.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.WebMcp;

public sealed class HubWebMcpPageToolClientTests
{
    [Fact]
    public async Task ExecuteAsync_AsksTheConnectionToRunTheToolAndAnswersWhatItReturns()
    {
        var proxy = new Mock<ISingleClientProxy>();

        proxy
            .Setup(p => p.InvokeCoreAsync<WebMcpPageToolResult>(
                WebMcpConstants.ExecutePageToolMethod,
                It.Is<object[]>(args => args.Length == 2 && (string)args[0] == "page_get_info" && (string)args[1] == """{"a":1}"""),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebMcpPageToolResult.Success("The page."));

        var client = CreateClient(proxy.Object);

        var result = await client.ExecuteAsync("connection-1", "page_get_info", """{"a":1}""", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("The page.", result.Text);
    }

    [Fact]
    public async Task ExecuteAsync_SendsAnEmptyObjectWhenThereAreNoArguments()
    {
        object[] sent = null;
        var proxy = new Mock<ISingleClientProxy>();

        proxy
            .Setup(p => p.InvokeCoreAsync<WebMcpPageToolResult>(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, object[] args, CancellationToken _) => sent = args)
            .ReturnsAsync(WebMcpPageToolResult.Success("ok"));

        var client = CreateClient(proxy.Object);

        await client.ExecuteAsync("connection-1", "page_get_info", null, TestContext.Current.CancellationToken);

        Assert.Equal("{}", sent[1]);
    }

    [Fact]
    public async Task ExecuteAsync_PassesOnAnErrorTheTabReports()
    {
        var proxy = new Mock<ISingleClientProxy>();

        proxy
            .Setup(p => p.InvokeCoreAsync<WebMcpPageToolResult>(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebMcpPageToolResult.Failure("No such field."));

        var client = CreateClient(proxy.Object);

        var result = await client.ExecuteAsync("connection-1", "page_set_form_field", "{}", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("No such field.", result.Text);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsAnEmptyAnswerAsAFailure()
    {
        var proxy = new Mock<ISingleClientProxy>();

        proxy
            .Setup(p => p.InvokeCoreAsync<WebMcpPageToolResult>(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebMcpPageToolResult)null);

        var client = CreateClient(proxy.Object);

        var result = await client.ExecuteAsync("connection-1", "page_get_info", "{}", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_GivesUpWhenTheTabDoesNotAnswerInTime()
    {
        var proxy = new Mock<ISingleClientProxy>();

        proxy
            .Setup(p => p.InvokeCoreAsync<WebMcpPageToolResult>(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, object[] _, CancellationToken token) =>
            {
                await Task.Delay(Timeout.Infinite, token);

                return WebMcpPageToolResult.Success("late");
            });

        var client = CreateClient(proxy.Object, TimeSpan.FromMilliseconds(50));

        var result = await client.ExecuteAsync("connection-1", "page_get_info", "{}", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("in time", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsATabItCannotReachWithoutItsDetails()
    {
        var proxy = new Mock<ISingleClientProxy>();

        proxy
            .Setup(p => p.InvokeCoreAsync<WebMcpPageToolResult>(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Connection secret gone."));

        var client = CreateClient(proxy.Object);

        var result = await client.ExecuteAsync("connection-1", "page_get_info", "{}", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.DoesNotContain("secret", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenTheCallerCancels()
    {
        var proxy = new Mock<ISingleClientProxy>();

        proxy
            .Setup(p => p.InvokeCoreAsync<WebMcpPageToolResult>(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, object[] _, CancellationToken token) =>
            {
                await Task.Delay(Timeout.Infinite, token);

                return WebMcpPageToolResult.Success("late");
            });

        var client = CreateClient(proxy.Object);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExecuteAsync("connection-1", "page_get_info", "{}", cancellation.Token));
    }

    private static HubWebMcpPageToolClient<TestHub> CreateClient(ISingleClientProxy proxy, TimeSpan? timeout = null)
    {
        var clients = new Mock<IHubClients>();

        clients
            .Setup(c => c.Client("connection-1"))
            .Returns(proxy);

        var hubContext = new Mock<IHubContext<TestHub>>();

        hubContext
            .SetupGet(c => c.Clients)
            .Returns(clients.Object);

        return new HubWebMcpPageToolClient<TestHub>(
            hubContext.Object,
            Options.Create(new WebMcpPageToolOptions
            {
                ToolCallTimeout = timeout ?? TimeSpan.FromSeconds(10),
            }),
            NullLogger<HubWebMcpPageToolClient<TestHub>>.Instance);
    }

    public sealed class TestHub : Hub
    {
    }
}
