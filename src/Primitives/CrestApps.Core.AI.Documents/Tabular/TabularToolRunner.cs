using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Shared preparation logic for the tabular data tools. Resolves the conversation context, ensures
/// the in-memory workspace is built for the current request (lazily loading and, when needed,
/// rebuilding it), registers an end-of-request release so the in-memory database is disposed when
/// the prompt completes, and surfaces a descriptive message when the operation cannot proceed.
/// </summary>
internal static class TabularToolRunner
{
    /// <summary>
    /// Represents the outcome of preparing a tabular tool invocation.
    /// </summary>
    /// <param name="Context">The resolved tabular context, when available.</param>
    /// <param name="Manager">The workspace manager, when available.</param>
    /// <param name="Tables">The synchronized tables, when the workspace was built.</param>
    /// <param name="Error">A descriptive message when preparation could not complete; otherwise <see langword="null"/>.</param>
    public readonly record struct PreparationResult(
        TabularToolContext Context,
        ITabularWorkspaceManager Manager,
        IReadOnlyList<TabularTableInfo> Tables,
        string Error);

    /// <summary>
    /// Resolves the tabular context and ensures the workspace is ready for the active request.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The preparation result.</returns>
    public static async Task<PreparationResult> PrepareAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = await TabularToolContext.ResolveAsync(services, cancellationToken);

        if (context is null)
        {
            return new PreparationResult(null, null, null, "Tabular data is only available within an active chat session or AI profile.");
        }

        if (context.Documents.Count == 0)
        {
            return new PreparationResult(context, null, null, "No tabular files (CSV, TSV, or Excel) are attached to this conversation.");
        }

        var manager = services.GetService<ITabularWorkspaceManager>();

        if (manager is null)
        {
            return new PreparationResult(context, null, null, "The tabular workspace service is not available.");
        }

        RegisterRequestRelease(manager, context);

        var tables = await manager.EnsureReadyAsync(context.ConversationKey, context.RequestKey, context.Documents, context.LoadContentAsync, cancellationToken);

        return new PreparationResult(context, manager, tables, null);
    }

    private static void RegisterRequestRelease(ITabularWorkspaceManager manager, TabularToolContext context)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null)
        {
            return;
        }

        // Register the end-of-request release exactly once per request so the in-memory database is
        // disposed when the prompt completes, even if multiple tabular tools run in the same cycle.
        var registrationKey = "TabularReleaseRegistered:" + context.ConversationKey;

        if (!invocationContext.Items.TryAdd(registrationKey, true))
        {
            return;
        }

        var conversationKey = context.ConversationKey;
        var requestKey = context.RequestKey;

        invocationContext.RegisterDisposeCallback(() => manager.ReleaseRequest(conversationKey, requestKey));
    }
}

