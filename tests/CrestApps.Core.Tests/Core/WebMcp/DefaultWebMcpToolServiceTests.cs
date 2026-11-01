using System.Security.Claims;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.WebMcp;
using CrestApps.Core.AI.WebMcp.Models;
using CrestApps.Core.AI.WebMcp.Services;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.WebMcp;

public sealed class DefaultWebMcpToolServiceTests
{
    private static readonly ClaimsPrincipal _user = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Test"));

    [Fact]
    public async Task GetToolsAsync_ListsNothingByDefault()
    {
        var service = CreateService(new WebMcpOptions());

        var tools = await service.GetToolsAsync(_user, TestContext.Current.CancellationToken);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task GetToolsAsync_ListsTheAllowedTools()
    {
        var service = CreateService(new WebMcpOptions
        {
            Tools = [EchoTestTool.TheName],
        });

        var tools = await service.GetToolsAsync(_user, TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools);
        Assert.Equal(EchoTestTool.TheName, tool.Name);
        Assert.Equal("Echo", tool.Title);
        Assert.Equal("Answers the text it receives.", tool.Description);
        Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetToolsAsync_MatchesTheAllowListWithoutCaringForCase()
    {
        var service = CreateService(new WebMcpOptions
        {
            Tools = ["ECHO_TOOL", "  ", null],
        });

        var tools = await service.GetToolsAsync(_user, TestContext.Current.CancellationToken);

        Assert.Equal([EchoTestTool.TheName], tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task GetToolsAsync_NeverListsSystemOrHiddenTools_EvenWhenExposingAll()
    {
        var service = CreateService(new WebMcpOptions
        {
            ExposeAllTools = true,
            Tools = [SystemTestTool.TheName, HiddenTestTool.TheName],
        });

        var tools = await service.GetToolsAsync(_user, TestContext.Current.CancellationToken);

        Assert.Equal(
            [EchoTestTool.TheName, FailingTestTool.TheName, ServicesProbeTestTool.TheName],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetToolsAsync_LeavesOutToolsTheUserMayNotUse()
    {
        var service = CreateService(
            new WebMcpOptions
            {
                ExposeAllTools = true,
            },
            authorize: name => name != FailingTestTool.TheName);

        var tools = await service.GetToolsAsync(_user, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(tools, tool => tool.Name == FailingTestTool.TheName);
        Assert.Contains(tools, tool => tool.Name == EchoTestTool.TheName);
    }

    [Fact]
    public async Task GetToolsAsync_ListsAllowedToolInstances()
    {
        var instance = CreateInstance();
        var service = CreateService(
            new WebMcpOptions
            {
                Tools = ["docs"],
            },
            configure: services => AddToolInstance(services, instance));

        var tools = await service.GetToolsAsync(_user, TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools);
        Assert.Equal(instance.GetFunctionName(), tool.Name);
        Assert.Equal("docs", tool.Title);
    }

    [Fact]
    public async Task GetToolsAsync_LeavesOutToolInstancesTheUserMayNotUse()
    {
        var instance = CreateInstance();
        var service = CreateService(
            new WebMcpOptions
            {
                ExposeAllTools = true,
            },
            configure: services => AddToolInstance(services, instance),
            authorize: name => name != instance.GetFunctionName());

        var tools = await service.GetToolsAsync(_user, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(tools, tool => tool.Name == instance.GetFunctionName());
    }

    [Fact]
    public async Task InvokeAsync_CallsAnAllowedToolWithTheArguments()
    {
        var service = CreateService(new WebMcpOptions
        {
            Tools = [EchoTestTool.TheName],
        });

        var result = await service.InvokeAsync(
            _user,
            EchoTestTool.TheName,
            new Dictionary<string, object>
            {
                ["text"] = "hi",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(WebMcpToolInvocationStatus.Succeeded, result.Status);
        Assert.False(result.IsError);
        Assert.Equal("echo: hi", result.Text);
    }

    [Fact]
    public async Task InvokeAsync_GivesTheToolTheRequestServices()
    {
        var service = CreateService(new WebMcpOptions
        {
            Tools = [ServicesProbeTestTool.TheName],
        });

        var result = await service.InvokeAsync(_user, ServicesProbeTestTool.TheName, null, TestContext.Current.CancellationToken);

        Assert.Equal("services", result.Text);
    }

    [Theory]
    [InlineData("missing_tool")]
    [InlineData(SystemTestTool.TheName)]
    [InlineData(HiddenTestTool.TheName)]
    [InlineData(FailingTestTool.TheName)]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvokeAsync_AnswersNotFoundForToolsThatAreNotGiven(string name)
    {
        var service = CreateService(new WebMcpOptions
        {
            Tools = [EchoTestTool.TheName, SystemTestTool.TheName, HiddenTestTool.TheName],
        });

        var result = await service.InvokeAsync(_user, name, null, TestContext.Current.CancellationToken);

        Assert.Equal(WebMcpToolInvocationStatus.NotFound, result.Status);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_AnswersNotFoundWhenTheUserMayNotUseTheTool()
    {
        var service = CreateService(
            new WebMcpOptions
            {
                Tools = [EchoTestTool.TheName],
            },
            authorize: _ => false);

        var result = await service.InvokeAsync(_user, EchoTestTool.TheName, null, TestContext.Current.CancellationToken);

        Assert.Equal(WebMcpToolInvocationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task InvokeAsync_HidesTheDetailsOfAFailure()
    {
        var service = CreateService(new WebMcpOptions
        {
            Tools = [FailingTestTool.TheName],
        });

        var result = await service.InvokeAsync(_user, FailingTestTool.TheName, null, TestContext.Current.CancellationToken);

        Assert.Equal(WebMcpToolInvocationStatus.Failed, result.Status);
        Assert.True(result.IsError);
        Assert.DoesNotContain("Secret", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_CallsAnAllowedToolInstance()
    {
        var instance = CreateInstance();
        var service = CreateService(
            new WebMcpOptions
            {
                Tools = ["docs"],
            },
            configure: services => AddToolInstance(services, instance));

        var result = await service.InvokeAsync(_user, instance.GetFunctionName(), null, TestContext.Current.CancellationToken);

        Assert.Equal(WebMcpToolInvocationStatus.Succeeded, result.Status);
        Assert.Equal("instance answer", result.Text);
    }

    [Fact]
    public async Task Methods_RequireAUser()
    {
        var service = CreateService(new WebMcpOptions());

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.InvokeAsync(null, EchoTestTool.TheName, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.GetToolsAsync(null, TestContext.Current.CancellationToken));
    }

    private static AIToolInstance CreateInstance()
    {
        return new AIToolInstance
        {
            ItemId = "instance-1",
            Name = "docs",
            Source = "test-source",
        };
    }

    private static void AddToolInstance(IServiceCollection services, AIToolInstance instance)
    {
        var catalog = new Mock<INamedCatalog<AIToolInstance>>();

        catalog
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AIToolInstance> { instance });

        var source = new Mock<IAIToolInstanceSource>();

        source
            .Setup(s => s.CreateTool(instance))
            .Returns(() => AIFunctionFactory.Create(() => "instance answer", instance.GetFunctionName(), "Searches the docs."));

        services.AddSingleton(catalog.Object);
        services.AddKeyedSingleton(instance.Source, source.Object);
    }

    private static DefaultWebMcpToolService CreateService(
        WebMcpOptions options,
        Action<IServiceCollection> configure = null,
        Func<string, bool> authorize = null)
    {
        var services = new ServiceCollection();

        services.AddCoreAITool<EchoTestTool>(EchoTestTool.TheName)
            .WithTitle("Echo")
            .Selectable();
        services.AddCoreAITool<FailingTestTool>(FailingTestTool.TheName)
            .WithTitle("Fails")
            .Selectable();
        services.AddCoreAITool<ServicesProbeTestTool>(ServicesProbeTestTool.TheName)
            .WithTitle("Services probe")
            .Selectable();
        services.AddCoreAITool<SystemTestTool>(SystemTestTool.TheName);
        services.AddCoreAITool<HiddenTestTool>(HiddenTestTool.TheName)
            .Selectable()
            .Hidden();

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        var evaluator = new Mock<IAIToolAccessEvaluator>();

        evaluator
            .Setup(e => e.IsAuthorizedAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, string name) => authorize?.Invoke(name) ?? true);

        var monitor = new Mock<IOptionsMonitor<WebMcpOptions>>();

        monitor
            .SetupGet(m => m.CurrentValue)
            .Returns(options);

        return new DefaultWebMcpToolService(
            provider,
            provider.GetRequiredService<IOptions<AIToolDefinitionOptions>>(),
            monitor.Object,
            evaluator.Object,
            NullLogger<DefaultWebMcpToolService>.Instance);
    }
}
