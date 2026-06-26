using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Shared preparation logic for the tabular data tools. Resolves the conversation documents, builds
/// (or reuses) the per-prompt in-memory workspace stored on the current <see cref="AIInvocationContext"/>,
/// registers its disposal at the end of the prompt, and surfaces a descriptive message when the
/// operation cannot proceed.
/// </summary>
internal static class TabularToolRunner
{
    private const string WorkspaceItemKey = "TabularWorkspace";

    /// <summary>
    /// Represents the outcome of preparing a tabular tool invocation.
    /// </summary>
    /// <param name="Workspace">The per-prompt workspace, when available.</param>
    /// <param name="Tables">The synchronized tables, when the workspace was built.</param>
    /// <param name="Error">A descriptive message when preparation could not complete; otherwise <see langword="null"/>.</param>
    public readonly record struct PreparationResult(
        TabularWorkspace Workspace,
        IReadOnlyList<TabularTableInfo> Tables,
        string Error);

    /// <summary>
    /// Resolves the tabular documents and ensures the per-prompt workspace is ready.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The preparation result.</returns>
    public static async Task<PreparationResult> PrepareAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var context = await TabularToolContext.ResolveAsync(services, cancellationToken);

        if (context is null)
        {
            return new PreparationResult(null, null, "Tabular data is only available within an active chat session or AI profile.");
        }

        if (context.Documents.Count == 0)
        {
            return new PreparationResult(null, null, "No tabular files are attached to this conversation.");
        }

        var workspace = GetOrCreateWorkspace(services);

        if (workspace is null)
        {
            return new PreparationResult(null, null, "The tabular workspace is only available within an active prompt.");
        }

        var tables = await workspace.EnsureReadyAsync(context.Documents, context.LoadContentAsync, cancellationToken);

        return new PreparationResult(workspace, tables, null);
    }

    private static TabularWorkspace GetOrCreateWorkspace(IServiceProvider services)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null)
        {
            return null;
        }

        if (invocationContext.Items.TryGetValue(WorkspaceItemKey, out var existing) && existing is TabularWorkspace workspace)
        {
            return workspace;
        }

        var options = services.GetRequiredService<IOptions<TabularWorkspaceOptions>>().Value;
        workspace = new TabularWorkspace(options);

        invocationContext.Items[WorkspaceItemKey] = workspace;

        // Dispose the in-memory database when the prompt completes so it is never retained
        // between prompts; the next prompt rebuilds a fresh copy from the uploaded files.
        invocationContext.RegisterDisposeCallback(workspace.Dispose);

        return workspace;
    }
}
