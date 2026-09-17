using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(TabularToolRunner).FullName);
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
        var workspaceLogger = services.GetRequiredService<ILogger<TabularWorkspace>>();
        var workspace = new TabularWorkspace(options, context.DatabasePath, workspaceLogger);
        var stopwatch = Stopwatch.StartNew();
        var tables = await workspace.EnsureReadyAsync(
            context.Documents,
            context.LoadArtifactAsync,
            context.ImportToWorkspaceAsync,
            cancellationToken);

        if (tables.Count == 0)
        {
            // The conversation has tabular files but none of them could be read. Say so instead of
            // handing back an empty workspace, which reads to the model as files that contain no data.
            workspace.Dispose();

            logger?.LogWarning(
                "None of the {DocumentCount} tabular document(s) attached to this conversation could be loaded into the workspace.",
                context.Documents.Count);

            return new PreparationResult(
                null,
                null,
                null,
                "The tabular files attached to this conversation could not be read. Their stored content is unavailable, so no data can be queried. Ask the user to upload the files again.");
        }

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "Prepared tabular workspace for {DocumentCount} document(s) with {TableCount} table(s) in {ElapsedMilliseconds} ms.",
                context.Documents.Count,
                tables.Count,
                stopwatch.ElapsedMilliseconds);
        }

        return new PreparationResult(workspace, tables, context, null);
    }
}
