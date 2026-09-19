namespace CrestApps.Core.AI.Services;

/// <summary>
/// One table among the search hits.
/// </summary>
public sealed class RetrievedTable
{
    /// <summary>
    /// Gets the canonical identifier of the table.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Gets the citation label the table is rendered under, for example <c>[tbl:1]</c>.
    /// </summary>
    public string Label { get; init; }

    /// <summary>
    /// Gets the table's title, which is its caption when it has one.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the caption printed with the table, when it has one.
    /// </summary>
    public string Caption { get; init; }

    /// <summary>
    /// Gets the table's column headings, when they are known.
    /// </summary>
    public string Columns { get; init; }

    /// <summary>
    /// Gets the page the table was printed on, when it is known.
    /// </summary>
    public int? Page { get; init; }
}
