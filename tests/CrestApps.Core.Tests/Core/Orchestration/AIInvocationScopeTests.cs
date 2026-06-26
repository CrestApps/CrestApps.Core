using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class AIInvocationScopeTests
{
    [Fact]
    public void Dispose_RunsRegisteredCallbacks()
    {
        var ran = 0;

        using (var scope = AIInvocationScope.Begin())
        {
            scope.Context.RegisterDisposeCallback(() => ran++);
            scope.Context.RegisterDisposeCallback(() => ran++);

            Assert.Equal(0, ran);
        }

        Assert.Equal(2, ran);
    }

    [Fact]
    public void Dispose_ContinuesWhenACallbackThrows()
    {
        var ran = false;

        using (var scope = AIInvocationScope.Begin())
        {
            scope.Context.RegisterDisposeCallback(() => throw new InvalidOperationException("boom"));
            scope.Context.RegisterDisposeCallback(() => ran = true);
        }

        // A throwing cleanup callback must not prevent the others from running or fail teardown.
        Assert.True(ran);
    }
}
