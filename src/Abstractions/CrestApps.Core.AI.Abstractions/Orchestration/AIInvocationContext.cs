using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Per-invocation context for AI operations, providing isolation between concurrent
/// SignalR hub method calls. Each hub invocation creates its own instance via
/// <see cref="AIInvocationScope.Begin"/>, and AI tools retrieve it via
/// <see cref="AIInvocationScope.Current"/>.
///
/// <para>
/// <b>Why this exists:</b> In SignalR, <c>HttpContext</c> is shared across all hub
/// method invocations on the same WebSocket connection. If a user sends multiple
/// messages concurrently, writing to <c>HttpContext.Items</c> causes data leaks
/// between invocations. This class uses <see cref="System.Threading.AsyncLocal{T}"/>
/// (via <see cref="AIInvocationScope"/>) to provide truly invocation-scoped storage
/// that flows correctly through async/await chains without any cross-invocation
/// contamination.
/// </para>
///
/// <para>
/// <b>For AI tools:</b> Tools are registered as singletons and the AI model does not
/// pass any invocation identifier when calling them. Tools retrieve the current
/// invocation context by calling <c>AIInvocationScope.Current</c>, which returns the
/// context for the async execution flow that is calling the tool — even when multiple
/// invocations are in flight simultaneously on different threads or continuations.
/// </para>
/// </summary>
public sealed class AIInvocationContext
{
    private int _referenceIndex;
    private List<Action> _disposeCallbacks;

    /// <summary>
    /// Gets the unique identifier for this invocation. A new value is generated for every
    /// invocation scope, so it is stable for the lifetime of a single request/prompt and
    /// differs across requests. Request-scoped resources (such as in-memory tabular workspaces)
    /// use it to reuse state within a request while rebuilding fresh on the next request.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the <see cref="AIToolExecutionContext"/> for the current invocation,
    /// providing provider, connection, and resource information.
    /// </summary>
    public AIToolExecutionContext ToolExecutionContext { get; set; }

    /// <summary>
    /// Gets or sets the completion context for the current invocation.
    /// </summary>
    public AICompletionContext CompletionContext { get; set; }

    /// <summary>
    /// Gets or sets the chat session for the current invocation.
    /// </summary>
    public AIChatSession ChatSession { get; set; }

    /// <summary>
    /// Gets or sets the chat interaction for the current invocation.
    /// </summary>
    public ChatInteraction ChatInteraction { get; set; }

    /// <summary>
    /// Gets or sets the data source identifier for the current invocation.
    /// Used by <c>DataSourceSearchTool</c> to scope searches to the correct data source.
    /// </summary>
    public string DataSourceId { get; set; }

    /// <summary>
    /// Gets the dictionary of citation references collected during tool execution
    /// (e.g., from <c>DataSourceSearchTool</c> and <c>SearchDocumentsTool</c>).
    /// Keyed by the citation marker (e.g., "[doc:1]") with the reference metadata as value.
    /// </summary>
    /// <param name="OrdinalIgnoreCase">The ordinal ignore case.</param>
    public Dictionary<string, AICompletionReference> ToolReferences { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a general-purpose property bag for extensibility.
    /// Handlers and tools can store arbitrary per-invocation data here.
    /// </summary>
    /// <param name="OrdinalIgnoreCase">The ordinal ignore case.</param>
    public Dictionary<string, object> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the current sub-agent invocation depth for the active async flow.
    /// The top-level completion runs at depth <c>0</c>; each tool-capable sub-agent that
    /// runs its own tools increments this value. Tool registry providers use it to prevent
    /// agents from invoking other agents (and thus runaway recursion).
    /// </summary>
    public int AgentInvocationDepth { get; set; }

    /// <summary>
    /// Returns the next unique reference index for citation markers (e.g., [doc:1], [doc:2], ...).
    /// This method is thread-safe and ensures a monotonically increasing counter across all
    /// handlers and tools within the same invocation, preventing index collisions between
    /// <c>DataSourcePreemptiveRagHandler</c>, <c>DocumentPreemptiveRagHandler</c>,
    /// <c>DataSourceSearchTool</c>, and <c>SearchDocumentsTool</c>.
    /// </summary>
    /// <returns>The next 1-based reference index.</returns>
    public int NextReferenceIndex()
    {
        return Interlocked.Increment(ref _referenceIndex);
    }

    /// <summary>
    /// Registers a callback to run when the invocation scope is disposed (i.e., when the
    /// request/prompt completes). Used to release request-scoped resources such as in-memory
    /// tabular workspaces so they are not retained in memory after the prompt is done.
    /// </summary>
    /// <param name="callback">The cleanup callback to run at the end of the invocation.</param>
    public void RegisterDisposeCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        (_disposeCallbacks ??= []).Add(callback);
    }

    /// <summary>
    /// Runs and clears the registered end-of-invocation cleanup callbacks. Each callback is
    /// best-effort: a failure in one callback does not prevent the others from running.
    /// </summary>
    internal void RunDisposeCallbacks()
    {
        if (_disposeCallbacks is null)
        {
            return;
        }

        foreach (var callback in _disposeCallbacks)
        {
            try
            {
                callback();
            }
            catch
            {
                // Cleanup callbacks are best-effort; never let teardown throw.
            }
        }

        _disposeCallbacks.Clear();
    }
}
