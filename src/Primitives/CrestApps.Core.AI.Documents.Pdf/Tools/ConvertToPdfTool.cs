using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Conversion;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Ingestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Converts uploaded Word, PowerPoint, spreadsheet, Markdown, HTML, text and image files into a composed PDF
/// that the other authoring tools can go on editing.
/// </summary>
internal sealed class ConvertToPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ConvertToPdf;

    private const int MaxImages = 60;
    private const int DefaultMaxRows = 1000;

    private static readonly HashSet<string> _word = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".docm", ".dotx", ".dotm" };
    private static readonly HashSet<string> _slides = new(StringComparer.OrdinalIgnoreCase) { ".pptx", ".pptm", ".ppsx", ".potx" };
    private static readonly HashSet<string> _markup = new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".html", ".htm" };
    private static readonly HashSet<string> _plain = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".text", ".log", ".rtf" };
    private static readonly HashSet<string> _code = new(StringComparer.OrdinalIgnoreCase) { ".json", ".xml", ".yaml", ".yml", ".sql", ".cs", ".js", ".ts", ".py", ".java", ".css" };
    private static readonly HashSet<string> _tabular = new(StringComparer.OrdinalIgnoreCase) { ".csv", ".tsv", ".xlsx", ".xlsm", ".xls", ".ods" };
    private static readonly HashSet<string> _images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
    private static readonly HashSet<string> _legacy = new(StringComparer.OrdinalIgnoreCase) { ".doc", ".ppt" };

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            "sources": { "type": "array", "items": { "type": "string" }, "description": "The uploaded files to convert, by file name or id, in order; several become one PDF, each starting on a new page. Defaults to the only uploaded file that is not a PDF." },
            "name": { "type": "string", "description": "Name of the working PDF. Defaults to the first file's name." },
            "replace": { "type": "boolean", "description": "Replace a working PDF of the same name." },
            "include_notes": { "type": "boolean", "description": "PowerPoint: put each slide's speaker notes after it." },
            "sheets": { "type": "array", "items": { "type": "string" }, "description": "Spreadsheets: only these worksheets or tables." },
            "max_rows": { "type": "integer", "minimum": 1, "description": "Spreadsheets: the most rows per table (default 1000)." },
            {{PdfCompositionSchemas.Metadata}},
            {{PdfCompositionSchemas.PageSetup}},
            {{PdfCompositionSchemas.Theme}},
            {{PdfCompositionSchemas.RunningHeads}},
            {{PdfCompositionSchemas.FrontMatter}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConvertToPdfTool"/> class.
    /// </summary>
    public ConvertToPdfTool()
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
    public override string Description => "Converts uploaded files into a PDF being composed: Word (headings, paragraphs, lists, tables, pictures), PowerPoint (a page per slide with its title, text, tables and pictures, optionally the speaker notes), spreadsheets and CSV (each sheet as a table, with the number formats and colours the tabular preview shows), Markdown, HTML, text and images (a page each). Several files become one PDF. The result is the files' content in the PDF's theme — not a copy of their page design — and can be refined with add_pdf_content and format_pdf, then shown with preview_pdf and delivered with export_pdf. The uploads are never changed.";

    /// <summary>
    /// Converts the files.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var uploads = ResolveSources(arguments, context);
        var first = uploads[0];
        var name = PdfToolContext.SanitizeName(arguments.GetString("name") ?? Path.GetFileNameWithoutExtension(first.FileName) ?? "converted");
        var replace = arguments.GetBoolean("replace") == true;

        return await context.MutateAsync(async state =>
        {
            var existing = state.Find(name);

            if (existing is not null && !replace)
            {
                throw new PdfToolException($"A working PDF named \"{name}\" already exists. Pass another 'name', or replace: true to start it over.");
            }

            if (existing is null && state.Documents.Count >= context.Options.MaxWorkingDocuments)
            {
                throw new PdfToolException($"This conversation already holds {state.Documents.Count} working PDFs, the most it keeps. Reuse a name with replace: true.");
            }

            var images = new PdfImageStore(
                async (bytes, mediaType, fileName, description, token) =>
                    "asset:" + (await context.AddAssetAsync(state, bytes, mediaType, fileName, description, token)).Id,
                context.Options.MaxImageBytes,
                MaxImages);

            var blocks = new List<PdfBlockDefinition>();
            var warnings = new List<string>();
            var landscape = false;
            string title = null;
            var summary = new List<string>();

            foreach (var upload in uploads)
            {
                var bytes = await context.ReadUploadAsync(upload, cancellationToken)
                    ?? throw new PdfToolException($"The stored file for \"{upload.FileName}\" is missing. Ask the user to upload it again.");
                var converted = await ConvertAsync(arguments, upload, bytes, images, cancellationToken);

                if (blocks.Count > 0 && converted.Blocks.Count > 0)
                {
                    blocks.Add(new PdfBlockDefinition { Type = PdfBlockTypes.PageBreak });
                }

                blocks.AddRange(converted.Blocks);
                warnings.AddRange(converted.Warnings.Select(warning => uploads.Count > 1 ? $"{upload.FileName}: {warning}" : warning));
                landscape |= converted.Landscape;
                title ??= converted.Title;
                summary.Add($"\"{upload.FileName}\" ({Describe(converted.Blocks)})");
            }

            if (blocks.Count == 0)
            {
                throw new PdfToolException($"{string.Join(", ", uploads.Select(upload => $"\"{upload.FileName}\""))} had no content that could be converted.");
            }

            var definition = new PdfDocumentDefinition
            {
                Sections = [new PdfSectionDefinition { Id = "s1" }],
                NextSectionNumber = 2,
            };

            PdfFormattingArguments.Apply(definition, arguments, replace: true);

            definition.Title ??= string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(first.FileName) : title.Trim();

            // Slides and wide sheets read best across the page, unless the call chose otherwise.
            if (landscape && !arguments.TryGetElement("page_setup", out _))
            {
                definition.PageSetup ??= new PdfPageSetupDefinition();
                definition.PageSetup.Orientation = "landscape";
            }

            warnings.AddRange(await PdfBlockPreparer.PrepareAsync(definition, blocks, arguments.Services, cancellationToken));
            definition.Sections[0].Blocks.AddRange(blocks);

            var document = existing ?? new PdfWorkingDocument { Name = name };

            if (!string.IsNullOrEmpty(document.BlobPath))
            {
                await context.DeleteBlobAsync(document.BlobPath);
                document.BlobPath = null;
            }

            document.Kind = PdfWorkingDocument.ComposedKind;
            document.Definition = definition;
            document.SourceFileName = first.FileName;
            document.SourceDocumentId = first.ItemId;
            document.ByteLength = 0;
            document.Version++;
            document.UpdatedUtc = context.TimeProvider.GetUtcNow().UtcDateTime;
            document.History = ["Converted from " + string.Join(", ", uploads.Select(upload => upload.FileName)) + " with convert_to_pdf"];

            if (existing is null)
            {
                state.Documents.Add(document);
            }

            state.ActiveDocument = document.Name;

            var render = await context.RenderAsync(document, cancellationToken);

            document.PageCount = render.PageCount;

            var response = new StringBuilder();

            response.Append("Converted ").Append(string.Join(", ", summary)).Append(" into working PDF \"").Append(document.Name).Append("\". ")
                .AppendLine(PdfCompositionDescriber.DescribeRender(render));

            foreach (var warning in warnings.Distinct(StringComparer.Ordinal))
            {
                response.Append("- ").AppendLine(warning);
            }

            response.AppendLine().AppendLine(PdfCompositionDescriber.DescribeBlocks(definition));
            response.AppendLine().Append("The content is set in the PDF's theme, not copied from the original's page design. Preview it with preview_pdf, refine it with add_pdf_content or format_pdf, and deliver it with export_pdf. The uploaded files were not changed.");

            return response.ToString();
        }, cancellationToken);
    }

    private static List<AIDocument> ResolveSources(PdfToolArguments arguments, PdfToolContext context)
    {
        var names = arguments.GetStrings("sources", splitCommas: true);

        if (names.Count == 0 && arguments.GetString("source") is { } single)
        {
            names.Add(single);
        }

        if (names.Count == 0)
        {
            var candidates = context.Uploads.Where(upload => !PdfToolContext.IsPdf(upload.FileName)).ToList();

            return candidates.Count switch
            {
                1 => candidates,
                0 => throw new PdfToolException("There is no uploaded file to convert besides PDFs. Ask the user to upload the Word, PowerPoint, spreadsheet, text or image file."),
                _ => throw new PdfToolException($"Say which files to convert in 'sources': {string.Join(", ", candidates.Select(upload => $"\"{upload.FileName}\""))}."),
            };
        }

        var uploads = new List<AIDocument>();

        foreach (var name in names)
        {
            var upload = context.FindUpload(name, pdfOnly: false)
                ?? throw new PdfToolException($"There is no uploaded file \"{name}\". Uploaded files: {string.Join(", ", context.Uploads.Select(candidate => $"\"{candidate.FileName}\""))}.");

            if (PdfToolContext.IsPdf(upload.FileName))
            {
                throw new PdfToolException($"\"{upload.FileName}\" is already a PDF; edit it with the PDF tools, or combine it with others using edit_pdf_pages (merge).");
            }

            uploads.Add(upload);
        }

        return uploads;
    }

    private static async Task<PdfConversionResult> ConvertAsync(PdfToolArguments arguments, AIDocument upload, byte[] bytes, PdfImageStore images, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(upload.FileName ?? string.Empty);

        if (_legacy.Contains(extension))
        {
            throw new PdfToolException($"\"{upload.FileName}\" is in the old binary {extension} format, which cannot be read here. Ask the user to save it as {extension}x and upload it again.");
        }

        if (_word.Contains(extension))
        {
            return await PdfWordConverter.ConvertAsync(bytes, images, cancellationToken);
        }

        if (_slides.Contains(extension))
        {
            return await PdfSlideConverter.ConvertAsync(bytes, images, arguments.GetBoolean("include_notes") == true, cancellationToken);
        }

        if (_tabular.Contains(extension))
        {
            return await ConvertTabularAsync(arguments, upload, bytes, extension, cancellationToken);
        }

        if (_images.Contains(extension) || upload.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            var result = new PdfConversionResult();
            var source = await images.StoreAsync(bytes, upload.ContentType ?? "image/" + extension.TrimStart('.'), upload.FileName, null, cancellationToken);

            if (source is not null)
            {
                result.Blocks.Add(new PdfBlockDefinition
                {
                    Type = PdfBlockTypes.Image,
                    Image = new PdfImageDefinition { Source = source, WidthPercent = 100, AltText = Path.GetFileNameWithoutExtension(upload.FileName) },
                });
            }

            result.Warnings.AddRange(images.Warnings);

            return result;
        }

        if (_markup.Contains(extension))
        {
            return PdfTextConverter.ConvertMarkup(Decode(bytes));
        }

        if (_code.Contains(extension))
        {
            var code = new PdfConversionResult();

            code.Blocks.Add(new PdfBlockDefinition { Type = PdfBlockTypes.Code, Text = Decode(bytes) });

            return code;
        }

        if (_plain.Contains(extension) || upload.ContentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PdfTextConverter.ConvertPlainText(Decode(bytes));
        }

        return await ConvertWithReaderAsync(arguments.Services, upload, bytes, cancellationToken);
    }

    private static async Task<PdfConversionResult> ConvertTabularAsync(PdfToolArguments arguments, AIDocument upload, byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        var result = new PdfConversionResult();
        var blocks = new PdfConversionBlocks(result.Blocks);
        var maxRows = Math.Clamp(arguments.GetInt("max_rows") ?? DefaultMaxRows, 1, 20_000);
        var sheets = new HashSet<string>(arguments.GetStrings("sheets", splitCommas: true), StringComparer.OrdinalIgnoreCase);

        // The tabular workspace reads the sheet the way the tabular preview shows it: number formats, colours
        // and totals included. The table blocks name its tables and are filled when they are prepared.
        var prepared = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        try
        {
            var tables = prepared.Error is null
                ? prepared.Tables.Where(table => string.Equals(table.SourceDocumentId, upload.ItemId, StringComparison.Ordinal) &&
                    (sheets.Count == 0 || sheets.Contains(table.WorksheetName ?? string.Empty) || sheets.Contains(table.TableName))).ToList()
                : [];

            if (tables.Count > 0)
            {
                foreach (var table in tables)
                {
                    blocks.PageBreak();
                    blocks.Heading(table.WorksheetName ?? table.TableName, 1);
                    blocks.Add(new PdfBlockDefinition
                    {
                        Type = PdfBlockTypes.Table,
                        Table = new PdfTableDefinition
                        {
                            RepeatHeader = true,
                            Source = new PdfTabularSourceDefinition { TableName = table.TableName, MaxRows = maxRows },
                        },
                    });

                    result.Landscape |= table.Columns?.Count > 7;
                }

                return result;
            }
        }
        finally
        {
            prepared.Workspace?.Dispose();
        }

        // Without a workspace the sheets are read directly: the values, without their formatting.
        TabularDocumentArtifact artifact = null;

        if (arguments.Services.GetService<TabularDocumentArtifactFactory>() is { } factory)
        {
            artifact = await factory.CreateAsync(upload, cancellationToken);
        }

        if (artifact is null && extension is ".csv" or ".tsv")
        {
            artifact = TabularDocumentArtifact.FromDelimitedContent(Decode(bytes), upload.FileName);
        }

        if (artifact is null)
        {
            throw new PdfToolException($"\"{upload.FileName}\" could not be read as a spreadsheet on this server.");
        }

        foreach (var sheet in artifact.GetWorksheets().Where(sheet => sheets.Count == 0 || sheets.Contains(sheet.Name ?? string.Empty)))
        {
            blocks.PageBreak();

            if (!string.IsNullOrWhiteSpace(sheet.Name))
            {
                blocks.Heading(sheet.Name, 1);
            }

            var rows = new List<List<string>> { sheet.Header };

            rows.AddRange(sheet.Rows.Take(maxRows));
            blocks.Table(rows);

            if (sheet.Rows.Count > maxRows)
            {
                result.Warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{sheet.Name ?? upload.FileName}: only the first {maxRows} of {sheet.Rows.Count} rows were converted; pass max_rows to change that."));
            }

            result.Landscape |= sheet.Header.Count > 7;
        }

        result.Warnings.Add("The sheets were read without their number formats and colours, because the tabular workspace is not available here.");

        return result;
    }

    private static async Task<PdfConversionResult> ConvertWithReaderAsync(IServiceProvider services, AIDocument upload, byte[] bytes, CancellationToken cancellationToken)
    {
        var resolver = services.GetService<IIngestionDocumentReaderResolver>();

        using var stream = new MemoryStream(bytes, writable: false);

        var reader = resolver?.Resolve(upload.FileName, upload.ContentType, stream)
            ?? throw new PdfToolException($"\"{upload.FileName}\" is not a file type that can be converted to PDF. Word, PowerPoint, spreadsheets, CSV, Markdown, HTML, text and images can.");

        stream.Position = 0;

        var read = await reader.ReadAsync(stream, upload.FileName, upload.ContentType ?? "application/octet-stream", cancellationToken);
        var result = new PdfConversionResult();
        var blocks = new PdfConversionBlocks(result.Blocks);

        foreach (var element in read.EnumerateContent())
        {
            if (element is not IngestionDocumentParagraph and not IngestionDocumentHeader || string.IsNullOrWhiteSpace(element.Text))
            {
                continue;
            }

            if (element.Metadata.TryGetValue(ElementMetadataKeys.HeadingLevel, out var level) &&
                int.TryParse(Convert.ToString(level, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var heading))
            {
                blocks.Heading(PdfInlineMarkdown.Escape(element.Text), heading);
            }
            else
            {
                blocks.Paragraph(PdfInlineMarkdown.Escape(element.Text));
            }
        }

        result.Warnings.Add("Only the text of this file could be converted; its tables and pictures, if any, were not.");

        return result;
    }

    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }

    private static string Describe(List<PdfBlockDefinition> blocks)
    {
        var counts = blocks
            .Where(block => block.Type != PdfBlockTypes.PageBreak)
            .GroupBy(block => block.Type)
            .Select(group => $"{group.Count()} {group.Key.Replace('_', ' ')}{(group.Count() == 1 ? string.Empty : "s")}");

        return string.Join(", ", counts);
    }
}
