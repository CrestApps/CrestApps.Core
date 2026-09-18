using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Keeps every log entry so a test can assert what was logged, and how often.
/// </summary>
/// <typeparam name="T">The category the logger is for.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>
    /// Gets the captured entries, in the order they were written.
    /// </summary>
    public List<(LogLevel Level, string Message, Exception Exception)> Entries { get; } = [];

    /// <summary>
    /// Begins a logical scope. Scopes are not captured.
    /// </summary>
    /// <typeparam name="TState">The scope state type.</typeparam>
    /// <param name="state">The scope state.</param>
    /// <returns>A disposable that does nothing.</returns>
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    /// <summary>
    /// Determines whether the level is enabled. Every level is.
    /// </summary>
    /// <param name="logLevel">The level.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <summary>
    /// Captures one entry.
    /// </summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="logLevel">The level.</param>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="state">The state.</param>
    /// <param name="exception">The exception, when there is one.</param>
    /// <param name="formatter">The message formatter.</param>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
