using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DefaultOrchestrationContextBuilderTests
{
    [Fact]
    public async Task BuildAsync_NullResource_Throws()
    {
        var builder = CreateBuilder([]);

        await Assert.ThrowsAsync<ArgumentNullException>(() => builder.BuildAsync(null, cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task BuildAsync_NoHandlers_ReturnsEmptyContext()
    {
        var builder = CreateBuilder([]);
        var resource = new AIProfile { DisplayText = "Test" };

        var context = await builder.BuildAsync(resource, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(context);
        Assert.Null(context.UserMessage);
        Assert.Null(context.SourceName);
        Assert.Empty(context.ConversationHistory);
        Assert.Empty(context.Documents);
    }

    [Fact]
    public async Task BuildAsync_HandlerCanPopulateContext()
    {
        var handler = new TestHandler(
            building: (ctx, _) => ctx.Context.SourceName = "test-source",
            built: null);
        var builder = CreateBuilder([handler]);

        var context = await builder.BuildAsync(new AIProfile(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("test-source", context.SourceName);
    }

    [Fact]
    public async Task BuildAsync_ConfigureDelegateRunsAfterBuilding()
    {
        var order = new List<string>();

        var handler = new TestHandler(
            building: (_, _) => order.Add("building"),
        built: (_, _) => order.Add("built"));
        var builder = CreateBuilder([handler]);

        var context = await builder.BuildAsync(new AIProfile(), ctx =>
        {
            order.Add("configure");
            ctx.UserMessage = "Hello";
        }, TestContext.Current.CancellationToken);

        Assert.Equal(["building", "configure", "built"], order);
        Assert.Equal("Hello", context.UserMessage);
    }

    [Fact]
    public async Task BuildAsync_ConfigureDelegateCanOverrideHandler()
    {
        var handler = new TestHandler(
            building: (ctx, _) => ctx.Context.SourceName = "handler-source",
            built: null);
        var builder = CreateBuilder([handler]);

        var context = await builder.BuildAsync(new AIProfile(), ctx =>
        {
            ctx.SourceName = "override-source";
        }, TestContext.Current.CancellationToken);

        Assert.Equal("override-source", context.SourceName);
    }

    [Fact]
    public async Task BuildAsync_HandlersExecuteInReverseOrder()
    {
        var order = new List<string>();

        var handler1 = new TestHandler(
            building: (_, _) => order.Add("handler1"),
        built: null);
        var handler2 = new TestHandler(
            building: (_, _) => order.Add("handler2"),
        built: null);

        // Handlers are reversed internally, so last registered runs first.
        var builder = CreateBuilder([handler1, handler2]);

        await builder.BuildAsync(new AIProfile(), cancellationToken: TestContext.Current.CancellationToken);

        // Reverse of [handler1, handler2] = [handler2, handler1]
        Assert.Equal(["handler2", "handler1"], order);
    }

    [Fact]
    public async Task BuildAsync_PropertiesBagIsAccessible()
    {
        var handler = new TestHandler(
            building: (ctx, _) => ctx.Context.Properties["key"] = "value",
            built: null);
        var builder = CreateBuilder([handler]);

        var context = await builder.BuildAsync(new AIProfile(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("value", context.Properties["key"]);
    }

    [Fact]
    public async Task BuildAsync_BuiltHandlerSeesConfigureChanges()
    {
        string capturedMessage = null;

        var handler = new TestHandler(
            building: null,
            built: (ctx, _) =>
            {
                capturedMessage = ((OrchestrationContext)ctx.OrchestrationContext).UserMessage;
            });
        var builder = CreateBuilder([handler]);

        await builder.BuildAsync(new AIProfile(), ctx =>
        {
            ctx.UserMessage = "After configure";
        }, TestContext.Current.CancellationToken);

        Assert.Equal("After configure", capturedMessage);
    }

    [Fact]
    public async Task BuildAsync_PropagatesCancellationTokenToHandlers()
    {
        var expectedToken = TestContext.Current.CancellationToken;
        CancellationToken buildingToken = default;
        CancellationToken builtToken = default;

        var handler = new TestHandler(
            building: (_, cancellationToken) => buildingToken = cancellationToken,
            built: (_, cancellationToken) => builtToken = cancellationToken);
        var builder = CreateBuilder([handler]);

        await builder.BuildAsync(new AIProfile(), cancellationToken: expectedToken);

        Assert.Equal(expectedToken, buildingToken);
        Assert.Equal(expectedToken, builtToken);
    }

    private static DefaultOrchestrationContextBuilder CreateBuilder(IEnumerable<IOrchestrationContextBuilderHandler> handlers)
    {
        return new DefaultOrchestrationContextBuilder(handlers, new EmptyServiceProvider(), NullLogger<DefaultOrchestrationContextBuilder>.Instance);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType) => null;
    }

    private sealed class TestHandler : IOrchestrationContextBuilderHandler
    {
        private readonly Action<OrchestrationContextBuildingContext, CancellationToken> _building;
        private readonly Action<OrchestrationContextBuiltContext, CancellationToken> _built;

        public TestHandler(
            Action<OrchestrationContextBuildingContext, CancellationToken> building,
            Action<OrchestrationContextBuiltContext, CancellationToken> built)
        {
            _building = building;
            _built = built;
        }

        public Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default)
        {
            _building?.Invoke(context, cancellationToken);

            return Task.CompletedTask;
        }

        public Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
        {
            _built?.Invoke(context, cancellationToken);

            return Task.CompletedTask;
        }
    }
}
