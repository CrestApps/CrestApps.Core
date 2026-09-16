using System.Globalization;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Orchestration;
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
    private const string InvocationResultCacheKey = nameof(ExportTabularDataTool) + ".Results";
    private const string InvocationCountKey = nameof(ExportTabularDataTool) + ".InvocationCount";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "sql": {
          "type": "string",
          "description": "Optional single read-only SQL query (SELECT or WITH ... SELECT) in SQLite dialect to shape the exported data. OMIT this to export the entire current in-memory table (all rows and columns, including every change applied with execute_tabular_command). Only provide it when the user wants a specific subset or custom shape."
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
      "required": [],
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
    public override string Description => "Creates a downloadable file from the active in-memory copy of the uploaded tabular data, reflecting every change already applied with execute_tabular_command (the exported data comes from memory, not the original uploaded file). Omit 'sql' to export the entire current table; provide a read-only SELECT only to export a specific subset. By default the export keeps the format of the originally uploaded file (for example xlsx stays xlsx); a different format can be requested. The export cannot read host files or data outside this tabular workspace. Returns a [doc:N] marker that MUST be included exactly as-is in your response so the UI renders the download link; never write the file name in brackets or invent your own link.";

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
        var invocationNumber = IncrementInvocationCount();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked (call #{InvocationNumber}).", Name, invocationNumber);
        }

        arguments.TryGetFirstString("sql", out var sql);

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        using var workspace = preparation.Workspace;

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

        var formattingTable = ResolveFormattingTable(preparation.Tables, sql);
        var storedFormatting = formattingTable is null
            ? (SpecJson: null, Revision: 0)
            : await workspace.GetFormattingAsync(formattingTable.TableName, cancellationToken);

        var cachedResponse = TryGetCachedResponse(
            preparation.Context.ExportReferenceType,
            preparation.Context.ExportReferenceId,
            fileName,
            sql,
            workspace.MutationVersion,
            storedFormatting.Revision);

        if (!string.IsNullOrEmpty(cachedResponse))
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "AI tool '{ToolName}' returned cached response (call #{InvocationNumber}). Documents={DocumentCount}, Tables={TableCount}, FullExport={IsFullExport}, FileName='{FileName}', MutationVersion={MutationVersion}.",
                    Name,
                    invocationNumber,
                    preparation.Context.Documents.Count,
                    preparation.Tables.Count,
                    string.IsNullOrWhiteSpace(sql),
                    fileName,
                    workspace.MutationVersion);
            }

            return cachedResponse;
        }

        try
        {
            var export = string.IsNullOrWhiteSpace(sql)
                ? await workspace.ExportFullAsync(cancellationToken)
                : await workspace.ExportAsync(sql, cancellationToken);

            if (export.Artifact.Header.Count == 0)
            {
                return "The export query did not produce any columns.";
            }

            var service = arguments.Services.GetRequiredService<IGeneratedDocumentService>();
            var content = new GeneratedFileContent
            {
                Header = export.Artifact.Header,
                Rows = export.Artifact.Rows,
                SpreadsheetFormatting = ResolveFormatting(
                    storedFormatting.SpecJson,
                    export.Artifact.Header,
                    formattingTable),
            };

            var result = await service.CreateAsync(
                new GeneratedDocumentRequest(
                    preparation.Context.ExportReferenceId,
                    preparation.Context.ExportReferenceType,
                    fileName,
                    content),
                cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "AI tool '{ToolName}' completed (call #{InvocationNumber}). Documents={DocumentCount}, Tables={TableCount}, FullExport={IsFullExport}, FileName='{FileName}', Rows={RowCount}, MutationVersion={MutationVersion}.",
                    Name,
                    invocationNumber,
                    preparation.Context.Documents.Count,
                    preparation.Tables.Count,
                    string.IsNullOrWhiteSpace(sql),
                    fileName,
                    export.RowCount,
                    workspace.MutationVersion);
            }

            var response = string.IsNullOrEmpty(result.ReferenceToken)
                ? $"Created \"{result.Document.FileName}\" with {export.RowCount} row(s). The generated document id is {result.Document.ItemId}."
                : $"Return this download marker verbatim and do not call export_tabular_data or generate_file again for this file: {result.ReferenceToken}";

            CacheResponse(
                preparation.Context.ExportReferenceType,
                preparation.Context.ExportReferenceId,
                fileName,
                sql,
                workspace.MutationVersion,
                storedFormatting.Revision,
                response);

            return response;
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

    private static int IncrementInvocationCount()
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null)
        {
            return 1;
        }

        if (!invocationContext.Items.TryGetValue(InvocationCountKey, out var countObject) ||
            countObject is not int count)
        {
            count = 0;
        }

        count++;
        invocationContext.Items[InvocationCountKey] = count;

        return count;
    }

    private static void CacheResponse(
        string referenceType,
        string referenceId,
        string fileName,
        string sql,
        int mutationVersion,
        int formattingRevision,
        string response)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null || string.IsNullOrEmpty(response))
        {
            return;
        }

        if (!invocationContext.Items.TryGetValue(InvocationResultCacheKey, out var cacheObject) ||
            cacheObject is not Dictionary<string, string> cache)
        {
            cache = new Dictionary<string, string>(StringComparer.Ordinal);
            invocationContext.Items[InvocationResultCacheKey] = cache;
        }

        cache[BuildCacheKey(referenceType, referenceId, fileName, sql, mutationVersion, formattingRevision)] = response;
    }

    private static string TryGetCachedResponse(
        string referenceType,
        string referenceId,
        string fileName,
        string sql,
        int mutationVersion,
        int formattingRevision)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null ||
            !invocationContext.Items.TryGetValue(InvocationResultCacheKey, out var cacheObject) ||
            cacheObject is not Dictionary<string, string> cache)
        {
            return null;
        }

        return cache.TryGetValue(
            BuildCacheKey(referenceType, referenceId, fileName, sql, mutationVersion, formattingRevision),
            out var response)
            ? response
            : null;
    }

    private static string BuildCacheKey(
        string referenceType,
        string referenceId,
        string fileName,
        string sql,
        int mutationVersion,
        int formattingRevision)
    {
        return string.Join(
            "|",
            referenceType ?? string.Empty,
            referenceId ?? string.Empty,
            fileName ?? string.Empty,
            mutationVersion.ToString(CultureInfo.InvariantCulture),
            // The formatting revision is part of the key so that re-exporting after a formatting change
            // produces a new file instead of handing back the previous, unformatted one.
            formattingRevision.ToString(CultureInfo.InvariantCulture),
            sql?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// Chooses the table whose recorded formatting applies to this export. A full export has exactly
    /// one table; a query-shaped export uses the only loaded table when there is just one, because that
    /// is the table the query necessarily came from.
    /// </summary>
    /// <param name="tables">The loaded tables.</param>
    /// <param name="sql">The export query, when one was supplied.</param>
    /// <returns>The table, or <see langword="null"/> when it cannot be determined.</returns>
    private static TabularTableInfo ResolveFormattingTable(IReadOnlyList<TabularTableInfo> tables, string sql)
    {
        if (tables is null || tables.Count == 0)
        {
            return null;
        }

        if (tables.Count == 1)
        {
            return tables[0];
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        // With several tables loaded, only a query naming exactly one of them can be attributed to it.
        TabularTableInfo matched = null;

        foreach (var table in tables)
        {
            if (sql.Contains(table.TableName, StringComparison.OrdinalIgnoreCase))
            {
                if (matched is not null)
                {
                    return null;
                }

                matched = table;
            }
        }

        return matched;
    }

    /// <summary>
    /// Loads the recorded formatting and aligns its column references with the headers this export
    /// actually produced.
    /// <para>
    /// A full export writes the original source headers while a query writes SQL column names, so a
    /// specification recorded against one naming would silently format nothing against the other.
    /// Translating the names keeps a formatting request working no matter how the file is exported.
    /// </para>
    /// </summary>
    /// <param name="specJson">The stored specification.</param>
    /// <param name="header">The header row this export produced.</param>
    /// <param name="table">The table the formatting was recorded against.</param>
    /// <returns>The formatting to apply, or <see langword="null"/> when none is recorded.</returns>
    private static SpreadsheetFormatting ResolveFormatting(
        string specJson,
        List<string> header,
        TabularTableInfo table)
    {
        var formatting = SpreadsheetFormattingJson.Deserialize(specJson);

        if (formatting is null || table is null || header is null || header.Count == 0)
        {
            return formatting;
        }

        var aliases = BuildColumnAliases(header, table);

        if (aliases.Count == 0)
        {
            return formatting;
        }

        foreach (var column in formatting.Columns)
        {
            column.Column = Translate(column.Column, aliases);
        }

        foreach (var conditional in formatting.ConditionalFormats)
        {
            conditional.Column = Translate(conditional.Column, aliases);
        }

        if (formatting.TotalRow?.Columns is not null)
        {
            foreach (var total in formatting.TotalRow.Columns)
            {
                total.Column = Translate(total.Column, aliases);
            }
        }

        foreach (var chart in formatting.Charts)
        {
            chart.CategoryColumn = Translate(chart.CategoryColumn, aliases);

            for (var index = 0; index < chart.ValueColumns.Count; index++)
            {
                chart.ValueColumns[index] = Translate(chart.ValueColumns[index], aliases);
            }
        }

        return formatting;
    }

    private static Dictionary<string, string> BuildColumnAliases(
        List<string> header,
        TabularTableInfo table)
    {
        var headerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in header)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                headerNames.Add(name.Trim());
            }
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.SourceName) ||
                string.Equals(column.SourceName, column.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only map toward a name the export actually wrote, so a reference that already matches is
            // never rewritten into one that does not.
            if (headerNames.Contains(column.SourceName) && !headerNames.Contains(column.Name))
            {
                aliases[column.Name] = column.SourceName;
            }
            else if (headerNames.Contains(column.Name) && !headerNames.Contains(column.SourceName))
            {
                aliases[column.SourceName] = column.Name;
            }
        }

        return aliases;
    }

    private static string Translate(string name, Dictionary<string, string> aliases)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return aliases.TryGetValue(name.Trim(), out var alias)
            ? alias
            : name;
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
