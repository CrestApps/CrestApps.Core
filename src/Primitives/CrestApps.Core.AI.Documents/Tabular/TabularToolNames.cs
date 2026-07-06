namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Well-known registered names for the tabular data tools used by the system tabular data agent.
/// These tools are hidden from the user-facing tool picker; they are only included when a profile
/// (the system tabular data agent) explicitly references them by name.
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

    /// <summary>
    /// The tool that replaces every empty cell in a table with a supplied value using one set-based update.
    /// </summary>
    public const string FillEmptyTabularCells = "fill_empty_tabular_cells";

    /// <summary>
    /// The tool that exports a read-only query result from the tabular workspace as a downloadable CSV file.
    /// </summary>
    public const string ExportTabularData = "export_tabular_data";
}
