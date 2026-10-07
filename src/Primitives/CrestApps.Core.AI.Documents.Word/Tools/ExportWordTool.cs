using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Structure;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Writes a Word document, or chosen content of it, as a <c>.docx</c> download.
/// </summary>
internal sealed class ExportWordTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.ExportWord;

    /// <summary>
    /// The media type of a Word document.
    /// </summary>
    public const string DocxMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "file_name": { "type": "string", "description": "The download's file name. Defaults to the document's name; .docx is added." },
            "ids": { "type": "array", "items": { "type": "string" }, "description": "Export only these elements (ids from get_word_document) as a separate document. An element in a table exports the whole table." },
            "headings": { "type": "array", "items": { "type": "string" }, "description": "Export only these headings, each with everything under it up to the next heading of the same or a higher level. Combines with 'ids'." },
            "update_fields": { "type": "boolean", "description": "Refresh the table of contents, caption numbers and page references first, and ask Word to update fields when it opens the file. Default true." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportWordTool"/> class.
    /// </summary>
    public ExportWordTool()
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
    public override string Description => "Writes a Word document as a .docx file the user can download, after refreshing its table of contents, captions and references. Returns a [doc:N] marker that MUST be written in the answer exactly as given. Preview with preview_word first so the user sees what they will download. Do not export again in the same turn unless the document changed. To export only part of the document as a separate file, pass 'headings' (a heading with everything under it) and/or 'ids'; the working document is unchanged.";

    /// <summary>
    /// Exports the document.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);
        var bytes = await context.ReadBytesAsync(source, cancellationToken);
        var ids = arguments.GetIds("ids");
        var headings = arguments.GetIds("headings");
        var partial = ids.Count + headings.Count > 0;
        var kept = 0;

        if (partial || arguments.GetBoolean("update_fields") != false)
        {
            using var package = WordPackage.Open(bytes);

            if (partial)
            {
                // A copy is cut down to the chosen content; the working document keeps all of it.
                kept = WordContentSubset.Keep(package, ids, headings);
            }

            if (arguments.GetBoolean("update_fields") != false)
            {
                WordDocumentRefresher.Refresh(package, context.Services);
            }

            bytes = package.Save();
        }

        if (partial)
        {
            try
            {
                using var check = WordPackage.Open(bytes);
            }
            catch (InvalidDataException ex)
            {
                throw new WordToolException($"The chosen content could not be written as a document: {ex.Message}", ex);
            }
        }

        var baseName = WordToolContext.SanitizeName(arguments.GetString("file_name") ?? (partial ? (source.IsUpload ? Path.GetFileNameWithoutExtension(source.Name) : source.Name) + "-excerpt" : source.Name));
        var fileName = baseName + ".docx";
        var marker = await context.ExportAsync(fileName, bytes, DocxMediaType, cancellationToken);

        if (partial)
        {
            return $"Exported {kept} element(s) of {source.Describe()} as \"{fileName}\" ({bytes.Length:N0} bytes); the working document is unchanged. WRITE THIS MARKER IN YOUR ANSWER EXACTLY AS SHOWN, ON A LINE OF ITS OWN: {marker}";
        }

        if (source.Working is not null)
        {
            await context.MutateAsync(state =>
            {
                var document = state.Find(source.Working.Name);

                if (document is not null)
                {
                    document.ExportedVersion = document.Version;
                }

                return Task.FromResult(true);
            }, cancellationToken);
        }

        return $"Exported {source.Describe()} as \"{fileName}\" ({bytes.Length:N0} bytes). WRITE THIS MARKER IN YOUR ANSWER EXACTLY AS SHOWN, ON A LINE OF ITS OWN: {marker}";
    }
}
