namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Represents the result of exporting tabular workspace data to a downloadable artifact.
/// </summary>
internal sealed class TabularExportResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabularExportResult"/> class.
    /// </summary>
    /// <param name="rowCount">The number of exported rows.</param>
    /// <param name="artifact">The parsed artifact that mirrors the exported file content.</param>
    public TabularExportResult(
        int rowCount,
        TabularDocumentArtifact artifact)
    {
        RowCount = rowCount;
        Artifact = artifact;
    }

    /// <summary>
    /// Gets the number of exported rows.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Gets the parsed artifact that mirrors the exported file content.
    /// </summary>
    public TabularDocumentArtifact Artifact { get; }
}
