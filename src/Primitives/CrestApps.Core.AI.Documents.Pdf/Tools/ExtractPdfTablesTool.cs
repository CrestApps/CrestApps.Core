using System.Text;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Detects the tables in a PDF — ruled or laid out with whitespace — and returns their cells, joining a
/// table that runs on from one page to the next, and offers them as a spreadsheet or CSV download.
/// </summary>
/// <remarks>
/// Tables are found by the same reader uploads are indexed with, so what this tool reports is what search
/// sees. It also answers "are there tables in this PDF, and where": every table is listed with its pages
/// and size.
/// </remarks>
internal sealed class ExtractPdfTablesTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ExtractPdfTables;

    private const int RowsShownPerTable = 60;
    private const int RowsShownForOneTable = 1_000;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "format": {
              "type": "string",
              "enum": ["markdown", "csv", "json"],
              "description": "How the tables are returned: 'markdown' (default), 'csv' or 'json'."
            },
            "merge_across_pages": {
              "type": "boolean",
              "description": "Whether a table continued on the next page (same number of columns) is joined to its first part, dropping the repeated header row. Defaults to true."
            },
            "table": {
              "type": "integer",
              "description": "Optional one-based number of a single table to return (as the list numbers them), with all of its rows."
            },
            "export": {
              "type": "string",
              "enum": ["xlsx", "csv"],
              "description": "Optional download: 'xlsx' writes one worksheet per table; 'csv' writes the chosen table, or every table one after another separated by a blank line."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractPdfTablesTool"/> class.
    /// </summary>
    public ExtractPdfTablesTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Detects the tables in a PDF (ruled or aligned with whitespace) and returns how many there are, the page(s) and size of each, and their cells as Markdown, CSV or JSON. Tables that continue across pages are joined. Pass 'table' for one table in full, or 'export' ('xlsx' or 'csv') for a download; the download marker must be included in your answer exactly as given. Tables that are pictures need ocr_pdf.";

    /// <summary>
    /// Extracts the tables.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tables.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var format = (arguments.GetString("format") ?? "markdown").Trim().ToLowerInvariant();

        if (format is not ("markdown" or "csv" or "json"))
        {
            throw new PdfToolException($"\"{format}\" is not a table format. Use 'markdown', 'csv' or 'json'.");
        }

        var export = arguments.GetString("export")?.Trim().TrimStart('.').ToLowerInvariant();

        if (export is not (null or "xlsx" or "csv"))
        {
            throw new PdfToolException($"Tables can be exported as 'xlsx' or 'csv', not \"{export}\".");
        }

        var merge = arguments.GetBoolean("merge_across_pages") ?? true;
        var password = arguments.GetString("password");

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, password);
        var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages);

        var readable = PdfReadableCopy.WithoutPassword(bytes, password, pdf.IsEncrypted);
        var content = await PdfIngestedContent.ReadAsync(arguments.Services, readable, source.Name, pages, pdf.NumberOfPages, cancellationToken);
        var tables = PdfExtractedTable.Build(content.AllTables, merge);
        var searched = PdfPageSelection.Describe(pages, pdf.NumberOfPages);

        if (tables.Count == 0)
        {
            var message = $"No tables were detected in \"{source.Name}\" ({searched}). A table that is a picture needs ocr_pdf; text laid out loosely in columns can be read with extract_pdf_text (format 'lines', include_positions).";

            return content.Warning is null
                ? message
                : message + " " + content.Warning;
        }

        var selected = Enumerable.Range(1, tables.Count).ToList();
        var chosen = arguments.GetInt("table");

        if (chosen.HasValue)
        {
            if (chosen.Value < 1 || chosen.Value > tables.Count)
            {
                throw new PdfToolException($"There is no table {chosen.Value}; {tables.Count} table(s) were found on {searched}.");
            }

            selected = [chosen.Value];
        }

        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);

        writer.Line($"Found {tables.Count} table(s) in \"{source.Name}\" ({searched}). The first row of each is taken as its header.");

        if (merge && tables.Exists(table => table.Pages.Count > 1))
        {
            writer.Line("Tables that continue on the next page were joined, without their repeated header row.");
        }

        if (export is not null)
        {
            var marker = await ExportAsync(arguments, context, source, tables, selected, export, cancellationToken);

            writer.Line(PdfDownloads.DescribeMarker(marker));
        }

        var rowsShown = chosen.HasValue
            ? RowsShownForOneTable
            : RowsShownPerTable;
        var notShown = new List<int>();
        var shortened = new List<int>();

        foreach (var number in selected)
        {
            var table = tables[number - 1];
            var heading = FormattableString.Invariant($"Table {number} — {table.DescribePages()}, {table.Rows.Count} rows × {table.ColumnCount} columns");
            var body = Render(table, number, format, rowsShown);

            if (!writer.Fits(heading.Length + body.Length + 4))
            {
                // A table too long for what is left is shown in part, as many rows as still fit.
                var perRow = Math.Max(1, body.Length / Math.Max(1, Math.Min(table.Rows.Count, rowsShown + 1)));
                var fitting = ((writer.Remaining - heading.Length - 200) / perRow) - 1;

                while (fitting >= 3)
                {
                    body = Render(table, number, format, fitting);

                    if (writer.Fits(heading.Length + body.Length + 4))
                    {
                        break;
                    }

                    fitting /= 2;
                }

                if (fitting < 3)
                {
                    notShown.Add(number);
                    writer.TryLine(string.Empty);
                    writer.TryLine(heading + " (cells not shown, to stay within the answer size)");

                    continue;
                }

                shortened.Add(number);
            }

            writer.TryLine(string.Empty);
            writer.TryLine(heading);
            writer.TryLine(body);
        }

        if (notShown.Count > 0 || shortened.Count > 0)
        {
            var affected = PdfPageRange.Describe(notShown.Concat(shortened));

            writer.Line();
            writer.Line($"[Not every row of table(s) {affected} is shown, to stay within the answer size. Ask for one with 'table', or get every row with export: 'xlsx' or 'csv'.]");
        }

        if (content.Warning is not null)
        {
            writer.Line(content.Warning);
        }

        return writer.ToString();
    }

    private static string Render(PdfExtractedTable table, int number, string format, int maxRows)
    {
        switch (format)
        {
            case "csv":
                {
                    var builder = new StringBuilder();
                    var shown = Math.Min(table.Rows.Count, maxRows + 1);

                    for (var row = 0; row < shown; row++)
                    {
                        builder.Append(string.Join(',', table.Rows[row].Select(EscapeCsv))).Append('\n');
                    }

                    if (shown < table.Rows.Count)
                    {
                        builder.Append(FormattableString.Invariant($"({table.Rows.Count - shown} more rows not shown)\n"));
                    }

                    return builder.ToString().TrimEnd('\n');
                }

            case "json":
                return PdfReadingJson.Serialize(new
                {
                    table = number,
                    pages = table.Pages,
                    rows = table.Rows.Count,
                    columns = table.ColumnCount,
                    header = table.Header,
                    data = table.Rows.Skip(1).Take(maxRows).ToList(),
                    truncated = table.Rows.Count - 1 > maxRows ? true : (bool?)null,
                });

            default:
                {
                    var builder = new StringBuilder();

                    PdfStructureWriter.AppendMarkdownTable(builder, table.Rows, maxRows);

                    return builder.ToString();
                }
        }
    }

    private static async Task<string> ExportAsync(
        PdfToolArguments arguments,
        PdfToolContext context,
        PdfSource source,
        List<PdfExtractedTable> tables,
        List<int> selected,
        string export,
        CancellationToken cancellationToken)
    {
        var extension = "." + export;
        var resolver = arguments.Services?.GetService<IGeneratedFileWriterResolver>();

        if (resolver is null || !resolver.TryResolve(extension, out var fileWriter))
        {
            var available = resolver?.SupportedExtensions is { Count: > 0 } extensions
                ? string.Join(", ", extensions.Order(StringComparer.OrdinalIgnoreCase))
                : "none";

            throw new PdfToolException($"This host cannot write {extension} files (formats it can write: {available}). Ask again without 'export' to read the tables here.");
        }

        var baseName = PdfDownloads.BaseName(source);
        using var stream = new MemoryStream();

        if (export == "xlsx")
        {
            var file = new GeneratedFileContent { Title = baseName + " tables" };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var number in selected)
            {
                var table = tables[number - 1];

                file.Sheets.Add(new GeneratedSheet
                {
                    Name = SheetName(number, table, names),
                    Header = table.Header,
                    Rows = table.Rows.Skip(1).ToList(),
                });
            }

            await fileWriter.WriteAsync(file, stream, cancellationToken);
        }
        else
        {
            var first = true;

            foreach (var number in selected)
            {
                var table = tables[number - 1];

                if (!first)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(Environment.NewLine), cancellationToken);
                }

                first = false;

                await fileWriter.WriteAsync(
                    new GeneratedFileContent
                    {
                        Header = table.Header,
                        Rows = table.Rows.Skip(1).ToList(),
                    },
                    stream,
                    cancellationToken);
            }
        }

        var fileName = selected.Count == 1 && tables.Count > 1
            ? FormattableString.Invariant($"{baseName}-table-{selected[0]}{extension}")
            : baseName + "-tables" + extension;

        return await context.ExportAsync(fileName, stream.ToArray(), PdfDownloads.ContentTypeFor(extension), cancellationToken);
    }

    private static string SheetName(int number, PdfExtractedTable table, HashSet<string> used)
    {
        var name = FormattableString.Invariant($"Table {number} (p{PdfPageRange.Describe(table.Pages).Replace(", ", "+", StringComparison.Ordinal)})");

        // A worksheet name holds at most 31 characters and none of : \ / ? * [ ].
        name = new string([.. name.Select(character => character is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? '-' : character)]);

        if (name.Length > 31)
        {
            name = FormattableString.Invariant($"Table {number}");
        }

        while (!used.Add(name))
        {
            name = FormattableString.Invariant($"Table {number}-{used.Count}");
        }

        return name;
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
