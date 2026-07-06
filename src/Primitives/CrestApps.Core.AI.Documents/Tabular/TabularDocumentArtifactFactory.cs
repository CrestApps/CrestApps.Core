using System.Diagnostics;
using CrestApps.Core.AI.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tabular;

internal sealed class TabularDocumentArtifactFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDocumentFileStore _fileStore;
    private readonly ILogger<TabularDocumentArtifactFactory> _logger;

    public TabularDocumentArtifactFactory(
        IServiceProvider serviceProvider,
        IDocumentFileStore fileStore,
        ILogger<TabularDocumentArtifactFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _fileStore = fileStore;
        _logger = logger;
    }

    public async Task<TabularDocumentArtifact> CreateAsync(AIDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.StoredFilePath))
        {
            return null;
        }

        var extension = Path.GetExtension(document.FileName);

        await using var stream = await _fileStore.GetFileAsync(document.StoredFilePath);

        if (stream is null)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var builder = _serviceProvider.GetKeyedService<ITabularDocumentArtifactBuilder>(extension);
        TabularDocumentArtifact artifact = null;

        if (builder != null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Using keyed tabular artifact builder '{BuilderType}' for '{FileName}'.",
                    builder.GetType().FullName,
                    document.FileName);
            }

            artifact = await builder.CreateAsync(stream, document.FileName, document.ContentType, cancellationToken);
        }
        else
        {
            var reader = _serviceProvider.GetKeyedService<IngestionDocumentReader>(extension);

            if (reader == null)
            {
                return null;
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Falling back to ingestion reader '{ReaderType}' for tabular artifact '{FileName}'.",
                    reader.GetType().FullName,
                    document.FileName);
            }

            var mediaType = MediaTypeHelper.InferMediaType(extension, document.ContentType);
            var ingestionDoc = await reader.ReadAsync(stream, document.FileName, mediaType, cancellationToken);
            artifact = string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
                ? CreateSpreadsheetArtifact(ingestionDoc)
                : CreateDelimitedArtifact(ingestionDoc, document.FileName);
        }

        if (_logger.IsEnabled(LogLevel.Debug) && artifact != null)
        {
            _logger.LogDebug(
                "Built tabular artifact for '{FileName}' with {ColumnCount} column(s) and {RowCount} row(s) in {ElapsedMilliseconds} ms.",
                document.FileName,
                artifact.Header.Count,
                artifact.Rows.Count,
                stopwatch.ElapsedMilliseconds);
        }

        return artifact;
    }

    public async Task<TabularWorkspaceImportResult> ImportToWorkspaceAsync(
        AIDocument document,
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(tableName);

        if (string.IsNullOrWhiteSpace(document.StoredFilePath))
        {
            return null;
        }

        var extension = Path.GetExtension(document.FileName);
        var importer = _serviceProvider.GetKeyedService<ITabularWorkspaceImporter>(extension);

        if (importer == null)
        {
            return null;
        }

        await using var stream = await _fileStore.GetFileAsync(document.StoredFilePath);

        if (stream is null)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Using keyed tabular workspace importer '{ImporterType}' for '{FileName}'.",
                importer.GetType().FullName,
                document.FileName);
        }

        var result = await importer.ImportAsync(
            stream,
            document.FileName,
            document.ContentType,
            connection,
            tableName,
            cancellationToken);

        if (_logger.IsEnabled(LogLevel.Debug) && result != null)
        {
            _logger.LogDebug(
                "Imported tabular workspace data for '{FileName}' with {ColumnCount} column(s) and {RowCount} row(s) in {ElapsedMilliseconds} ms.",
                document.FileName,
                result.Columns.Count,
                result.RowCount,
                stopwatch.ElapsedMilliseconds);
        }

        return result;
    }

    private static TabularDocumentArtifact CreateDelimitedArtifact(IngestionDocument ingestionDoc, string fileName)
    {
        var content = string.Join('\n', ingestionDoc.EnumerateContent()
            .Select(element => element.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)));

        return TabularDocumentArtifact.FromDelimitedContent(content, fileName);
    }

    private static TabularDocumentArtifact CreateSpreadsheetArtifact(IngestionDocument ingestionDoc)
    {
        var rows = ingestionDoc.EnumerateContent()
            .Select(element => element.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Split('\t').ToList())
            .ToList();

        if (rows.Count == 0)
        {
            return new TabularDocumentArtifact();
        }

        return new TabularDocumentArtifact
        {
            Header = rows[0],
            Rows = rows.Count > 1 ? rows.Skip(1).ToList() : [],
        };
    }
}
