using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Ingestion;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Lists, embeds, extracts and removes the files a PDF carries.
/// </summary>
internal sealed class ManagePdfAttachmentsTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ManagePdfAttachments;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "action": {
              "type": "string",
              "enum": ["list", "add", "extract", "remove"],
              "description": "list (default) shows the attachments; add embeds a file; extract offers one as a download; remove deletes one. list and extract save nothing."
            },
            "source": { "type": "string", "description": "For add: the file to embed — an uploaded file of any type, or a working or uploaded PDF — by name or document id." },
            "text": { "type": "string", "description": "For add, instead of 'source': the content of a text file to embed; name it with 'file_name'." },
            "file_name": { "type": "string", "description": "For add with 'text': the file name, such as notes.txt." },
            "name": { "type": "string", "description": "For add: the name to list the attachment under (defaults to its file name). For extract and remove: the attachment's name or file name, as list shows it." },
            "description": { "type": "string", "description": "For add: a description viewers show next to the attachment." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private static readonly HashSet<string> _safeDownloadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".txt", ".csv", ".md", ".json", ".docx", ".xlsx", ".pptx", ".yaml", ".yml",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagePdfAttachmentsTool"/> class.
    /// </summary>
    public ManagePdfAttachmentsTool()
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
    public override string Description => "Manages the files embedded in a PDF (attachments): list them with name, size and description; add an uploaded file, a working PDF or a text file; extract one as a download; or remove one. Embedded files listed in the document and files attached to pages are both covered. add and remove save a working copy.";

    /// <summary>
    /// Carries out the attachment request.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var action = (arguments.GetString("action") ?? "list").Trim().ToLowerInvariant();
        var password = arguments.GetString("password");

        switch (action)
        {
            case "list":
            case "extract":
                var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
                var bytes = await context.ReadPdfAsync(source, cancellationToken);

                using (var document = PdfFiles.OpenForImport(bytes, password))
                {
                    var attachments = PdfAttachments.List(document);

                    return action == "list"
                        ? DescribeList(source, attachments)
                        : await ExtractAsync(context, source, attachments, arguments.GetString("name"), cancellationToken);
                }

            case "add":
                return await AddAsync(arguments, context, password, cancellationToken);
            case "remove":
                return await RemoveAsync(arguments, context, password, cancellationToken);
            default:
                throw new PdfToolException($"'{action}' is not an action. Use list, add, extract or remove.");
        }
    }

    private static async Task<string> AddAsync(PdfToolArguments arguments, PdfToolContext context, string password, CancellationToken cancellationToken)
    {
        var sourceName = arguments.GetString("source");
        var text = arguments.GetString("text");

        if ((sourceName is null) == (text is null))
        {
            throw new PdfToolException("To add an attachment pass either 'source' (an uploaded file or a working PDF) or 'text' with 'file_name'.");
        }

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var (content, fileName) = await ReadContentAsync(context, state, sourceName, text, arguments.GetString("file_name"), cancellationToken);
            var name = CleanFileName(arguments.GetString("name") ?? fileName);

            if (content.LongLength > context.Options.MaxDocumentBytes)
            {
                throw new PdfToolException($"\"{fileName}\" is {content.LongLength:N0} bytes, larger than the {context.Options.MaxDocumentBytes:N0} bytes this agent keeps.");
            }

            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var document = PdfFiles.OpenForEditing(bytes, password);

            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document);
            var part = sync.PdfAPart;

            // PDF/A-1 allows no embedded files and PDF/A-2 only PDF/A files; PDF/A-3 and later allow any file
            // listed as an associated file.
            sync.DropPdfAConformance = part == 1 || (part == 2 && !ClaimsPdfA(content));

            var extension = Path.GetExtension(name);
            var replaced = PdfAttachments.Add(
                document,
                name,
                content,
                arguments.GetString("description"),
                MediaTypeHelper.InferMediaType(extension, "application/octet-stream"),
                context.TimeProvider.GetUtcNow(),
                associate: part >= 3);

            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, $"Attached \"{name}\"", cancellationToken);
            var answer = new StringBuilder();

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine(replaced
                ? $"Replaced the attachment \"{name}\" with {fileName} ({PdfPropertiesToolText.Size(content.LongLength)})."
                : $"Attached \"{name}\" ({PdfPropertiesToolText.Size(content.LongLength)}).");

            PdfPropertiesToolText.AppendNotes(answer, sync.DescribeConformance(), signatures);

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static async Task<string> RemoveAsync(PdfToolArguments arguments, PdfToolContext context, string password, CancellationToken cancellationToken)
    {
        var name = arguments.GetString("name")
            ?? throw new PdfToolException("Pass 'name': the attachment to remove, as action 'list' shows it.");

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var document = PdfFiles.OpenForEditing(bytes, password);

            var available = PdfAttachments.List(document);
            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document);
            var removed = PdfAttachments.Remove(document, name);

            if (removed == 0)
            {
                throw new PdfToolException($"There is no attachment named \"{name}\". " + DescribeNames(available));
            }

            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, $"Removed the attachment \"{name}\"", cancellationToken);
            var answer = new StringBuilder();

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Removed {removed} attachment(s) named \"{name}\"; the embedded file is no longer in the file."));

            PdfPropertiesToolText.AppendNotes(answer, signatures);

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static async Task<string> ExtractAsync(PdfToolContext context, PdfSource source, List<PdfAttachment> attachments, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (attachments.Count != 1)
            {
                throw new PdfToolException("Pass 'name': the attachment to extract. " + DescribeNames(attachments));
            }

            name = attachments[0].Name;
        }

        var attachment = attachments.FirstOrDefault(candidate => candidate.Matches(name))
            ?? throw new PdfToolException($"There is no attachment named \"{name}\". " + DescribeNames(attachments));

        var content = PdfAttachments.Read(attachment);
        var fileName = CleanFileName(attachment.FileName ?? attachment.Name);
        var safe = _safeDownloadExtensions.Contains(Path.GetExtension(fileName));
        var marker = await context.ExportAsync(fileName, content, safe ? null : "application/octet-stream", cancellationToken);
        var answer = new StringBuilder();

        answer.AppendLine($"Extracted \"{fileName}\" ({PdfPropertiesToolText.Size(content.LongLength)}) from {source.Describe()}. Download:");
        answer.AppendLine(marker);

        if (!safe)
        {
            answer.AppendLine("Its file type can run code in a browser or on a computer, so it is offered as a plain binary download; tell the user to open it only if they trust where the PDF came from.");
        }

        return answer.ToString().TrimEnd();
    }

    private static async Task<(byte[] Content, string FileName)> ReadContentAsync(
        PdfToolContext context,
        PdfWorkspaceState state,
        string sourceName,
        string text,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (text is not null)
        {
            var name = string.IsNullOrWhiteSpace(fileName) ? "notes.txt" : fileName.Trim();

            if (string.IsNullOrEmpty(Path.GetExtension(name)))
            {
                name += ".txt";
            }

            return (Encoding.UTF8.GetBytes(text), name);
        }

        var working = state.Find(sourceName) ?? state.Find(Path.GetFileNameWithoutExtension(sourceName));

        if (working is not null)
        {
            return (await context.ReadPdfAsync(PdfSource.ForWorking(working), cancellationToken), working.Name + ".pdf");
        }

        var upload = context.FindUpload(sourceName, pdfOnly: false);

        if (upload is null)
        {
            var uploads = context.Uploads.Select(document => $"\"{document.FileName}\"").ToList();

            throw new PdfToolException($"There is no uploaded file or working PDF named \"{sourceName}\". " +
                (uploads.Count == 0 ? "Nothing has been uploaded." : "Uploaded files: " + string.Join(", ", uploads) + "."));
        }

        var bytes = await context.ReadUploadAsync(upload, cancellationToken)
            ?? throw new PdfToolException($"The stored file for \"{upload.FileName}\" is missing. Ask the user to upload it again.");

        return (bytes, upload.FileName);
    }

    private static bool ClaimsPdfA(byte[] content)
    {
        try
        {
            using var pdf = PdfFiles.OpenForReading(content);

            return pdf.TryGetXmpMetadata(out var xmp) &&
                PdfXmpPacket.DescribeConformance(PdfXmpPacket.Parse(xmp.GetXmlBytes().ToArray())).Any(claim => claim.StartsWith("PDF/A", StringComparison.Ordinal));
        }
        catch (PdfToolException)
        {
            return false;
        }
    }

    private static string DescribeList(PdfSource source, List<PdfAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return $"{PdfPropertiesToolText.Capitalize(source.Describe())} has no attachments.";
        }

        var answer = new StringBuilder();

        answer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{PdfPropertiesToolText.Capitalize(source.Describe())} has {attachments.Count} attachment(s):"));

        foreach (var attachment in attachments.Take(100))
        {
            answer.Append("- \"").Append(attachment.Name).Append('"');

            if (!string.IsNullOrEmpty(attachment.FileName) && !string.Equals(attachment.FileName, attachment.Name, StringComparison.Ordinal))
            {
                answer.Append(" (file ").Append(attachment.FileName).Append(')');
            }

            answer.Append(attachment.Size is { } size ? ", " + PdfPropertiesToolText.Size(size) : ", size not recorded");

            if (!string.IsNullOrEmpty(attachment.MediaType))
            {
                answer.Append(", ").Append(attachment.MediaType);
            }

            if (attachment.Page is { } page)
            {
                answer.Append(string.Create(CultureInfo.InvariantCulture, $", attached to page {page}"));
            }

            if (!string.IsNullOrWhiteSpace(attachment.Description))
            {
                answer.Append(" — ").Append(PdfPropertiesToolText.Quote(attachment.Description, 120));
            }

            if (attachment.Stream is null)
            {
                answer.Append(" (refers to an external file; nothing embedded)");
            }

            answer.AppendLine();
        }

        return answer.ToString().TrimEnd();
    }

    private static string DescribeNames(List<PdfAttachment> attachments)
    {
        return attachments.Count == 0
            ? "The PDF has no attachments."
            : "Attachments: " + string.Join(", ", attachments.Take(50).Select(attachment => $"\"{attachment.Name}\"")) + ".";
    }

    private static string CleanFileName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim().Replace('\\', '/');
        var slash = trimmed.LastIndexOf('/');

        if (slash >= 0)
        {
            trimmed = trimmed[(slash + 1)..];
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(trimmed.Length);

        foreach (var character in trimmed)
        {
            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
        }

        var cleaned = builder.ToString().Trim(' ', '.');

        if (cleaned.Length > 150)
        {
            cleaned = cleaned[..150];
        }

        return cleaned.Length == 0 ? "attachment" : cleaned;
    }
}
