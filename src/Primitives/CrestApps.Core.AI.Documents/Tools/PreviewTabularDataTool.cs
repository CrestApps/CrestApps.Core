using System.Globalization;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tooling;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Tool that shows the reader what the tabular data looks like: a picture of the first rows and columns,
/// drawn as a spreadsheet, or a written-out table where the host cannot show a picture.
/// </summary>
/// <remarks>
/// Everything else the tabular agent does answers a question about the data. Nothing showed the data, so a
/// reader who uploaded a workbook and asked what was in it was told about it in prose and had to take that
/// on trust. A preview is what lets them check for themselves that the right file was read, that the header
/// row was found where they expect it, and that the columns mean what the answer says they mean.
/// <para>
/// The preview is of the workspace as it currently stands, not of the uploaded file, so a column added or a
/// value corrected earlier in the conversation is visible in it. That is the more useful of the two and the
/// only one consistent with every other tabular tool, and the caption says which it is.
/// </para>
/// </remarks>
public sealed class PreviewTabularDataTool : AIFunction
{
    public const string TheName = TabularToolNames.PreviewTabularData;

    private const string ImageFormat = "image";
    private const string TableFormat = "table";
    private const string PreviewExtension = ".svg";
    private const string InvocationResultCacheKey = nameof(PreviewTabularDataTool) + ".Results";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "table_name": {
          "type": "string",
          "description": "Optional name of one loaded table to preview, exactly as list_tabular_data reports it. Omit to preview every loaded table, which is what a reader asking to see their uploaded file wants."
        },
        "sql": {
          "type": "string",
          "description": "Optional single read-only SQL query (SELECT or WITH ... SELECT) in SQLite dialect to preview instead of a loaded table. Use this to show a joined, filtered, or reshaped result - in particular to show what an export will contain before creating the file. Ignored when 'table_name' is also supplied."
        },
        "format": {
          "type": "string",
          "description": "Optional. 'image' (the default) draws the data as a picture of a spreadsheet. 'table' writes it out as a Markdown table instead; use it only when the reader explicitly asks for text.",
          "enum": ["image", "table"]
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
    public override string Description => "Shows the user what the tabular data looks like, as a picture of a spreadsheet with its header row, lettered columns and numbered rows. Call this whenever the user asks to see, view, preview, or 'show me' an uploaded or exported table, and once after loading a file so they can confirm the right data was read. The preview is truncated to the first rows and columns so it stays readable, and says what it left out. It reflects the current in-memory data, including every change applied with execute_tabular_command. Returns one [fig:N] marker per previewed table that MUST be included exactly as-is in your response so the picture is drawn; never write the file name in brackets or invent your own link. This is not a download: use export_tabular_data when the user wants the file itself.";

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
        var logger = arguments.Services.GetRequiredService<ILogger<PreviewTabularDataTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        arguments.TryGetFirstString("table_name", out var tableName);
        arguments.TryGetFirstString("sql", out var sql);
        arguments.TryGetFirstString("format", out var format);

        var asTable = string.Equals(format, TableFormat, StringComparison.OrdinalIgnoreCase);

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        using var workspace = preparation.Workspace;
        var options = arguments.Services.GetRequiredService<IOptions<TabularPreviewOptions>>().Value;

        var cacheKey = BuildCacheKey(preparation.Context, tableName, sql, format, workspace.MutationVersion);
        var cachedResponse = TryGetCachedResponse(cacheKey);

        if (!string.IsNullOrEmpty(cachedResponse))
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' returned a cached preview for this turn.", Name);
            }

            return cachedResponse;
        }

        List<TabularPreviewGrid> grids;
        string skippedTables = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                var table = FindTable(preparation.Tables, tableName);

                if (table is null)
                {
                    return $"There is no loaded table named \"{tableName}\". The loaded tables are: {string.Join(", ", preparation.Tables.Select(candidate => candidate.TableName))}.";
                }

                grids = [await BuildTableGridAsync(workspace, table, options, cancellationToken)];
            }
            else if (!string.IsNullOrWhiteSpace(sql))
            {
                grids = [await BuildQueryGridAsync(workspace, sql, options, cancellationToken)];
            }
            else
            {
                var previewed = preparation.Tables.Take(Math.Max(1, options.MaxTables)).ToList();
                var built = new List<TabularPreviewGrid>(previewed.Count);

                foreach (var table in previewed)
                {
                    built.Add(await BuildTableGridAsync(workspace, table, options, cancellationToken));
                }

                grids = built;

                if (preparation.Tables.Count > previewed.Count)
                {
                    // Named rather than counted, so the next call can ask for one of them by name instead of
                    // the model guessing that there were more and what they were called.
                    skippedTables = string.Join(
                        ", ",
                        preparation.Tables.Skip(previewed.Count).Select(table => table.TableName));
                }
            }
        }
        catch (TabularSqlException ex)
        {
            return ex.Message;
        }
        catch (SqliteException ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Tabular preview failed for tool '{ToolName}'.", Name);
            }

            return $"The preview query could not be executed: {ex.Message}";
        }

        var response = asTable
            ? BuildTableResponse(grids, skippedTables)
            : await BuildImageResponseAsync(arguments.Services, preparation.Context, grids, options, skippedTables, logger, cancellationToken);

        CacheResponse(cacheKey, response);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "AI tool '{ToolName}' completed. Grids={GridCount}, Format={Format}, MutationVersion={MutationVersion}.",
                Name,
                grids.Count,
                asTable ? TableFormat : ImageFormat,
                workspace.MutationVersion);
        }

        return response;
    }

    /// <summary>
    /// Builds the window onto one loaded table, headed by the names the uploaded file used.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="table">The table.</param>
    /// <param name="options">The preview limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The shaped grid.</returns>
    private static async Task<TabularPreviewGrid> BuildTableGridAsync(
        TabularWorkspace workspace,
        TabularTableInfo table,
        TabularPreviewOptions options,
        CancellationToken cancellationToken)
    {
        var maxRows = Math.Max(1, options.MaxRows);
        var quoted = TabularWorkspaceSqliteHelpers.QuoteIdentifier(table.TableName);
        var result = await workspace.QueryAsync(
            $"SELECT * FROM {quoted} LIMIT {maxRows.ToString(CultureInfo.InvariantCulture)}",
            maxRows,
            cancellationToken);

        var headers = new List<string>(result.Columns.Count);
        var declaredTypes = new List<string>(result.Columns.Count);

        foreach (var column in result.Columns)
        {
            var info = table.Columns.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase));

            // The header the reader knows is the one their file printed, not the identifier the column was
            // given to make it safe to write in SQL.
            headers.Add(string.IsNullOrWhiteSpace(info?.SourceName) ? column : info.SourceName);
            declaredTypes.Add(info?.DeclaredType);
        }

        var formatting = await ResolveFormattingAsync(workspace, table, headers, cancellationToken);

        return TabularPreviewGrid.Create(
            string.IsNullOrWhiteSpace(table.WorksheetName) ? table.TableName : table.WorksheetName,
            BuildSubtitle(table),
            headers,
            result.Rows,
            table.RowCount,
            declaredTypes,
            options,
            formatting);
    }

    /// <summary>
    /// Loads the formatting the exported workbook would be written with for this table.
    /// </summary>
    /// <remarks>
    /// Resolved through <see cref="TabularFormattingResolver"/>, the same path the export takes, so the
    /// picture shows the reader's own header colour and their own currency and date formats rather than a
    /// look the preview invented. The precedence matches the export's: formatting recorded against the
    /// table wins, and the specification recorded for the workspace as a whole stands in when there is none.
    /// </remarks>
    /// <param name="workspace">The workspace.</param>
    /// <param name="table">The table being previewed.</param>
    /// <param name="headers">The headers this preview is drawing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The formatting, or <see langword="null"/> when none applies.</returns>
    private static async Task<SpreadsheetFormatting> ResolveFormattingAsync(
        TabularWorkspace workspace,
        TabularTableInfo table,
        List<string> headers,
        CancellationToken cancellationToken)
    {
        var stored = await workspace.GetFormattingAsync(table.TableName, cancellationToken);

        if (string.IsNullOrEmpty(stored.SpecJson))
        {
            stored = await workspace.GetFormattingAsync(
                TabularToolNames.WorkspaceFormattingKey,
                cancellationToken);
        }

        return TabularFormattingResolver.Resolve(stored.SpecJson, headers, table);
    }

    /// <summary>
    /// Builds the window onto a query result.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="sql">The read-only query.</param>
    /// <param name="options">The preview limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The shaped grid.</returns>
    private static async Task<TabularPreviewGrid> BuildQueryGridAsync(
        TabularWorkspace workspace,
        string sql,
        TabularPreviewOptions options,
        CancellationToken cancellationToken)
    {
        var maxRows = Math.Max(1, options.MaxRows);
        var result = await workspace.QueryAsync(sql, maxRows, cancellationToken);
        var totalRowCount = result.Truncated
            ? await CountQueryRowsAsync(workspace, sql, result.Rows.Count, cancellationToken)
            : result.Rows.Count;

        return TabularPreviewGrid.Create(
            "Query result",
            null,
            result.Columns,
            result.Rows,
            totalRowCount,
            null,
            options);
    }

    /// <summary>
    /// Counts the rows a truncated query would have returned, so the caption can say what the reader is
    /// seeing a part of.
    /// </summary>
    /// <remarks>
    /// Best effort. A query the count cannot be wrapped around still gets its preview; it just reports the
    /// rows on hand rather than claiming a total it does not know.
    /// </remarks>
    /// <param name="workspace">The workspace.</param>
    /// <param name="sql">The read-only query.</param>
    /// <param name="fallback">The row count to report when the total cannot be established.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The total row count.</returns>
    private static async Task<long> CountQueryRowsAsync(
        TabularWorkspace workspace,
        string sql,
        int fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            var counted = await workspace.QueryAsync($"SELECT COUNT(*) FROM ({sql.TrimEnd().TrimEnd(';')})", 1, cancellationToken);

            if (counted.Rows.Count > 0 && counted.Rows[0].Length > 0 &&
                long.TryParse(Convert.ToString(counted.Rows[0][0], CultureInfo.InvariantCulture), out var total))
            {
                return total;
            }
        }
        catch (Exception exception) when (exception is TabularSqlException or SqliteException)
        {
            // Nothing to report but the rows already read.
        }

        return fallback;
    }

    /// <summary>
    /// Draws each grid, stores it as a document the host can serve, and registers it under the marker the
    /// model is asked to repeat.
    /// </summary>
    /// <remarks>
    /// Falls back to the written-out table whenever the host cannot actually show a picture here — no
    /// conversation to attach the file to, or no endpoint to serve it from. A marker registered without a
    /// link the host can serve reaches the reader as a broken image, so the choice is made on whether the
    /// picture can be delivered rather than on whether it could be drawn.
    /// </remarks>
    /// <param name="services">The request services.</param>
    /// <param name="context">The tabular tool context.</param>
    /// <param name="grids">The shaped grids.</param>
    /// <param name="options">The preview limits.</param>
    /// <param name="skippedTables">The tables that were not previewed, when some were left out.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool response.</returns>
    private static async Task<string> BuildImageResponseAsync(
        IServiceProvider services,
        TabularToolContext context,
        List<TabularPreviewGrid> grids,
        TabularPreviewOptions options,
        string skippedTables,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null ||
            string.IsNullOrEmpty(context.ExportReferenceId) ||
            string.IsNullOrEmpty(context.ExportReferenceType))
        {
            return BuildTableResponse(grids, skippedTables);
        }

        var documentService = services.GetService<IGeneratedDocumentService>();
        var writerResolver = services.GetService<IGeneratedFileWriterResolver>();

        // Resolved rather than asked whether it is supported: the preview format is deliberately absent
        // from the formats a caller may request, so IsSupported reports false for it by design.
        if (documentService is null || writerResolver?.TryResolve(PreviewExtension, out _) != true)
        {
            return BuildTableResponse(grids, skippedTables);
        }

        if (grids.Count == 0)
        {
            return "There was no tabular data to preview.";
        }

        var figureIndex = FigureReferenceMarker.NextIndex(invocationContext);
        var pending = new List<(string Marker, AICompletionReference Reference)>(grids.Count);
        var summary = new StringBuilder();

        foreach (var grid in grids)
        {
            var (markup, shownColumnCount) = TabularPreviewSvgRenderer.Render(grid, options);

            var result = await documentService.CreateAsync(
                new GeneratedDocumentRequest(
                    context.ExportReferenceId,
                    context.ExportReferenceType,
                    BuildFileName(grid),
                    new GeneratedFileContent
                    {
                        Title = grid.Title,
                        Text = markup,
                    })
                {
                    // The preview is shown where its marker sits, so it must not also be listed underneath
                    // the answer as a file the reader asked to download.
                    RegisterDownloadReference = false,
                },
                cancellationToken);

            var link = ResolveLink(services, result.Document.ItemId);

            if (string.IsNullOrEmpty(link))
            {
                // Without an address this host serves there is no picture, only a marker the reader is left
                // looking at. Nothing is registered until every grid has one, so a host that cannot serve
                // them falls back whole rather than answering with some pictures and some markers.
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "Tabular preview fell back to a written table: no '{RouteName}' endpoint is registered to serve the picture from.",
                        DownloadAIDocument.DefaultRouteName);
                }

                return BuildTableResponse(grids, skippedTables);
            }

            var title = string.IsNullOrWhiteSpace(grid.Subtitle) ? grid.Title : $"{grid.Title} — {grid.Subtitle}";
            var marker = FigureReferenceMarker.Format(figureIndex);

            // The figure number, not a citation number. Figures count separately from [doc:n], so this
            // deliberately does not draw from NextReferenceIndex and does not consume a download's number —
            // an export in the same turn still gets [doc:1]. The two numberings cannot be confused with each
            // other downstream because the marker is replaced by its picture before anything reads a
            // reference as a citation.
            pending.Add((marker, new AICompletionReference
            {
                Text = title,
                Title = title,
                Link = link,
                IsImage = true,
                Index = figureIndex,
                ReferenceId = result.Document.ItemId,
                ReferenceType = AIReferenceTypes.DataSource.Document,
            }));

            // The caption is numbered rather than carrying the marker again. A marker sitting inside a
            // descriptive line reads as a list that has already been rendered, and the answer that follows
            // says the images are "shown above" while writing none of them.
            summary
                .Append(pending.Count)
                .Append(". ")
                .Append(title)
                .Append(": ")
                .Append(grid.BuildCaption(shownColumnCount))
                .AppendLine(".");

            // Logged as the marker paired with the address it resolves through, so a picture that does not
            // appear can be told apart from one that was never registered by grepping this line against the
            // download endpoint's for the same document id.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Tabular preview registered marker '{Marker}' for document '{DocumentId}' at '{Link}': '{FileName}', {Rows} of {TotalRows} row(s), {Columns} of {TotalColumns} column(s), {ByteCount} bytes of markup.",
                    marker,
                    result.Document.ItemId,
                    link,
                    result.Document.FileName,
                    grid.Rows.Count,
                    grid.TotalRowCount,
                    shownColumnCount,
                    grid.TotalColumnCount,
                    markup.Length);
            }

            figureIndex++;
        }

        foreach (var (marker, reference) in pending)
        {
            invocationContext.ToolReferences[marker] = reference;
        }

        var response = new StringBuilder();

        response
            .AppendLine("WRITE THE FOLLOWING LINE IN YOUR ANSWER, EXACTLY AS SHOWN, ON A LINE OF ITS OWN:")
            .AppendLine()
            .AppendLine(string.Join(' ', pending.Select(entry => entry.Marker)))
            .AppendLine()
            .AppendLine("That line is a placeholder the host replaces with the actual images. The reader sees no images at all unless it appears in your answer character for character. Do NOT describe it, renumber it, turn it into a link or a list, or write that the images are \"shown above\" in place of it. Do not call preview_tabular_data again for data that has not changed.")
            .AppendLine()
            .AppendLine("What each image shows, in order, for your own wording only:")
            .Append(summary);

        AppendSkippedTables(response, skippedTables);

        return response.ToString();
    }

    /// <summary>
    /// Writes the preview out as text, for when a picture cannot be shown or was not asked for.
    /// </summary>
    /// <param name="grids">The shaped grids.</param>
    /// <param name="skippedTables">The tables that were not previewed, when some were left out.</param>
    /// <returns>The tool response.</returns>
    private static string BuildTableResponse(List<TabularPreviewGrid> grids, string skippedTables)
    {
        if (grids.Count == 0)
        {
            return "There was no tabular data to preview.";
        }

        var builder = new StringBuilder();

        builder.AppendLine("Include the table(s) below in your answer exactly as written, so the user can see the data:");
        builder.AppendLine();

        foreach (var grid in grids)
        {
            builder.AppendLine(TabularPreviewTableRenderer.Render(grid));
            builder.AppendLine();
        }

        AppendSkippedTables(builder, skippedTables);

        return builder.ToString().TrimEnd();
    }

    private static void AppendSkippedTables(StringBuilder builder, string skippedTables)
    {
        if (string.IsNullOrEmpty(skippedTables))
        {
            return;
        }

        builder
            .Append("Not previewed: ")
            .Append(skippedTables)
            .AppendLine(". Say so, and pass 'table_name' to preview one of them.");
    }

    /// <summary>
    /// Builds the address the host serves the preview from, when it registered the endpoint.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="documentId">The generated document identifier.</param>
    /// <returns>The link, or <see langword="null"/> when the host exposes none.</returns>
    private static string ResolveLink(IServiceProvider services, string documentId)
    {
        var linkGenerator = services.GetService<LinkGenerator>();

        if (linkGenerator is null)
        {
            return null;
        }

        var values = new RouteValueDictionary
        {
            ["documentId"] = documentId,
        };

        try
        {
            var httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;

            return httpContext is null
                ? linkGenerator.GetPathByName(DownloadAIDocument.DefaultRouteName, values)
                : linkGenerator.GetPathByName(httpContext, DownloadAIDocument.DefaultRouteName, values);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the table the caller named, accepting the worksheet name and the source file name as well as
    /// the SQL table name.
    /// </summary>
    /// <param name="tables">The loaded tables.</param>
    /// <param name="name">The name the caller supplied.</param>
    /// <returns>The table, or <see langword="null"/> when nothing matches.</returns>
    private static TabularTableInfo FindTable(IReadOnlyList<TabularTableInfo> tables, string name)
    {
        var trimmed = name.Trim();

        return tables.FirstOrDefault(table => string.Equals(table.TableName, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? tables.FirstOrDefault(table => string.Equals(table.WorksheetName, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? tables.FirstOrDefault(table => string.Equals(table.SourceFileName, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSubtitle(TabularTableInfo table)
    {
        if (string.IsNullOrWhiteSpace(table.SourceFileName))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(table.WorksheetName)
            ? table.SourceFileName
            : $"{table.SourceFileName} · worksheet \"{table.WorksheetName}\"";
    }

    /// <summary>
    /// Names the preview after what it is a preview of, so a reader who downloads it can tell the files
    /// apart.
    /// </summary>
    /// <param name="grid">The shaped grid.</param>
    /// <returns>The file name.</returns>
    private static string BuildFileName(TabularPreviewGrid grid)
    {
        var baseName = string.IsNullOrWhiteSpace(grid.Title) ? "tabular" : grid.Title;
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(baseName.Length);

        foreach (var character in baseName)
        {
            builder.Append(invalidCharacters.Contains(character) || character == ' ' ? '_' : character);
        }

        baseName = builder.ToString().Trim('_');

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "tabular";
        }

        if (baseName.Length > 100)
        {
            baseName = baseName[..100];
        }

        return baseName + "_preview" + PreviewExtension;
    }

    private static string BuildCacheKey(
        TabularToolContext context,
        string tableName,
        string sql,
        string format,
        int mutationVersion)
    {
        return string.Join(
            "|",
            context.ExportReferenceType ?? string.Empty,
            context.ExportReferenceId ?? string.Empty,
            tableName?.Trim() ?? string.Empty,
            sql?.Trim() ?? string.Empty,
            format?.Trim() ?? string.Empty,
            mutationVersion.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Remembers a preview for the rest of the turn, so a model that calls twice for the same data is given
    /// the same marker rather than a second copy of the same picture.
    /// </summary>
    private static void CacheResponse(string key, string response)
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

        cache[key] = response;
    }

    private static string TryGetCachedResponse(string key)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null ||
            !invocationContext.Items.TryGetValue(InvocationResultCacheKey, out var cacheObject) ||
            cacheObject is not Dictionary<string, string> cache)
        {
            return null;
        }

        return cache.TryGetValue(key, out var response) ? response : null;
    }
}
