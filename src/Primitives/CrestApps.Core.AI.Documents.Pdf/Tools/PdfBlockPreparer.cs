using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Readies blocks that arrive in a tool call for the document: gives each an identifier and reads the data
/// the model pointed at — a <c>[chart:…]</c> marker, a query over the uploaded spreadsheets — into the block.
/// </summary>
internal static class PdfBlockPreparer
{
    private const int DefaultMaxRows = 1000;
    private const int MaxRowsLimit = 5000;
    private const string ChartMarkerPrefix = "[chart:";

    /// <summary>
    /// Readies blocks for a document.
    /// </summary>
    /// <param name="definition">The document the blocks are added to, which issues their identifiers.</param>
    /// <param name="blocks">The blocks.</param>
    /// <param name="services">The request services, used to reach the tabular workspace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="requireChartData">
    /// Whether the blocks are the model's own writing, as opposed to content converted from a file. For them, a
    /// chart block that ends up with nothing to plot fails the call — a chart left out with only a warning was
    /// reported to the user as "the charts are embedded" while the file had headings over empty space — and so
    /// does chart marker text, and a heading that repeats the one right before it is left out.
    /// </param>
    /// <param name="precedingBlock">The block the new blocks follow in the document, when there is one.</param>
    /// <returns>Notes about data that could not be read as asked.</returns>
    public static async Task<List<string>> PrepareAsync(
        PdfDocumentDefinition definition,
        List<PdfBlockDefinition> blocks,
        IServiceProvider services,
        CancellationToken cancellationToken,
        bool requireChartData = true,
        PdfBlockDefinition precedingBlock = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var warnings = new List<string>();

        if (blocks is null || blocks.Count == 0)
        {
            return warnings;
        }

        ReplaceHandWrittenContents(definition, blocks, warnings);

        if (requireChartData)
        {
            await ChartsFromMarkerTextAsync(blocks, services);
            RemoveRepeatedHeadings(blocks, precedingBlock, warnings);
        }

        TabularToolRunner.PreparationResult? tabular = null;

        try
        {
            foreach (var block in blocks)
            {
                if (block is null)
                {
                    continue;
                }

                block.Id = "b" + definition.NextBlockNumber++.ToString(CultureInfo.InvariantCulture);

                var type = PdfLayoutBuilder.NormalizeType(block);

                if (!string.IsNullOrEmpty(type))
                {
                    block.Type = type;
                }

                LiftFlatObject(block);
                ChartFromImageSource(block);

                if (block.Chart?.ConversationChart is { } number)
                {
                    await ReadConversationChartAsync(block.Chart, number, services);
                }

                if (block.Chart is { } chart && !string.IsNullOrWhiteSpace(chart.ChartJs))
                {
                    ReadChartJs(chart, warnings);
                }

                if (requireChartData && block.Type == PdfBlockTypes.Chart && block.Chart?.Source is null && !HasValues(block.Chart))
                {
                    var listed = PdfConversationCharts.Describe(await PdfConversationCharts.GetAsync(services));

                    throw new PdfToolException(
                        $"Nothing was added: the chart{(string.IsNullOrWhiteSpace(block.Chart?.Title) ? string.Empty : $" \"{block.Chart.Title}\"")} has no data to plot, so it would be an empty space in the document. " +
                        "Give it labels and series with the real values, conversation_chart with the number of a chart shown earlier, source over uploaded tabular data, or chart_js with the whole [chart:…] marker." +
                        (listed is null ? string.Empty : Environment.NewLine + listed));
                }

                var needsTabular = block.Table?.Source is not null || block.Chart?.Source is not null;

                if (!needsTabular)
                {
                    continue;
                }

                tabular ??= await TabularToolRunner.PrepareAsync(services, cancellationToken);

                if (tabular.Value.Error is not null)
                {
                    throw new PdfToolException($"Block {block.Id} reads uploaded tabular data, but none can be read: {tabular.Value.Error}");
                }

                if (block.Table?.Source is { } tableSource)
                {
                    await ReadTableAsync(block.Table, tableSource, tabular.Value, cancellationToken);
                    block.Table.Source = null;
                }

                if (block.Chart?.Source is { } chartSource)
                {
                    await ReadChartAsync(block.Chart, chartSource, tabular.Value, cancellationToken);
                    block.Chart.Source = null;
                }
            }
        }
        finally
        {
            tabular?.Workspace?.Dispose();
        }

        return warnings;
    }

    /// <summary>
    /// Replaces a table of contents written as content — a "Table of Contents" heading over a list of the
    /// document's headings — with the generated one, which has page numbers and links and stays current.
    /// </summary>
    /// <param name="definition">The document.</param>
    /// <param name="blocks">The blocks of the call, changed in place.</param>
    /// <param name="notes">Receives a note when one was replaced.</param>
    internal static void ReplaceHandWrittenContents(PdfDocumentDefinition definition, List<PdfBlockDefinition> blocks, List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(blocks);

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if (block is null || PdfLayoutBuilder.NormalizeType(block) != PdfBlockTypes.Heading || !IsContentsTitle(block.Text))
            {
                continue;
            }

            var end = index + 1;
            var next = end < blocks.Count ? blocks[end] : null;

            // The list under it is the hand-written entries; it is taken only when its items are the
            // document's own headings, so an ordinary list under a heading called "Contents" is kept.
            if (next is not null && PdfLayoutBuilder.NormalizeType(next) == PdfBlockTypes.List)
            {
                if (!ListsHeadings(next, definition, blocks))
                {
                    continue;
                }

                end++;
            }

            // The generated contents get a page of their own, so a break the model put after its own is not
            // needed and would leave a blank page.
            if (end < blocks.Count && blocks[end] is { } after && PdfLayoutBuilder.NormalizeType(after) == PdfBlockTypes.PageBreak)
            {
                end++;
            }

            blocks.RemoveRange(index, end - index);

            definition.TableOfContents ??= new PdfTableOfContentsDefinition();
            definition.TableOfContents.Enabled = true;
            definition.TableOfContents.Title ??= Plain(block.Text);

            notes.Add("The table of contents written as a heading and a list was replaced with a generated one (table_of_contents), which shows each heading's page number and links to it. Do not write one by hand.");

            return;
        }
    }

    private static bool IsContentsTitle(string text)
    {
        return Plain(text).TrimEnd(':').ToLowerInvariant() is "table of contents" or "contents" or "toc" or "content";
    }

    private static bool ListsHeadings(PdfBlockDefinition list, PdfDocumentDefinition definition, List<PdfBlockDefinition> blocks)
    {
        var items = (list.Items ?? []).Select(EntryText).Where(item => item.Length > 0).ToList();

        if (items.Count == 0)
        {
            return false;
        }

        var headings = new HashSet<string>(
            (definition.Sections ?? []).SelectMany(section => section.Blocks ?? []).Concat(blocks)
                .Where(candidate => candidate is not null && PdfLayoutBuilder.NormalizeType(candidate) == PdfBlockTypes.Heading && !IsContentsTitle(candidate.Text))
                .Select(candidate => EntryText(candidate.Text)),
            StringComparer.OrdinalIgnoreCase);

        return items.Count(headings.Contains) * 2 >= items.Count;
    }

    private static string EntryText(string text)
    {
        // "1. Revenue Analysis ........ 3" names the heading "Revenue Analysis".
        var plain = Plain(text);
        var start = 0;

        while (start < plain.Length && (char.IsDigit(plain[start]) || plain[start] is '.' or ')' or '-' or '•' or '*' or ' '))
        {
            start++;
        }

        var end = plain.Length;

        while (end > start && (char.IsDigit(plain[end - 1]) || plain[end - 1] is '.' or ' ' or '…'))
        {
            end--;
        }

        return plain[start..end].Trim();
    }

    private static string Plain(string text)
    {
        return (text ?? string.Empty).Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal).Trim();
    }

    /// <summary>
    /// Reads a table, image or chart written flat on its block into the object the block carries it in.
    /// </summary>
    /// <remarks>
    /// Asked to put a chart in a document, a model wrote <c>{"type": "chart", "conversation_chart": 1}</c> and
    /// <c>{"type": "chart", "labels": […], "series": […]}</c> — the chart's fields on the block, not inside
    /// its <c>chart</c> object. They were dropped as unknown properties, every chart arrived with no data,
    /// and the document had headings over empty space.
    /// </remarks>
    /// <param name="block">The block.</param>
    internal static void LiftFlatObject(PdfBlockDefinition block)
    {
        var unrecognized = block.Unrecognized;

        block.Unrecognized = null;

        if (block.Type == PdfBlockTypes.Chart && block.Chart is { } nested)
        {
            nested.Title ??= block.Title;
        }

        if (unrecognized is not { Count: > 0 })
        {
            return;
        }

        var node = new JsonObject();

        foreach (var (key, value) in unrecognized)
        {
            node[key] = JsonNode.Parse(value.GetRawText());
        }

        try
        {
            switch (block.Type)
            {
                case PdfBlockTypes.Chart when block.Chart is null:
                    block.Chart = PdfDefinitionJson.FromNode<PdfChartDefinition>(node);
                    block.Chart.Title ??= block.Title;

                    break;

                case PdfBlockTypes.Image when block.Image is null:
                    block.Image = PdfDefinitionJson.FromNode<PdfImageDefinition>(node);

                    break;

                case PdfBlockTypes.Table when block.Table is null:
                    block.Table = PdfDefinitionJson.FromNode<PdfTableDefinition>(node);

                    break;
            }
        }
        catch (JsonException)
        {
            // Properties that do not read as the block's object are left out, as unknown ones always were.
        }
    }

    /// <summary>
    /// Turns a paragraph that is a whole <c>[chart:…]</c> marker into the chart, and refuses any other text that
    /// carries one.
    /// </summary>
    /// <remarks>
    /// Text is printed as it is written, so a marker in it reached the reader as its characters. Asked to put
    /// the charts of the conversation in a report, gpt-4.1 wrote <c>[chart:Refer to chart marker above]</c> as a
    /// paragraph under each chart's heading — a placeholder for a chart it never named — and the file showed
    /// that line where the chart should be. A marker that holds a chart is drawn; anything else names no chart
    /// this host can find, so the call is refused with the ways to add one, rather than printing the marker.
    /// </remarks>
    /// <param name="blocks">The blocks of the call, changed in place.</param>
    /// <param name="services">The request services, used to list the charts of the conversation.</param>
    private static async Task ChartsFromMarkerTextAsync(List<PdfBlockDefinition> blocks, IServiceProvider services)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if (block is null)
            {
                continue;
            }

            var type = PdfLayoutBuilder.NormalizeType(block);
            var text = block.Text?.Trim();

            if (type is PdfBlockTypes.Paragraph or PdfBlockTypes.Markdown &&
                text is not null &&
                text.StartsWith(ChartMarkerPrefix, StringComparison.OrdinalIgnoreCase) &&
                text.EndsWith(']') &&
                PdfChartJsReader.FindMarkers(text).Count == 1 &&
                PdfChartJsReader.TryRead(text, out _))
            {
                block.Type = PdfBlockTypes.Chart;
                block.Chart = new PdfChartDefinition { ChartJs = text };
                block.Text = null;

                continue;
            }

            if (type == PdfBlockTypes.Code)
            {
                // Code is shown as written on purpose; a marker in it is an example, not a chart.
                continue;
            }

            var marked = new[] { block.Text, block.Title }
                .Concat(block.Items ?? [])
                .FirstOrDefault(value => value?.Contains(ChartMarkerPrefix, StringComparison.OrdinalIgnoreCase) == true);

            if (marked is null)
            {
                continue;
            }

            var listed = PdfConversationCharts.Describe(await PdfConversationCharts.GetAsync(services));

            throw new PdfToolException(
                $"Nothing was added: block {index + 1} ({type}) has a chart marker in its text — \"{Excerpt(marked)}\" — and text is printed as written, so the reader would see the marker instead of a chart. " +
                "Add the chart as its own block: {\"type\": \"chart\", \"conversation_chart\": N} for a chart shown earlier in the conversation, labels and series with the real values, source over uploaded tabular data, or chart_js with the whole [chart:{…}] marker. Never write a marker in text." +
                (listed is null ? string.Empty : Environment.NewLine + listed));
        }
    }

    /// <summary>
    /// Leaves out a heading that repeats the heading right before it.
    /// </summary>
    /// <remarks>
    /// gpt-4.1 put the document's title heading in <c>create_pdf</c> and then sent the content again starting
    /// with the same heading, and the report opened with its title twice. Two headings with the same words and
    /// nothing between them are never two sections, so the second is left out and the model is told. A heading
    /// used again further on, with content between, is kept.
    /// </remarks>
    /// <param name="blocks">The blocks of the call, changed in place.</param>
    /// <param name="precedingBlock">The block the new blocks follow in the document, when there is one.</param>
    /// <param name="notes">Receives a note for each heading left out.</param>
    internal static void RemoveRepeatedHeadings(List<PdfBlockDefinition> blocks, PdfBlockDefinition precedingBlock, List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(notes);

        var previous = precedingBlock;

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if (block is null)
            {
                continue;
            }

            if (IsHeading(block) && IsHeading(previous) && string.Equals(HeadingKey(block), HeadingKey(previous), StringComparison.Ordinal))
            {
                notes.Add($"Left out the heading \"{block.Text.Trim()}\": it is already the heading before it. Do not send content the document already has; append only what is new.");
                blocks.RemoveAt(index);
                index--;

                continue;
            }

            previous = block;
        }
    }

    private static bool IsHeading(PdfBlockDefinition block)
    {
        return block is not null &&
            PdfLayoutBuilder.NormalizeType(block) == PdfBlockTypes.Heading &&
            !string.IsNullOrWhiteSpace(block.Text);
    }

    private static string HeadingKey(PdfBlockDefinition block)
    {
        return string.Join(' ', block.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }

    private static string Excerpt(string text)
    {
        var trimmed = text.Trim();

        return trimmed.Length <= 80 ? trimmed : trimmed[..80] + "…";
    }

    /// <summary>
    /// Turns an image block that points at a chart — a <c>[chart:…]</c> marker, Chart.js JSON, or a chart of
    /// the conversation — into the chart block it was meant to be.
    /// </summary>
    /// <remarks>
    /// Asked to put "the actual chart images" in the document, a model reaches for an image block with the
    /// marker as its source. No picture can be read from that, and the block was left out.
    /// </remarks>
    /// <param name="block">The block.</param>
    internal static void ChartFromImageSource(PdfBlockDefinition block)
    {
        if (block.Type != PdfBlockTypes.Image || block.Image?.Source is not { } source)
        {
            return;
        }

        var trimmed = source.Trim();
        var chart = new PdfChartDefinition { Caption = block.Image.Caption };

        if (trimmed.StartsWith("[chart:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith('{'))
        {
            chart.ChartJs = trimmed;
        }
        else if (TryReadChartNumber(trimmed, out var number))
        {
            chart.ConversationChart = number;
        }
        else
        {
            return;
        }

        block.Type = PdfBlockTypes.Chart;
        block.Chart = chart;
        block.Image = null;
    }

    private static bool TryReadChartNumber(string source, out int number)
    {
        number = 0;

        // "chart:2", "chart 2", "conversation_chart:2".
        var lower = source.ToLowerInvariant();
        var prefix = lower.StartsWith("conversation_chart", StringComparison.Ordinal)
            ? "conversation_chart".Length
            : lower.StartsWith("chart", StringComparison.Ordinal) ? "chart".Length : -1;

        if (prefix < 0)
        {
            return false;
        }

        var rest = source[prefix..].TrimStart(':', ' ', '#', '_');

        return int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    private static bool HasValues(PdfChartDefinition chart)
    {
        return chart?.Series is { Count: > 0 } series && series.Any(candidate => candidate?.Values?.Any(value => value.HasValue) == true);
    }

    private static async Task ReadConversationChartAsync(PdfChartDefinition chart, int number, IServiceProvider services)
    {
        var charts = await PdfConversationCharts.GetAsync(services);

        if (number < 1 || number > charts.Count)
        {
            throw new PdfToolException(charts.Count == 0
                ? $"Nothing was added: conversation_chart {number} was asked for, but no chart was shown earlier in this conversation. Give the chart labels and series with the real values, or source over uploaded tabular data."
                : $"Nothing was added: there is no conversation chart {number}.{Environment.NewLine}{PdfConversationCharts.Describe(charts)}");
        }

        var read = charts[number - 1];

        chart.ChartType ??= read.ChartType;
        chart.Title ??= read.Title;

        if (chart.Labels is not { Count: > 0 })
        {
            chart.Labels = [.. read.Labels];
        }

        if (!HasValues(chart))
        {
            chart.Series = [.. read.Series.Select(series => new PdfChartSeriesDefinition
            {
                Name = series.Name,
                Color = series.Color,
                Values = [.. series.Values],
            })];
        }

        chart.SourceDescription ??= "Chart from the conversation";
        chart.ConversationChart = null;
    }

    private static void ReadChartJs(PdfChartDefinition chart, List<string> warnings)
    {
        if (!PdfChartJsReader.TryRead(chart.ChartJs, out var read))
        {
            warnings.Add("The chart_js value is not a Chart.js configuration with data; pass labels and series instead.");
            chart.ChartJs = null;

            return;
        }

        chart.ChartType ??= read.ChartType;
        chart.Title ??= read.Title;

        if (chart.Labels is not { Count: > 0 })
        {
            chart.Labels = read.Labels;
        }

        if (chart.Series is not { Count: > 0 })
        {
            chart.Series = read.Series;
        }

        chart.SourceDescription ??= "Chart from the conversation";
        chart.ChartJs = null;
    }

    private static async Task ReadTableAsync(
        PdfTableDefinition table,
        PdfTabularSourceDefinition source,
        TabularToolRunner.PreparationResult tabular,
        CancellationToken cancellationToken)
    {
        var (result, headers, formatting, total, description) = await QueryAsync(source, tabular, cancellationToken);

        if (table.Columns is not { Count: > 0 })
        {
            table.Columns = [];

            foreach (var header in headers)
            {
                table.Columns.Add(new PdfTableColumnDefinition { Header = header });
            }
        }

        // A column the model did not format takes the format the spreadsheet export would use, so the table
        // reads the same as the workbook and its preview.
        for (var index = 0; index < table.Columns.Count && index < headers.Count; index++)
        {
            var column = table.Columns[index];

            column.Header ??= headers[index];

            if (string.IsNullOrWhiteSpace(column.Format) && string.IsNullOrWhiteSpace(column.FormatCode) && formatting is not null)
            {
                var code = SpreadsheetNumberFormatCode.Resolve(formatting.FindColumn(headers[index]));

                if (!string.IsNullOrWhiteSpace(code) &&
                    !string.Equals(code, "General", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(code, "@", StringComparison.Ordinal))
                {
                    column.FormatCode = code;
                }
            }
        }

        table.Rows = [.. result.Rows.Select(row => row.Select(ToCellText).ToList())];
        table.SourceDescription = description;
        table.SourceRowCount = total > result.Rows.Count ? total : null;
    }

    private static async Task ReadChartAsync(
        PdfChartDefinition chart,
        PdfTabularSourceDefinition source,
        TabularToolRunner.PreparationResult tabular,
        CancellationToken cancellationToken)
    {
        var (result, headers, _, _, description) = await QueryAsync(source, tabular, cancellationToken);

        if (headers.Count == 0 || result.Rows.Count == 0)
        {
            throw new PdfToolException("The chart's data query returned no rows.");
        }

        var labelIndex = string.IsNullOrWhiteSpace(source.LabelColumn)
            ? 0
            : FindColumn(headers, source.LabelColumn);

        if (labelIndex < 0)
        {
            throw new PdfToolException($"The chart names the label column \"{source.LabelColumn}\", which the query does not return. It returns: {string.Join(", ", headers)}.");
        }

        var valueIndexes = new List<int>();

        if (source.ValueColumns is { Count: > 0 })
        {
            foreach (var name in source.ValueColumns)
            {
                var index = FindColumn(headers, name);

                if (index < 0)
                {
                    throw new PdfToolException($"The chart names the value column \"{name}\", which the query does not return. It returns: {string.Join(", ", headers)}.");
                }

                valueIndexes.Add(index);
            }
        }
        else
        {
            for (var index = 0; index < headers.Count; index++)
            {
                if (index != labelIndex && result.Rows.All(row => row[index] is null or DBNull or long or int or double or float or decimal))
                {
                    valueIndexes.Add(index);
                }
            }
        }

        if (valueIndexes.Count == 0)
        {
            throw new PdfToolException("The chart's query returns no numeric column to plot; name one in source.value_columns.");
        }

        chart.Labels = [.. result.Rows.Select(row => ToCellText(row[labelIndex]))];
        chart.Series = [];

        foreach (var index in valueIndexes)
        {
            chart.Series.Add(new PdfChartSeriesDefinition
            {
                Name = headers[index],
                Values = [.. result.Rows.Select(row => ToNumber(row[index]))],
            });
        }

        chart.SourceDescription = description;
    }

    private static async Task<(TabularQueryResult Result, List<string> Headers, SpreadsheetFormatting Formatting, long Total, string Description)> QueryAsync(
        PdfTabularSourceDefinition source,
        TabularToolRunner.PreparationResult tabular,
        CancellationToken cancellationToken)
    {
        var maxRows = Math.Clamp(source.MaxRows ?? DefaultMaxRows, 1, MaxRowsLimit);
        TabularTableInfo table = null;
        string sql;

        if (!string.IsNullOrWhiteSpace(source.Sql))
        {
            sql = source.Sql.Trim().TrimEnd(';');
        }
        else if (!string.IsNullOrWhiteSpace(source.TableName))
        {
            var name = source.TableName.Trim();

            table = tabular.Tables.FirstOrDefault(candidate => string.Equals(candidate.TableName, name, StringComparison.OrdinalIgnoreCase))
                ?? tabular.Tables.FirstOrDefault(candidate => string.Equals(candidate.WorksheetName, name, StringComparison.OrdinalIgnoreCase))
                ?? tabular.Tables.FirstOrDefault(candidate => string.Equals(candidate.SourceFileName, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new PdfToolException($"There is no loaded table named \"{name}\". Loaded tables: {string.Join(", ", tabular.Tables.Select(candidate => candidate.TableName))}.");

            sql = "SELECT * FROM " + TabularWorkspaceSqliteHelpers.QuoteIdentifier(table.TableName);
        }
        else
        {
            throw new PdfToolException("A tabular source needs 'sql' or 'table_name'.");
        }

        TabularQueryResult result;

        try
        {
            result = await tabular.Workspace.QueryAsync(sql, maxRows, cancellationToken);
        }
        catch (Exception ex) when (ex is TabularSqlException or SqliteException)
        {
            throw new PdfToolException($"The tabular query failed: {ex.Message}", ex);
        }

        var headers = new List<string>(result.Columns.Count);

        foreach (var column in result.Columns)
        {
            var info = table?.Columns.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase));

            // A full table is headed the way the uploaded file headed it, not by its SQL identifiers.
            headers.Add(string.IsNullOrWhiteSpace(info?.SourceName) ? column : info.SourceName);
        }

        var stored = table is null
            ? await tabular.Workspace.GetFormattingAsync(TabularToolNames.WorkspaceFormattingKey, cancellationToken)
            : await tabular.Workspace.GetFormattingAsync(table.TableName, cancellationToken);

        if (table is not null && string.IsNullOrEmpty(stored.SpecJson))
        {
            stored = await tabular.Workspace.GetFormattingAsync(TabularToolNames.WorkspaceFormattingKey, cancellationToken);
        }

        var formatting = TabularFormattingResolver.Resolve(stored.SpecJson, headers, table);
        long total = result.Rows.Count;

        if (result.Truncated)
        {
            try
            {
                var counted = await tabular.Workspace.QueryAsync($"SELECT COUNT(*) FROM ({sql})", 1, cancellationToken);

                if (counted.Rows.Count > 0 && counted.Rows[0].Length > 0 &&
                    long.TryParse(Convert.ToString(counted.Rows[0][0], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var counts))
                {
                    total = counts;
                }
            }
            catch (Exception ex) when (ex is TabularSqlException or SqliteException)
            {
                // The rows on hand are all that can be reported.
            }
        }

        var description = table is null
            ? "Query over the uploaded tabular data"
            : string.IsNullOrWhiteSpace(table.WorksheetName)
                ? $"Table {table.TableName} from {table.SourceFileName}"
                : $"Worksheet \"{table.WorksheetName}\" of {table.SourceFileName}";

        return (result, headers, formatting, total, description);
    }

    private static int FindColumn(List<string> headers, string name)
    {
        return headers.FindIndex(header => SpreadsheetFormatting.NameMatches(header, name));
    }

    private static string ToCellText(object value)
    {
        return value switch
        {
            null or DBNull => string.Empty,
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            DateTime date => date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static double? ToNumber(object value)
    {
        return value switch
        {
            null or DBNull => null,
            double number => number,
            float number => number,
            decimal number => (double)number,
            long number => number,
            int number => number,
            string text when PdfCellValues.TryParseNumber(text, percentAsFraction: false, out var parsed) => parsed,
            _ => null,
        };
    }
}
