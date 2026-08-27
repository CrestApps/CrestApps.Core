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

        List<TabularWorksheet> worksheets = [];
        TabularWorksheet current = null;
        var headerAssigned = false;

        OpenXmlTabularWorksheetReader.ReadWorksheets(
            workbookPart,
            fileName,
            _logger,
            name =>
            {
                current = new TabularWorksheet
                {
                    Name = name,
                };
                headerAssigned = false;
            },
            row =>
            {
                if (!headerAssigned)
                {
                    current.Header = row;
                    headerAssigned = true;

                    return;
                }

                current.Rows.Add(row);
            },
            () =>
            {
                // A worksheet with no non-empty rows contributes no header, so it is not surfaced as a
                // table. Every worksheet that has at least one row keeps its own header and data.
                if (headerAssigned)
                {
                    worksheets.Add(current);
                }

                current = null;
            },
            cancellationToken);

        if (worksheets.Count == 0)
        {
            return Task.FromResult(new TabularDocumentArtifact());
        }

        var artifact = new TabularDocumentArtifact
        {
            Worksheets = worksheets,
            Header = worksheets[0].Header,
            Rows = worksheets[0].Rows,
        };

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "OpenXml tabular builder created artifact for '{FileName}' with {WorksheetCount} worksheet(s) in {ElapsedMilliseconds} ms.",
                fileName,
                worksheets.Count,
                stopwatch.ElapsedMilliseconds);
        }

        return Task.FromResult(artifact);
    }
}
