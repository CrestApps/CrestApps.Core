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
}
