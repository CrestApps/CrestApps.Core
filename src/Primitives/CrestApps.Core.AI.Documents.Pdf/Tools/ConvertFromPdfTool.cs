using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Converts a PDF, or some of its pages, into another format and offers the result as a download: Word,
/// Excel, Markdown, plain text, HTML, JSON, CSV, or SVG pictures of the pages.
/// </summary>
/// <remarks>
/// Word, Markdown, HTML and JSON are built from the document's structure — headings, paragraphs, lists,
/// tables and figures in reading order — so the result can be edited rather than merely read. Excel and
/// CSV carry the tables. SVG draws the pages themselves.
/// </remarks>
internal sealed class ConvertFromPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ConvertFromPdf;

    private const int MaxPages = 200;
    private const int MaxSvgPages = 30;
    private const long MaxEmbeddedImageBytes = 5L * 1024 * 1024;

    private static readonly Dictionary<string, string> _formatAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["docx"] = "docx",
        ["word"] = "docx",
        ["doc"] = "docx",
        ["xlsx"] = "xlsx",
        ["excel"] = "xlsx",
        ["xls"] = "xlsx",
        ["md"] = "md",
        ["markdown"] = "md",
        ["txt"] = "txt",
        ["text"] = "txt",
        ["html"] = "html",
        ["htm"] = "html",
        ["json"] = "json",
        ["csv"] = "csv",
        ["svg"] = "svg",
        ["image"] = "svg",
        ["images"] = "svg",
    };

    private static readonly Dictionary<string, string> _formatNames = new(StringComparer.Ordinal)
    {
        ["docx"] = "Word",
        ["xlsx"] = "Excel",
        ["md"] = "Markdown",
        ["txt"] = "plain text",
        ["html"] = "HTML",
        ["json"] = "JSON",
        ["csv"] = "CSV",
        ["svg"] = "SVG page pictures",
    };

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "format": {
              "type": "string",
              "enum": ["docx", "xlsx", "md", "txt", "html", "json", "csv", "svg"],
              "description": "The format to convert to: 'docx' (Word, editable headings, paragraphs, lists and tables), 'xlsx' (one worksheet per table), 'md' (Markdown), 'txt' (plain text), 'html' (a self-contained web page), 'json' (the document's structure), 'csv' (the tables) or 'svg' (a picture of each page; several pages come as a zip)."
            },
            "file_name": {
              "type": "string",
              "description": "Optional file name for the download. Its extension is set to match the format."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConvertFromPdfTool"/> class.
    /// </summary>
    public ConvertFromPdfTool()
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
    public override string Description => "Converts a PDF (or selected pages) to Word (docx), Excel (xlsx, one sheet per table), Markdown, plain text, HTML, JSON (structure), CSV (tables) or SVG pictures of the pages, and returns a [doc:N] download marker that must be included in your answer exactly as given. Word, Markdown and HTML keep headings, lists and tables; figures become placeholders except in HTML, which embeds them.";

    /// <summary>
    /// Converts the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was converted, and the download marker.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var requestedName = arguments.GetString("file_name");
        var format = ResolveFormat(arguments.GetString("format"), requestedName);
        var password = arguments.GetString("password");

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, password);
        var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages);
        var limit = format == "svg"
            ? MaxSvgPages
            : MaxPages;
        var notes = new List<string>();

        if (pages.Count > limit)
        {
            notes.Add(FormattableString.Invariant($"Only the first {limit} of the {pages.Count} pages asked for were converted; convert the rest with 'pages' in further calls."));
            pages = pages.Take(limit).ToList();
        }

        var readable = PdfReadableCopy.WithoutPassword(bytes, password, pdf.IsEncrypted);
        var baseName = PdfDownloads.BaseName(source);
        // Several pages drawn as pictures arrive as one archive.
        var extension = format == "svg" && pages.Count > 1
            ? ".zip"
            : "." + format;
        var fileName = PdfDownloads.FileName(requestedName, baseName, extension);

        var (file, contentType, summary) = format switch
        {
            "docx" or "md" or "html" or "json" => await ConvertStructureAsync(arguments, pdf, readable, source, pages, format, notes, cancellationToken),
            "xlsx" or "csv" => await ConvertTablesAsync(arguments, pdf, readable, source, pages, format, cancellationToken),
            "svg" => ConvertToSvg(arguments, readable, pages, baseName, notes),
            _ => ConvertToText(pdf, pages, notes, cancellationToken),
        };

        var marker = await context.ExportAsync(fileName, file, contentType, cancellationToken);
        var scope = PdfPageSelection.Describe(pages, pdf.NumberOfPages);
        var answer = new StringBuilder();

        answer.Append($"Converted \"{source.Name}\" ({scope}) to {_formatNames[format]} as \"{fileName}\": {summary}.");

        foreach (var note in notes)
        {
            answer.Append(' ').Append(note);
        }

        answer.Append('\n').Append(PdfDownloads.DescribeMarker(marker));

        return answer.ToString();
    }

    private static string ResolveFormat(string format, string fileName)
    {
        var requested = format?.Trim().TrimStart('.');

        if (string.IsNullOrEmpty(requested) && !string.IsNullOrWhiteSpace(fileName))
        {
            requested = Path.GetExtension(fileName.Trim()).TrimStart('.');
        }

        if (string.IsNullOrEmpty(requested))
        {
            throw new PdfToolException("Pass 'format': docx, xlsx, md, txt, html, json, csv or svg.");
        }

        return _formatAliases.TryGetValue(requested, out var resolved)
            ? resolved
            : throw new PdfToolException($"A PDF cannot be converted to \"{requested}\" here. Choose docx, xlsx, md, txt, html, json, csv or svg. To make a PDF from another file, use convert_to_pdf.");
    }

    private static IGeneratedFileWriter ResolveWriter(PdfToolArguments arguments, string extension)
    {
        var resolver = arguments.Services?.GetService<IGeneratedFileWriterResolver>();

        if (resolver is not null && resolver.TryResolve(extension, out var writer))
        {
            return writer;
        }

        var available = new List<string> { "md", "txt", "html", "json", "svg" };

        foreach (var candidate in new[] { "docx", "xlsx", "csv" })
        {
            if (resolver?.IsSupported("." + candidate) == true)
            {
                available.Add(candidate);
            }
        }

        throw new PdfToolException($"This host cannot write {extension} files. Formats available here: {string.Join(", ", available)}.");
    }

    private static async Task<(byte[] File, string ContentType, string Summary)> ConvertStructureAsync(
        PdfToolArguments arguments,
        PdfDocument pdf,
        byte[] readable,
        PdfSource source,
        List<int> pages,
        string format,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        // Resolved first, so a host that cannot write the format says so before the document is read.
        var writer = format == "docx"
            ? ResolveWriter(arguments, ".docx")
            : null;

        var content = await PdfIngestedContent.ReadAsync(arguments.Services, readable, source.Name, pages, pdf.NumberOfPages, cancellationToken);
        var layout = PdfLayoutAnalyzer.Analyze(pdf, pages, content, encodeImages: format == "html", cancellationToken);
        var elements = PdfStructureBuilder.Build(layout);

        if (content.Warning is not null)
        {
            notes.Add(content.Warning);
        }

        if (elements.Count == 0)
        {
            throw new PdfToolException("No text was found on these pages, so there is nothing to convert. They may be scanned images; ocr_pdf can read them first.");
        }

        var title = string.IsNullOrWhiteSpace(pdf.Information?.Title)
            ? PdfDownloads.BaseName(source)
            : pdf.Information.Title.Trim();
        var summary = Summarize(elements);
        byte[] file;

        switch (format)
        {
            case "docx":
                {
                    using var stream = new MemoryStream();

                    await writer.WriteAsync(
                        new GeneratedFileContent
                        {
                            Text = PdfStructureWriter.ToMarkdown(elements, pageMarkers: false, maxParagraphCharacters: 0, maxTableRows: 0),
                        },
                        stream,
                        cancellationToken);

                    file = stream.ToArray();

                    if (elements.Exists(element => element.Type == PdfStructureElement.FigureType))
                    {
                        notes.Add("Figures are marked with a placeholder in the Word file; HTML embeds them.");
                    }

                    break;
                }

            case "md":
                file = Encoding.UTF8.GetBytes(PdfStructureWriter.ToMarkdown(elements, pageMarkers: true, maxParagraphCharacters: 0, maxTableRows: 0));

                break;

            case "html":
                file = Encoding.UTF8.GetBytes(PdfStructureWriter.ToHtml(elements, title, PdfLanguageDetector.ReadDeclaredLanguage(pdf), MaxEmbeddedImageBytes));

                break;

            default:
                {
                    var model = new
                    {
                        file = source.Name,
                        title,
                        author = string.IsNullOrWhiteSpace(pdf.Information?.Author) ? null : pdf.Information.Author,
                        language = PdfLanguageDetector.ReadDeclaredLanguage(pdf),
                        page_count = pdf.NumberOfPages,
                        pages,
                        body_font_size = layout.BodyFontSize > 0 ? layout.BodyFontSize : (double?)null,
                        elements = PdfStructureWriter.ToJsonModel(elements, maxParagraphCharacters: 0, includeTableRows: true),
                    };

                    file = JsonSerializer.SerializeToUtf8Bytes(model, PdfReadingJson.IndentedOptions);

                    break;
                }
        }

        return (file, PdfDownloads.ContentTypeFor("." + format), summary);
    }

    private static async Task<(byte[] File, string ContentType, string Summary)> ConvertTablesAsync(
        PdfToolArguments arguments,
        PdfDocument pdf,
        byte[] readable,
        PdfSource source,
        List<int> pages,
        string format,
        CancellationToken cancellationToken)
    {
        var writer = ResolveWriter(arguments, "." + format);
        var content = await PdfIngestedContent.ReadAsync(arguments.Services, readable, source.Name, pages, pdf.NumberOfPages, cancellationToken);
        var tables = PdfExtractedTable.Build(content.AllTables, mergeAcrossPages: true);

        if (tables.Count == 0)
        {
            var where = pages.Count == pdf.NumberOfPages
                ? "any page"
                : "pages " + PdfPageRange.Describe(pages);
            var holder = format == "xlsx"
                ? "a spreadsheet"
                : "a CSV file";

            throw new PdfToolException($"No tables were detected on {where}, and {holder} holds tables. Convert to docx, md or txt for the text instead. {content.Warning}".TrimEnd());
        }

        using var stream = new MemoryStream();

        if (format == "xlsx")
        {
            var file = new GeneratedFileContent { Title = PdfDownloads.BaseName(source) };

            for (var index = 0; index < tables.Count; index++)
            {
                var table = tables[index];

                file.Sheets.Add(new GeneratedSheet
                {
                    Name = FormattableString.Invariant($"Table {index + 1}"),
                    Header = table.Header,
                    Rows = table.Rows.Skip(1).ToList(),
                });
            }

            await writer.WriteAsync(file, stream, cancellationToken);
        }
        else
        {
            for (var index = 0; index < tables.Count; index++)
            {
                if (index > 0)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(Environment.NewLine), cancellationToken);
                }

                await writer.WriteAsync(
                    new GeneratedFileContent
                    {
                        Header = tables[index].Header,
                        Rows = tables[index].Rows.Skip(1).ToList(),
                    },
                    stream,
                    cancellationToken);
            }
        }

        var summary = FormattableString.Invariant($"{tables.Count} table(s)");

        if (format == "xlsx")
        {
            summary += ", one worksheet each";
        }
        else if (tables.Count > 1)
        {
            summary += ", one after another separated by a blank line";
        }

        return (stream.ToArray(), PdfDownloads.ContentTypeFor("." + format), summary);
    }

    private static (byte[] File, string ContentType, string Summary) ConvertToSvg(
        PdfToolArguments arguments,
        byte[] readable,
        List<int> pages,
        string baseName,
        List<string> notes)
    {
        var options = arguments.Services?.GetService<IOptions<PdfPreviewOptions>>()?.Value ?? new PdfPreviewOptions();
        var renderings = PdfPageSvgRenderer.Render(readable, pages, options);

        if (renderings.Count == 0)
        {
            throw new PdfToolException("None of the pages could be drawn.");
        }

        var skipped = renderings.Sum(rendering => rendering.ImagesSkipped);

        if (skipped > 0)
        {
            notes.Add(FormattableString.Invariant($"{skipped} picture(s) were too large or not decodable and are drawn as placeholders."));
        }

        if (renderings.Exists(rendering => rendering.Simplified))
        {
            notes.Add("Dense vector artwork was simplified on some pages.");
        }

        if (renderings.Count == 1)
        {
            return (Encoding.UTF8.GetBytes(renderings[0].Svg), PdfDownloads.ContentTypeFor(".svg"), "1 page drawn as SVG");
        }

        var zip = PdfDownloads.Zip(renderings.Select(rendering => (FormattableString.Invariant($"{baseName}-page-{rendering.PageNumber}.svg"), Encoding.UTF8.GetBytes(rendering.Svg))));

        return (zip, PdfDownloads.ZipContentType, FormattableString.Invariant($"{renderings.Count} pages drawn as SVG, one file per page in a zip"));
    }

    private static (byte[] File, string ContentType, string Summary) ConvertToText(PdfDocument pdf, List<int> pages, List<string> notes, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var empty = new List<int>();

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = PdfPageText.GetText(pdf.GetPage(number));

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(FormattableString.Invariant($"--- Page {number} ---\n\n"));

            if (string.IsNullOrWhiteSpace(text))
            {
                empty.Add(number);
                builder.Append("(no extractable text)");
            }
            else
            {
                builder.Append(text);
            }
        }

        if (empty.Count > 0)
        {
            notes.Add($"Pages {PdfPageRange.Describe(empty)} have no text layer (they may be scanned; ocr_pdf can read them).");
        }

        return (Encoding.UTF8.GetBytes(builder.Append('\n').ToString()), PdfDownloads.ContentTypeFor(".txt"), FormattableString.Invariant($"{pages.Count - empty.Count} page(s) of text"));
    }

    private static string Summarize(List<PdfStructureElement> elements)
    {
        var headings = elements.Count(element => element.Type == PdfStructureElement.HeadingType);
        var paragraphs = elements.Count(element => element.Type == PdfStructureElement.ParagraphType);
        var items = elements.Count(element => element.Type == PdfStructureElement.ListItemType);
        var tables = elements.Count(element => element.Type == PdfStructureElement.TableType);
        var figures = elements.Count(element => element.Type == PdfStructureElement.FigureType);

        return FormattableString.Invariant($"{headings} heading(s), {paragraphs} paragraph(s), {items} list item(s), {tables} table(s), {figures} figure(s)");
    }
}
