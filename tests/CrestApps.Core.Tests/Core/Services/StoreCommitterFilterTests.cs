using CrestApps.Core.Filters;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class StoreCommitterFilterTests
{
    [Fact]
    public async Task ActionFilter_SuccessfulAction_CommitsOnce()
    {
        var committer = new FakeStoreCommitter();
        var filter = new StoreCommitterActionFilter(committer, NullLogger<StoreCommitterActionFilter>.Instance);

        var executingContext = CreateActionExecutingContext(out var executedContext, requestAborted: TestContext.Current.CancellationToken);

        await filter.OnActionExecutionAsync(executingContext, () => Task.FromResult(executedContext));

        Assert.Equal(1, committer.CommitCount);
    }

    [Fact]
    public async Task ActionFilter_ActionThrew_DoesNotCommit()
    {
        var committer = new FakeStoreCommitter();
        var filter = new StoreCommitterActionFilter(committer, NullLogger<StoreCommitterActionFilter>.Instance);

        var executingContext = CreateActionExecutingContext(out var executedContext, requestAborted: TestContext.Current.CancellationToken);
        executedContext.Exception = new InvalidOperationException("boom");

        await filter.OnActionExecutionAsync(executingContext, () => Task.FromResult(executedContext));

        Assert.Equal(0, committer.CommitCount);
    }

    [Fact]
    public async Task ActionFilter_ExceptionHandled_StillCommits()
    {
        var committer = new FakeStoreCommitter();
        var filter = new StoreCommitterActionFilter(committer, NullLogger<StoreCommitterActionFilter>.Instance);

        var executingContext = CreateActionExecutingContext(out var executedContext, requestAborted: TestContext.Current.CancellationToken);
        executedContext.Exception = new InvalidOperationException("boom");
        executedContext.ExceptionHandled = true;

        await filter.OnActionExecutionAsync(executingContext, () => Task.FromResult(executedContext));

        Assert.Equal(1, committer.CommitCount);
    }

    [Fact]
    public async Task ActionFilter_PassesRequestAbortedAsCancellationToken()
    {
        var committer = new FakeStoreCommitter();
        var filter = new StoreCommitterActionFilter(committer, NullLogger<StoreCommitterActionFilter>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var executingContext = CreateActionExecutingContext(out var executedContext, requestAborted: cts.Token);

        await filter.OnActionExecutionAsync(executingContext, () => Task.FromResult(executedContext));

        Assert.Equal(1, committer.CommitCount);
        Assert.True(committer.LastCancellationToken.IsCancellationRequested);
        Assert.Equal(cts.Token, committer.LastCancellationToken);
    }

    [Fact]
    public async Task EndpointFilter_SuccessfulHandler_CommitsAndReturnsResult()
    {
        var committer = new FakeStoreCommitter();
        var filter = new StoreCommitterEndpointFilter(committer, NullLogger<StoreCommitterEndpointFilter>.Instance);

        var context = new TestEndpointFilterContext(new DefaultHttpContext());
        var expected = new object();

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object>(expected));

        Assert.Same(expected, result);
        Assert.Equal(1, committer.CommitCount);
    }

    [Fact]
    public async Task EndpointFilter_HandlerThrows_DoesNotCommit()
    {
        var committer = new FakeStoreCommitter();
        var filter = new StoreCommitterEndpointFilter(committer, NullLogger<StoreCommitterEndpointFilter>.Instance);

        var context = new TestEndpointFilterContext(new DefaultHttpContext());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await filter.InvokeAsync(context, _ => throw new InvalidOperationException("boom")));

        Assert.Equal(0, committer.CommitCount);
    }

    [Fact]
    public async Task EndpointFilter_PropagatesRequestAbortedAsCancellationToken()
    {
        var committer = new FakeStoreCommitter();
        var filter = new StoreCommitterEndpointFilter(committer, NullLogger<StoreCommitterEndpointFilter>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var httpContext = new DefaultHttpContext { RequestAborted = cts.Token };
        var context = new TestEndpointFilterContext(httpContext);

        await filter.InvokeAsync(context, _ => ValueTask.FromResult<object>(null));

        Assert.Equal(1, committer.CommitCount);
        Assert.True(committer.LastCancellationToken.IsCancellationRequested);
    }

    private static ActionExecutingContext CreateActionExecutingContext(
        out ActionExecutedContext executedContext,
        CancellationToken requestAborted = default)
    {
        var httpContext = new DefaultHttpContext { RequestAborted = requestAborted };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingContext = new ActionExecutingContext(actionContext, [], new Dictionary<string, object>(), controller: new object());
        executedContext = new ActionExecutedContext(actionContext, [], controller: new object());

        return executingContext;
    }

    private sealed class FakeStoreCommitter : IStoreCommitter
    {
        public int CommitCount { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            LastCancellationToken = cancellationToken;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestEndpointFilterContext : EndpointFilterInvocationContext
    {
        public TestEndpointFilterContext(HttpContext httpContext)
        {
            HttpContext = httpContext;
        }

        public override HttpContext HttpContext { get; }

        public override IList<object> Arguments { get; } = [];

        public override T GetArgument<T>(int index) => throw new NotSupportedException();
    }
}
