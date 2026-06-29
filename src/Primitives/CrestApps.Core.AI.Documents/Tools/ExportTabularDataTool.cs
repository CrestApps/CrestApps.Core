using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Tool that exports a read-only query result from the active in-memory tabular workspace as a
/// generated, downloadable document. The export preserves the format of the originally uploaded file
/// (for example an <c>.xlsx</c> upload exports as <c>.xlsx</c>) unless the caller requests a different
/// format, and is downloaded through the normal document authorization path.
/// </summary>
public sealed class ExportTabularDataTool : AIFunction
{
    public const string TheName = TabularToolNames.ExportTabularData;

    private const string DefaultExtension = ".csv";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "sql": {
          "type": "string",
          "description": "A single read-only SQL query (SELECT or WITH ... SELECT) in SQLite dialect. The query result is written to the generated file."
        },
        "file_name": {
          "type": "string",
          "description": "Optional file name to show to the user, including the desired extension. The extension selects the export format. If omitted, the format of the originally uploaded file is used."
        },
        "format": {
          "type": "string",
          "description": "Optional export format/extension (for example 'xlsx' or 'csv'). Used only when 'file_name' has no extension. Defaults to the format of the originally uploaded file."
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
    public override string Description => "Creates a downloadable file from a read-only SQL query over the active in-memory copy of the uploaded tabular data. By default the export keeps the format of the originally uploaded file (for example xlsx stays xlsx); a different format can be requested. The export cannot read host files or data outside this tabular workspace.";

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

        var resolver = arguments.Services.GetRequiredService<IGeneratedFileWriterResolver>();
        arguments.TryGetFirstString("file_name", out var fileName);
        arguments.TryGetFirstString("format", out var format);

        var explicitExtension = ResolveExplicitExtension(fileName, format);

        if (!string.IsNullOrEmpty(explicitExtension) && !resolver.IsSupported(explicitExtension))
        {
            var supported = string.Join(", ", resolver.SupportedExtensions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

            return $"The '{explicitExtension}' format is not supported for export. Supported formats are: {supported}.";
        }

        var targetExtension = !string.IsNullOrEmpty(explicitExtension)
            ? explicitExtension
            : ResolveOriginalExtension(preparation.Context.Documents, resolver);

        fileName = NormalizeFileName(fileName, targetExtension);

        try
        {
            var export = await preparation.Workspace.ExportAsync(sql, cancellationToken);

            if (export.Artifact.Header.Count == 0)
            {
                return "The export query did not produce any columns.";
            }

            var service = arguments.Services.GetRequiredService<IGeneratedDocumentService>();
            var content = new GeneratedFileContent
            {
                Header = export.Artifact.Header,
                Rows = export.Artifact.Rows,
            };

            var result = await service.CreateAsync(
                new GeneratedDocumentRequest(
                    preparation.Context.ExportReferenceId,
                    preparation.Context.ExportReferenceType,
                    fileName,
                    content),
                cancellationToken);

            // Persist the artifact so the generated file can itself be re-queried as tabular data.
            var artifactStore = arguments.Services.GetRequiredService<ITabularDocumentArtifactStore>();
            await artifactStore.SaveAsync(result.Document.ItemId, export.Artifact, cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return string.IsNullOrEmpty(result.ReferenceToken)
                ? $"Created \"{result.Document.FileName}\" with {export.RowCount} row(s). The generated document id is {result.Document.ItemId}."
                : $"Created \"{result.Document.FileName}\" with {export.RowCount} row(s). Include the following marker exactly as-is in your response so the user can download it: {result.ReferenceToken}";
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

    private static string ResolveExplicitExtension(string fileName, string format)
    {
        var fromFileName = GeneratedFileWriterOptions.Normalize(Path.GetExtension(fileName ?? string.Empty));

        if (!string.IsNullOrEmpty(fromFileName))
        {
            return fromFileName;
        }

        return GeneratedFileWriterOptions.Normalize(format);
    }

    private static string ResolveOriginalExtension(
        IReadOnlyList<TabularDocumentRef> documents,
        IGeneratedFileWriterResolver resolver)
    {
        var extensions = documents
            .Select(document => GeneratedFileWriterOptions.Normalize(Path.GetExtension(document.FileName ?? string.Empty)))
            .Where(extension => !string.IsNullOrEmpty(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Only preserve the original format when every source file shares it and a writer is available;
        // otherwise fall back to CSV which is universally supported.
        if (extensions.Count == 1 && resolver.IsSupported(extensions[0]))
        {
            return extensions[0];
        }

        return DefaultExtension;
    }

    private static string NormalizeFileName(string fileName, string extension)
    {
        var baseName = !string.IsNullOrWhiteSpace(fileName)
            ? Path.GetFileNameWithoutExtension(fileName.Trim())
            : "tabular-export";

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(baseName.Length);

        foreach (var character in baseName)
        {
            builder.Append(invalidCharacters.Contains(character)
                ? '_'
                : character);
        }

        baseName = builder.ToString().Trim();

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "tabular-export";
        }

        if (baseName.Length > 124)
        {
            baseName = baseName[..124];
        }

        return baseName + extension;
    }
}
