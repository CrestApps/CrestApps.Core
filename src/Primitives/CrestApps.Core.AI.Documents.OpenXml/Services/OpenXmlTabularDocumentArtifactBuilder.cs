using System.Diagnostics;
using CrestApps.Core.AI.Documents.Tabular;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Builds tabular artifacts from Open XML spreadsheets using a sheet-streaming fast path that avoids
/// materializing the generic ingestion document graph first.
/// </summary>
public sealed class OpenXmlTabularDocumentArtifactBuilder : ITabularDocumentArtifactBuilder
{
    private readonly ILogger<OpenXmlTabularDocumentArtifactBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenXmlTabularDocumentArtifactBuilder"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public OpenXmlTabularDocumentArtifactBuilder(ILogger<OpenXmlTabularDocumentArtifactBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a tabular artifact from an Open XML spreadsheet stream.
    /// </summary>
    /// <param name="source">The spreadsheet stream.</param>
    /// <param name="fileName">The source file name.</param>
    /// <param name="contentType">The source content type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed tabular artifact.</returns>
    public Task<TabularDocumentArtifact> CreateAsync(
        Stream source,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        var stopwatch = Stopwatch.StartNew();

        using var document = SpreadsheetDocument.Open(source, false);

        var workbookPart = document.WorkbookPart;

        if (workbookPart == null)
        {
            return Task.FromResult(new TabularDocumentArtifact());
        }

        List<string> header = null;
        var rows = new List<List<string>>();
        OpenXmlTabularWorksheetReader.ReadNonEmptyRows(
            workbookPart,
            fileName,
            _logger,
            (row, firstNonEmptyRowInWorksheet) =>
            {
                if (header == null)
                {
                    header = row;

                    return;
                }

                if (!firstNonEmptyRowInWorksheet)
                {
                    rows.Add(row);
                }
            },
            cancellationToken);

        if (header == null)
        {
            return Task.FromResult(new TabularDocumentArtifact());
        }

        var artifact = new TabularDocumentArtifact
        {
            Header = header,
            Rows = rows,
        };

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "OpenXml tabular builder created artifact for '{FileName}' with {ColumnCount} column(s) and {RowCount} row(s) in {ElapsedMilliseconds} ms.",
                fileName,
                artifact.Header.Count,
                artifact.Rows.Count,
                stopwatch.ElapsedMilliseconds);
        }

        return Task.FromResult(artifact);
    }
}
