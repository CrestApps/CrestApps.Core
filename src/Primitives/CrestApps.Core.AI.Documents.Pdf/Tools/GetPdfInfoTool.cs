using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Lists the PDFs a conversation holds, or describes one of them.
/// </summary>
/// <remarks>
/// The first call the agent makes. The model is handed the user's request, not the conversation, so without
/// this it would guess at names; with it, it sees every upload and working copy and what each one is.
/// </remarks>
internal sealed class GetPdfInfoTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.GetPdfInfo;

    private const int MaxListedUploads = 25;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            "pdf": { "type": "string", "description": "Optional. Omit to list every PDF in the conversation (uploads and working PDFs) and the other files that can be used as images or converted. Pass a name to describe that PDF in detail." },
            {{PdfToolSchemas.Password}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPdfInfoTool"/> class.
    /// </summary>
    public GetPdfInfoTool()
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
    public override string Description => "Call first. Without 'pdf', lists every uploaded PDF and working PDF in the conversation (with pages, size and what was done to each) plus other files usable as images or conversion sources. With 'pdf', describes it: pages and page sizes, metadata, language, tagging, encryption and permissions, bookmarks, form fields, annotations, links, images, attachments, layers, signatures, and pages without text (possibly scanned); for a composed document, its blocks and formatting.";

    /// <summary>
    /// Describes the PDFs.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(arguments.Pdf()))
        {
            return await ListAsync(context, cancellationToken);
        }

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        return Describe(source, bytes, arguments.GetString("password"));
    }

    private static async Task<string> ListAsync(PdfToolContext context, CancellationToken cancellationToken)
    {
        var state = await context.GetStateAsync(cancellationToken);
        var builder = new StringBuilder();

        builder.AppendLine("Working PDFs (yours to change; uploads are never changed):");

        if (state.Documents.Count == 0)
        {
            builder.AppendLine("  (none yet)");
        }

        foreach (var document in state.Documents)
        {
            builder.Append("  \"").Append(document.Name).Append('"');

            if (string.Equals(document.Name, state.ActiveDocument, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" [active]");
            }

            builder.Append(" — ").Append(document.IsComposed ? "composed document" : "file")
                .Append(", ").Append(document.PageCount.ToString(CultureInfo.InvariantCulture)).Append(" page(s)");

            if (!document.IsComposed && document.ByteLength > 0)
            {
                builder.Append(", ").Append(FormatSize(document.ByteLength));
            }

            if (!string.IsNullOrWhiteSpace(document.SourceFileName))
            {
                builder.Append(", from \"").Append(document.SourceFileName).Append('"');
            }

            builder.AppendLine();

            foreach (var change in document.History.TakeLast(3))
            {
                builder.Append("      · ").AppendLine(change);
            }
        }

        var pdfs = context.PdfUploads.ToList();

        builder.AppendLine().AppendLine("Uploaded PDFs:");

        if (pdfs.Count == 0)
        {
            builder.AppendLine("  (none)");
        }

        foreach (var upload in pdfs.Take(MaxListedUploads))
        {
            builder.Append("  \"").Append(upload.FileName).Append("\" (id ").Append(upload.ItemId).Append(", ").Append(FormatSize(upload.FileSize));

            try
            {
                var bytes = await context.ReadUploadAsync(upload, cancellationToken);

                if (bytes is not null)
                {
                    builder.Append(", ").Append(PdfFiles.CountPages(bytes).ToString(CultureInfo.InvariantCulture)).Append(" page(s)");
                }
            }
            catch (PdfToolException)
            {
                builder.Append(", too large to open");
            }

            builder.AppendLine(")");
        }

        if (pdfs.Count > MaxListedUploads)
        {
            builder.Append("  … and ").Append(pdfs.Count - MaxListedUploads).AppendLine(" more.");
        }

        var others = context.Uploads.Where(upload => !PdfToolContext.IsPdf(upload.FileName)).ToList();

        if (others.Count > 0)
        {
            builder.AppendLine().AppendLine("Other uploads (images can be placed with an image block; documents and spreadsheets can be turned into a PDF with convert_to_pdf):");

            foreach (var upload in others.Take(MaxListedUploads))
            {
                builder.Append("  \"").Append(upload.FileName).Append("\" (").Append(upload.ContentType ?? "unknown type").Append(", ").Append(FormatSize(upload.FileSize)).AppendLine(")");
            }
        }

        if (state.Assets.Count > 0)
        {
            builder.AppendLine().AppendLine("Pictures kept in the workspace (use as an image block's source):");

            foreach (var asset in state.Assets.Take(MaxListedUploads))
            {
                builder.Append("  asset:").Append(asset.Id).Append(" — ").AppendLine(asset.Description ?? asset.FileName);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Describe(PdfSource source, byte[] bytes, string password)
    {
        using var pdf = PdfFiles.OpenForReading(bytes, password);

        var builder = new StringBuilder();

        builder.Append(char.ToUpperInvariant(source.Describe()[0])).Append(source.Describe()[1..]).Append(": ")
            .Append(pdf.NumberOfPages.ToString(CultureInfo.InvariantCulture)).Append(" page(s), ").Append(FormatSize(bytes.Length))
            .Append(", PDF ").Append(pdf.Version.ToString("0.0", CultureInfo.InvariantCulture)).AppendLine(".");

        AppendPageSizes(builder, pdf);
        AppendMetadata(builder, pdf);
        AppendCatalogFacts(builder, pdf);
        AppendContentFacts(builder, pdf);
        AppendBookmarks(builder, pdf);
        AppendFormFields(builder, pdf);

        if (source.IsComposed && source.Working.Definition is { } definition)
        {
            builder.AppendLine().AppendLine("This is a composed document; change it with add_pdf_content and format_pdf.");
            builder.AppendLine(PdfCompositionDescriber.DescribeFormatting(definition));
            builder.AppendLine(PdfCompositionDescriber.DescribeBlocks(definition, 60));
        }
        else if (source.Working is { } working && working.History.Count > 0)
        {
            builder.AppendLine().AppendLine("Changes made to this working copy:");

            foreach (var change in working.History.TakeLast(10))
            {
                builder.Append("- ").AppendLine(change);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendPageSizes(StringBuilder builder, PdfDocument pdf)
    {
        var sizes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var rotated = new List<int>();

        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            var page = pdf.GetPage(number);
            var size = PdfPageSizes.Describe(page.Width, page.Height);

            if (!sizes.TryGetValue(size, out var pages))
            {
                sizes[size] = pages = [];
            }

            pages.Add(number);

            if (page.Rotation.Value != 0)
            {
                rotated.Add(number);
            }
        }

        builder.Append("Page sizes: ").AppendLine(string.Join("; ", sizes.Select(entry => sizes.Count == 1
            ? entry.Key
            : $"{entry.Key} (pages {PdfPageRange.Describe(entry.Value)})")));

        if (rotated.Count > 0)
        {
            builder.Append("Rotated pages: ").AppendLine(PdfPageRange.Describe(rotated));
        }
    }

    private static void AppendMetadata(StringBuilder builder, PdfDocument pdf)
    {
        var info = pdf.Information;
        var parts = new List<string>();

        void Add(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{label} \"{value.Trim()}\"");
            }
        }

        Add("title", info.Title);
        Add("author", info.Author);
        Add("subject", info.Subject);
        Add("keywords", info.Keywords);
        Add("creator", info.Creator);
        Add("producer", info.Producer);
        Add("created", info.CreationDate);
        Add("modified", info.ModifiedDate);

        builder.Append("Metadata: ").AppendLine(parts.Count == 0 ? "none" : string.Join(", ", parts));
    }

    private static void AppendCatalogFacts(StringBuilder builder, PdfDocument pdf)
    {
        var catalog = pdf.Structure.Catalog.CatalogDictionary;
        var language = PdfPigTokens.GetText(pdf, catalog, "Lang");
        var markInfo = PdfPigTokens.GetDictionary(pdf, catalog, "MarkInfo");
        var tagged = markInfo is not null &&
            markInfo.TryGet(NameToken.Create("Marked"), out var marked) &&
            PdfPigTokens.Resolve(pdf, marked) is BooleanToken { Data: true } &&
            catalog.ContainsKey(NameToken.Create("StructTreeRoot"));

        var names = PdfPigTokens.GetDictionary(pdf, catalog, "Names");
        var hasJavaScript = names is not null && names.ContainsKey(NameToken.Create("JavaScript"));
        var layers = PdfPigTokens.GetDictionary(pdf, catalog, "OCProperties");
        var layerCount = layers is not null &&
            layers.TryGet(NameToken.Create("OCGs"), out var ocgs) &&
            PdfPigTokens.Resolve(pdf, ocgs) is ArrayToken layerArray
                ? layerArray.Data.Count
                : 0;

        var attachments = pdf.Advanced.TryGetEmbeddedFiles(out var files) ? files.Count : 0;

        builder.Append("Language: ").Append(string.IsNullOrWhiteSpace(language) ? "not set" : language)
            .Append(". Tagged for accessibility: ").Append(tagged ? "yes" : "no")
            .Append(". Encrypted: ").Append(pdf.IsEncrypted ? "yes" : "no").AppendLine(".");

        var extras = new List<string>();

        if (attachments > 0)
        {
            extras.Add($"{attachments} attachment(s)");
        }

        if (layerCount > 0)
        {
            extras.Add($"{layerCount} layer(s)");
        }

        if (hasJavaScript)
        {
            extras.Add("document JavaScript");
        }

        if (extras.Count > 0)
        {
            builder.Append("Also contains: ").Append(string.Join(", ", extras)).AppendLine(".");
        }
    }

    private static void AppendContentFacts(StringBuilder builder, PdfDocument pdf)
    {
        var withoutText = new List<int>();
        var images = 0;
        var links = 0;
        var annotations = new Dictionary<AnnotationType, int>();

        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            var page = pdf.GetPage(number);
            var imageCount = page.NumberOfImages;

            images += imageCount;

            if (page.Letters.Count(letter => !string.IsNullOrWhiteSpace(letter.Value)) < 5)
            {
                withoutText.Add(number);
            }

            try
            {
                links += page.GetHyperlinks().Count;

                foreach (var annotation in page.GetAnnotations())
                {
                    if (annotation.Type is AnnotationType.Link or AnnotationType.Widget or AnnotationType.Popup)
                    {
                        continue;
                    }

                    annotations[annotation.Type] = annotations.GetValueOrDefault(annotation.Type) + 1;
                }
            }
            catch (Exception)
            {
                // A malformed annotation array costs the count, not the description.
            }
        }

        builder.Append("Images: ").Append(images.ToString(CultureInfo.InvariantCulture))
            .Append(". Hyperlinks: ").Append(links.ToString(CultureInfo.InvariantCulture))
            .Append(". Annotations: ").Append(annotations.Count == 0
                ? "none"
                : string.Join(", ", annotations.Select(entry => $"{entry.Value} {entry.Key.ToString().ToLowerInvariant()}")))
            .AppendLine(".");

        if (withoutText.Count > 0)
        {
            builder.Append("Pages with no extractable text (possibly scanned; ocr_pdf can read them): ").AppendLine(PdfPageRange.Describe(withoutText));
        }
    }

    private static void AppendBookmarks(StringBuilder builder, PdfDocument pdf)
    {
        if (!pdf.TryGetBookmarks(out var bookmarks) || bookmarks.Roots.Count == 0)
        {
            builder.AppendLine("Bookmarks: none.");

            return;
        }

        var lines = new List<string>();

        void Walk(IReadOnlyList<BookmarkNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                if (lines.Count >= 20)
                {
                    return;
                }

                var page = node is DocumentBookmarkNode document ? $" (p. {document.PageNumber})" : string.Empty;

                lines.Add(new string(' ', depth * 2) + "- " + node.Title + page);

                if (depth < 2)
                {
                    Walk(node.Children, depth + 1);
                }
            }
        }

        Walk(bookmarks.Roots, 0);

        builder.AppendLine("Bookmarks:").AppendLine(string.Join('\n', lines));
    }

    private static void AppendFormFields(StringBuilder builder, PdfDocument pdf)
    {
        if (!pdf.TryGetForm(out var form) || form.Fields.Count == 0)
        {
            builder.AppendLine("Form fields: none.");

            return;
        }

        var fields = new List<AcroFieldBase>();

        void Collect(IEnumerable<AcroFieldBase> source)
        {
            foreach (var field in source)
            {
                if (field is AcroNonTerminalField parent && parent.Children is { Count: > 0 } && field is not AcroRadioButtonsField)
                {
                    Collect(parent.Children);
                }
                else
                {
                    fields.Add(field);
                }
            }
        }

        Collect(form.Fields);

        var signatures = fields.Count(field => field is AcroSignatureField);

        builder.Append("Form fields: ").Append(fields.Count.ToString(CultureInfo.InvariantCulture));

        if (signatures > 0)
        {
            builder.Append(" (").Append(signatures.ToString(CultureInfo.InvariantCulture)).Append(" signature field(s))");
        }

        builder.Append(" — ").Append(string.Join(", ", fields.Take(15).Select(field => $"{field.Information?.PartialName} ({field.FieldType})")));

        if (fields.Count > 15)
        {
            builder.Append(", …");
        }

        builder.AppendLine(". Use get_pdf_form_fields for values and options.");
    }

    private static string FormatSize(long bytes)
    {
        return bytes >= 1024 * 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.0} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, bytes / 1024.0):0} KB");
    }
}
