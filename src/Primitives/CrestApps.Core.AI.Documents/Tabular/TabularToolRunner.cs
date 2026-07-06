using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Shared preparation logic for the tabular data tools. Resolves the conversation documents, opens
/// the file-backed workspace for the active tabular scope, and surfaces a descriptive message when
/// the operation cannot proceed. Each call creates a fresh <see cref="TabularWorkspace"/> that the
/// caller must dispose after use.
/// </summary>
internal static class TabularToolRunner
{
    /// <summary>
    /// Represents the outcome of preparing a tabular tool invocation.
    /// </summary>
    /// <param name="Workspace">The per-call workspace, when available. The caller must dispose it.</param>
    /// <param name="Tables">The synchronized tables, when the workspace was built.</param>
    /// <param name="Context">The resolved tabular tool context.</param>
    /// <param name="Error">A descriptive message when preparation could not complete; otherwise <see langword="null"/>.</param>
    public readonly record struct PreparationResult(
        TabularWorkspace Workspace,
        IReadOnlyList<TabularTableInfo> Tables,
        TabularToolContext Context,
        string Error);

    /// <summary>
    /// Resolves the tabular documents and creates a workspace ready for querying.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The preparation result.</returns>
    public static async Task<PreparationResult> PrepareAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var context = await TabularToolContext.ResolveAsync(services, cancellationToken);

        if (context is null)
        {
            return new PreparationResult(null, null, null, "Tabular data is only available within an active chat session or AI profile.");
        }

        if (context.Documents.Count == 0)
        {
            return new PreparationResult(null, null, null, "No tabular files are attached to this conversation.");
        }

        var options = services.GetRequiredService<IOptions<TabularWorkspaceOptions>>().Value;
        var workspace = new TabularWorkspace(options, context.DatabasePath);
        var tables = await workspace.EnsureReadyAsync(context.Documents, context.LoadArtifactAsync, cancellationToken);

        return new PreparationResult(workspace, tables, context, null);
    }
}
