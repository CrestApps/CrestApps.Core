using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Records that a tabular export produced a file during the current AI invocation.
/// <para>
/// The tabular agent exports the real rows from the workspace, but the primary model that delegated to
/// it also has a general file-creation tool. Left unguarded it will sometimes follow a successful
/// export by writing its own version of the same file from memory and handing the user that one
/// instead — a plausible-looking file with invented rows, shadowing the correct export. The signal
/// lets the file-creation tool recognize that the real file already exists.
/// </para>
/// </summary>
internal static class TabularExportSignal
{
    private const string ItemKey = "CrestApps.Tabular.LastExport";

    /// <summary>
    /// Describes a tabular export that already succeeded in this invocation.
    /// </summary>
    /// <param name="FileName">The exported file name.</param>
    /// <param name="Marker">The download marker the model must return, when one was issued.</param>
    public sealed record Result(string FileName, string Marker);

    /// <summary>
    /// Records a successful export.
    /// </summary>
    /// <param name="fileName">The exported file name.</param>
    /// <param name="marker">The download marker, when one was issued.</param>
    public static void Record(string fileName, string marker)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null || string.IsNullOrEmpty(fileName))
        {
            return;
        }

        invocationContext.Items[ItemKey] = new Result(fileName, marker);
    }

    /// <summary>
    /// Gets the export that already succeeded in this invocation, when there is one.
    /// </summary>
    /// <param name="result">The recorded export.</param>
    /// <returns><see langword="true"/> when a tabular export has already produced a file.</returns>
    public static bool TryGetLast(out Result result)
    {
        result = null;

        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null ||
            !invocationContext.Items.TryGetValue(ItemKey, out var value) ||
            value is not Result recorded)
        {
            return false;
        }

        result = recorded;

        return true;
    }
}
