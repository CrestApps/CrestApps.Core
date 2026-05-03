using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Support;

internal sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue { get; set; }

    public TOptions Get(string name)
    {
        return CurrentValue;
    }

    public IDisposable OnChange(Action<TOptions, string> listener)
    {
        return EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
