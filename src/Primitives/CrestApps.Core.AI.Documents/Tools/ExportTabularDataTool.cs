using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Tool that exports a read-only query result from the active in-memory tabular workspace as a
/// generated CSV document that can be downloaded through the normal document authorization path.
/// </summary>
public sealed class ExportTabularDataTool : AIFunction
{
    public const string TheName = TabularToolNames.ExportTabularData;

    private const string CsvContentType = "text/csv";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "sql": {
          "type": "string",
          "description": "A single read-only SQL query (SELECT or WITH ... SELECT) in SQLite dialect. The query result is written to a generated CSV file."
        },
        "file_name": {
          "type": "string",
          "description": "Optional CSV file name to show to the user. If omitted, a safe default file name is used."
        }
      },
      "required": ["sql"],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Creates a downloadable CSV file from a read-only SQL query over the active in-memory copy of the uploaded tabular data. The export cannot read host files or data outside this tabular workspace.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    /// <summary>
    /// Invokes core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<ExportTabularDataTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        if (!arguments.TryGetFirstString("sql", out var sql) || string.IsNullOrWhiteSpace(sql))
        {
            return "A 'sql' query is required.";
        }

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        if (string.IsNullOrEmpty(preparation.Context.ExportReferenceId) ||
            string.IsNullOrEmpty(preparation.Context.ExportReferenceType))
        {
            return "A generated tabular file can only be created from an active chat session or chat interaction.";
        }

        if (!arguments.TryGetFirstString("file_name", out var fileName) || string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "tabular-export.csv";
        }

        fileName = NormalizeCsvFileName(fileName);

        try
        {
            await using var csv = new MemoryStream();
            var export = await preparation.Workspace.ExportCsvAsync(sql, csv, cancellationToken);

            if (export.Artifact.Header.Count == 0)
            {
                return "The export query did not produce any columns.";
            }

            var document = await SaveGeneratedDocumentAsync(
                arguments.Services,
                preparation.Context,
                fileName,
                csv,
                export.Artifact,
                cancellationToken);
            var reference = AddDownloadReference(document);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return string.IsNullOrEmpty(reference)
                ? $"Created \"{document.FileName}\" with {export.RowCount} row(s). The generated document id is {document.ItemId}."
                : $"Created \"{document.FileName}\" with {export.RowCount} row(s). The downloadable file is available as {reference}.";
        }
        catch (TabularSqlException ex)
        {
            return ex.Message;
        }
        catch (SqliteException ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Tabular export failed for tool '{ToolName}'.", Name);
            }

            return $"The export query could not be executed: {ex.Message}";
        }
    }

    private static async Task<AIDocument> SaveGeneratedDocumentAsync(
        IServiceProvider services,
        TabularToolContext context,
        string fileName,
        MemoryStream csv,
        TabularDocumentArtifact artifact,
        CancellationToken cancellationToken)
    {
        var documentStore = services.GetRequiredService<IAIDocumentStore>();
        var fileStore = services.GetRequiredService<IDocumentFileStore>();
        var artifactStore = services.GetRequiredService<ITabularDocumentArtifactStore>();
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var documentId = UniqueId.GenerateId();
        var (storedFileName, storagePath) = DocumentFileStoragePath.Create(
            context.ExportReferenceType,
            context.ExportReferenceId,
            fileName);

        csv.Position = 0;
        await fileStore.SaveFileAsync(storagePath, csv);

        var document = new AIDocument
        {
            ItemId = documentId,
            ReferenceId = context.ExportReferenceId,
            ReferenceType = context.ExportReferenceType,
            FileName = fileName,
            StoredFileName = storedFileName,
            StoredFilePath = storagePath,
            ContentType = CsvContentType,
            FileSize = csv.Length,
            UploadedUtc = timeProvider.GetUtcNow().UtcDateTime,
        };

        await documentStore.CreateAsync(document, cancellationToken);
        await artifactStore.SaveAsync(document.ItemId, artifact, cancellationToken);

        return document;
    }

    private static string AddDownloadReference(AIDocument document)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null)
        {
            return null;
        }

        var referenceIndex = invocationContext.NextReferenceIndex();
        var template = $"[doc:{referenceIndex}]";
        invocationContext.ToolReferences.TryAdd(template, new AICompletionReference
        {
            Text = document.FileName,
            Title = document.FileName,
            Index = referenceIndex,
            ReferenceId = document.ItemId,
            ReferenceType = AIReferenceTypes.DataSource.Document,
        });

        return template;
    }

    private static string NormalizeCsvFileName(string fileName)
    {
        fileName = Path.GetFileName(fileName.Trim());

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "tabular-export.csv";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);

        foreach (var character in fileName)
        {
            builder.Append(invalidCharacters.Contains(character)
                ? '_'
                : character);
        }

        fileName = builder.ToString().Trim();

        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".csv";
        }

        return fileName.Length <= 128
            ? fileName
            : string.Concat(fileName.AsSpan(0, 124), ".csv");
    }
}
