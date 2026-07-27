using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DefaultAICompletionContextBuilderTests
{
    [Fact]
    public async Task BuildAsync_NullResource_Throws()
    {
        var builder = CreateBuilder([]);

        await Assert.ThrowsAsync<ArgumentNullException>(() => builder.BuildAsync(null, cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task BuildAsync_HandlersExecuteInReverseOrderForBothPhases()
    {
        var order = new List<string>();
        var handler1 = new TestHandler(
            building: _ => order.Add("handler1-building"),
            built: _ => order.Add("handler1-built"));
        var handler2 = new TestHandler(
            building: _ => order.Add("handler2-building"),
            built: _ => order.Add("handler2-built"));
        var builder = CreateBuilder([handler1, handler2]);

        await builder.BuildAsync(new object(), _ => order.Add("configure"), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["handler2-building", "handler1-building", "configure", "handler2-built", "handler1-built"],
            order);
    }

    [Fact]
    public async Task BuildAsync_ArrayHandlersAreSnapshottedForEachPhase()
    {
        var order = new List<string>();
        IAICompletionContextBuilderHandler[] handlers = null;

        var replacement = new TestHandler(
            building: _ => order.Add("replacement-building"),
            built: _ => order.Add("replacement-built"));
        var handler1 = new TestHandler(
            building: _ => order.Add("handler1-building"),
            built: _ => order.Add("handler1-built"));
        var handler2 = new TestHandler(
            building: _ =>
            {
                order.Add("handler2-building");
                handlers[0] = replacement;
            },
            built: _ => order.Add("handler2-built"));

        handlers = [handler1, handler2];
        var builder = CreateBuilder(handlers);

        await builder.BuildAsync(new object(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["handler2-building", "handler1-building", "handler2-built", "replacement-built"],
            order);
    }

    [Fact]
    public async Task BuildAsync_NonArrayHandlersRemainLazyAcrossPhases()
    {
        var order = new List<string>();
        var enumerationCount = 0;
        var first = new TestHandler(
            building: _ => order.Add("first-building"),
            built: _ => order.Add("first-built"));
        var second = new TestHandler(
            building: _ => order.Add("second-building"),
            built: _ => order.Add("second-built"));

        IEnumerable<IAICompletionContextBuilderHandler> GetHandlers()
        {
            enumerationCount++;
            yield return enumerationCount == 1 ? first : second;
        }

        var builder = CreateBuilder(GetHandlers());

        await builder.BuildAsync(new object(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["first-building", "second-built"], order);
        Assert.Equal(2, enumerationCount);
    }

    private static DefaultAICompletionContextBuilder CreateBuilder(IEnumerable<IAICompletionContextBuilderHandler> handlers)
    {
        return new DefaultAICompletionContextBuilder(handlers, NullLogger<DefaultAICompletionContextBuilder>.Instance);
    }

    private sealed class TestHandler : IAICompletionContextBuilderHandler
    {
        private readonly Action<AICompletionContextBuildingContext> _building;
        private readonly Action<AICompletionContextBuiltContext> _built;

        public TestHandler(
            Action<AICompletionContextBuildingContext> building,
            Action<AICompletionContextBuiltContext> built)
        {
            _building = building;
            _built = built;
        }

        public Task BuildingAsync(AICompletionContextBuildingContext context)
        {
            _building?.Invoke(context);

            return Task.CompletedTask;
        }

        public Task BuiltAsync(AICompletionContextBuiltContext context)
        {
            _built?.Invoke(context);

            return Task.CompletedTask;
        }
    }
}
