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
    /// The tool that shows the tabular data to the reader as a picture of a spreadsheet, or as a written
    /// table where the host cannot show a picture.
    /// </summary>
    public const string PreviewTabularData = "preview_tabular_data";

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

    /// <summary>
    /// The tool that compares a measure between two tabular queries by joining them on a shared key.
    /// </summary>
    public const string CompareTabularData = "compare_tabular_data";

    /// <summary>
    /// The tool that records how an exported spreadsheet should be formatted: number formats, colors,
    /// conditional formatting, formulas, totals, and charts.
    /// </summary>
    public const string FormatTabularData = "format_tabular_data";

    /// <summary>
    /// The key formatting is recorded under when it belongs to the export as a whole rather than to one
    /// source table.
    /// <para>
    /// An export is frequently a query that joins several tables, so there is no single source table to
    /// attach its presentation to. Requiring one would make formatting unavailable for exactly the
    /// reports that need it most, so a specification recorded without naming a table is kept here and
    /// applied to any export the workspace produces.
    /// </para>
    /// </summary>
    public const string WorkspaceFormattingKey = "__workspace__";
}
