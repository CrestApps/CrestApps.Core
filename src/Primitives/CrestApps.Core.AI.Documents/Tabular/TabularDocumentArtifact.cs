namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Represents a parsed tabular document artifact that can be stored durably and reused
/// across application instances.
/// </summary>
public sealed class TabularDocumentArtifact
{
    /// <summary>
    /// Gets or sets the parsed header row.
    /// </summary>
    public List<string> Header { get; set; } = [];

    /// <summary>
    /// Gets or sets the parsed data rows.
    /// </summary>
    public List<List<string>> Rows { get; set; } = [];

    /// <summary>
    /// Gets or sets the parsed worksheets for multi-sheet sources such as Excel workbooks. When this
    /// list is populated, each worksheet is loaded as an independent table so worksheet boundaries and
    /// names are preserved. When it is empty, <see cref="Header"/> and <see cref="Rows"/> describe a
    /// single implicit worksheet (used by delimited sources and by query or export results).
    /// </summary>
    public List<TabularWorksheet> Worksheets { get; set; } = [];

    /// <summary>
    /// Creates a parsed artifact from delimited content.
    /// </summary>
    /// <param name="content">The delimited content.</param>
    /// <param name="fileName">The source file name.</param>
    public static TabularDocumentArtifact FromDelimitedContent(string content, string fileName)
    {
        var records = DelimitedDataParser.ParseRecords(content, fileName);

        if (records.Count == 0)
        {
            return new TabularDocumentArtifact();
        }

        var header = records[0];
        records.RemoveAt(0);

        return new TabularDocumentArtifact
        {
            Header = header,
            Rows = records,
        };
    }

    /// <summary>
    /// Returns the worksheets that make up this artifact. Multi-sheet sources return their parsed
    /// <see cref="Worksheets"/>; single-sheet sources return one worksheet built from <see cref="Header"/>
    /// and <see cref="Rows"/> so callers can treat every artifact uniformly.
    /// </summary>
    /// <returns>The worksheets contained in the artifact.</returns>
    public IReadOnlyList<TabularWorksheet> GetWorksheets()
    {
        if (Worksheets is { Count: > 0 })
        {
            return Worksheets;
        }

        return
        [
            new TabularWorksheet
            {
                Name = null,
                Header = Header ?? [],
                Rows = Rows ?? [],
            },
        ];
    }
}
