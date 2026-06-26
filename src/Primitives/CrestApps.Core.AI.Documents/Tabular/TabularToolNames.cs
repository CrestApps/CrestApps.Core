namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Well-known registered names for the tabular data tools used by the built-in tabular data agent.
/// These are user-selectable tools rather than always-on system tools, so they are only included
/// when a profile (such as the tabular data agent) explicitly references them.
/// </summary>
public static class TabularToolNames
{
    /// <summary>
    /// The tool that lists the tabular tables available in the conversation along with their schema.
    /// </summary>
    public const string ListTabularData = "list_tabular_data";

    /// <summary>
    /// The tool that runs a read-only SQL query against the tabular workspace.
    /// </summary>
    public const string QueryTabularData = "query_tabular_data";

    /// <summary>
    /// The tool that runs a manipulation or schema statement against the tabular workspace.
    /// </summary>
    public const string ExecuteTabularCommand = "execute_tabular_command";
}
