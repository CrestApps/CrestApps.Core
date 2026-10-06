using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Writes a Word document as a <c>.docx</c> download.
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
    public override string Description => "Writes a Word document as a .docx file the user can download, after refreshing its table of contents, captions and references. Returns a [doc:N] marker that MUST be written in the answer exactly as given. Preview with preview_word first so the user sees what they will download. Do not export again in the same turn unless the document changed.";

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

        if (arguments.GetBoolean("update_fields") != false)
        {
            using var package = WordPackage.Open(bytes);

            WordDocumentRefresher.Refresh(package, context.Services);
            bytes = package.Save();
        }

        var baseName = WordToolContext.SanitizeName(arguments.GetString("file_name") ?? source.Name);
        var fileName = baseName + ".docx";
        var marker = await context.ExportAsync(fileName, bytes, DocxMediaType, cancellationToken);

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
