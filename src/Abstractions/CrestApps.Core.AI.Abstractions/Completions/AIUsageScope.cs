namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Labels the AI usage recorded within an async flow with a category and a purpose, so usage reports can be
/// broken down by the feature that made each request. Every client returned by the AI client factory reads the
/// current scope when it records usage, whatever the provider.
///
/// <para>
/// <b>Usage:</b>
/// <code>
/// using var usageScope = AIUsageScope.Begin(contextType: "Leads", purpose: "LeadScoring");
/// var client = await clientFactory.CreateChatClientAsync(deployment);
/// var response = await client.GetResponseAsync(messages);
/// </code>
/// </para>
///
/// <para>
/// Scopes nest: a value left <see langword="null"/> is inherited from the enclosing scope. A value set on the
/// request itself through <see cref="AICompletionContextKeys.UsageContextType"/> or
/// <see cref="AICompletionContextKeys.UsagePurpose"/> in the options' additional properties takes precedence
/// over the scope.
/// </para>
/// </summary>
public static class AIUsageScope
{
    private static readonly AsyncLocal<AIUsageContext> _current = new();

    /// <summary>
    /// Gets the usage labels for the current async execution flow, or <see langword="null"/> when no scope has
    /// been started.
    /// </summary>
    public static AIUsageContext Current => _current.Value;

    /// <summary>
    /// Begins a scope that labels the usage recorded within it. Dispose the returned scope to restore the
    /// enclosing labels.
    /// </summary>
    /// <param name="contextType">The category to record, or <see langword="null"/> to inherit the enclosing one.</param>
    /// <param name="purpose">The purpose to record, or <see langword="null"/> to inherit the enclosing one.</param>
    /// <returns>A scope that restores the enclosing labels when disposed.</returns>
    public static Scope Begin(string contextType = null, string purpose = null)
    {
        var parent = _current.Value;

        return new Scope(parent, new AIUsageContext
        {
            ContextType = contextType ?? parent?.ContextType,
            Purpose = purpose ?? parent?.Purpose,
        });
    }

    /// <summary>
    /// A disposable wrapper that sets <see cref="Current"/> and restores the enclosing labels when disposed.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
        private readonly AIUsageContext _parent;

        internal Scope(AIUsageContext parent, AIUsageContext context)
        {
            _parent = parent;
            Context = context;
            _current.Value = context;
        }

        /// <summary>
        /// Gets the labels this scope applies.
        /// </summary>
        public AIUsageContext Context { get; }

        /// <summary>
        /// Restores the labels of the enclosing scope.
        /// </summary>
        public void Dispose()
        {
            _current.Value = _parent;
        }
    }
}
