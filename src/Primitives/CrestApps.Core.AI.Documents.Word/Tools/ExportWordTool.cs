using CrestApps.Core.AI.Documents.Generation;
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
            "update_fields": { "type": "boolean", "description": "Refresh the table of contents, caption numbers and page references first. Word is asked to update fields when it opens the file only when some could not be refreshed here. Default true." }
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
        var ids = arguments.GetIds("ids");
        var headings = arguments.GetIds("headings");
        var updateFields = arguments.GetBoolean("update_fields") != false;

        if (ids.Count + headings.Count == 0)
        {
            var fileName = WordToolContext.SanitizeName(arguments.GetString("file_name") ?? source.Name) + ".docx";
            var isDefaultFile = string.Equals(fileName, DefaultFileName(source), StringComparison.OrdinalIgnoreCase);

            // A preview in this turn already gave this version of the document as a download; a second copy of
            // the same file would only make the reader wonder which one to take.
            if (isDefaultFile && updateFields && PreviewDownloads.TryGet(DownloadKey(source), source.Key, out var offered))
            {
                return $"This version of {source.Describe()} was already given as the download \"{fileName}\" in this turn. WRITE THIS MARKER IN YOUR ANSWER EXACTLY AS SHOWN, ON A LINE OF ITS OWN: {offered}";
            }

            var (marker, length) = await ExportWholeAsync(context, source, fileName, updateFields, cancellationToken);

            if (isDefaultFile && updateFields)
            {
                PreviewDownloads.Record(DownloadKey(source), source.Key, marker);
            }

            return $"Exported {source.Describe()} as \"{fileName}\" ({length:N0} bytes). WRITE THIS MARKER IN YOUR ANSWER EXACTLY AS SHOWN, ON A LINE OF ITS OWN: {marker}";
        }

        var bytes = await context.ReadBytesAsync(source, cancellationToken);
        int kept;

        using (var package = WordPackage.Open(bytes))
        {
            // A copy is cut down to the chosen content; the working document keeps all of it.
            kept = WordContentSubset.Keep(package, ids, headings);

            if (updateFields)
            {
                WordDocumentRefresher.Refresh(package, context.Services);
            }

            bytes = package.Save();
        }

        try
        {
            using var check = WordPackage.Open(bytes);
        }
        catch (InvalidDataException ex)
        {
            throw new WordToolException($"The chosen content could not be written as a document: {ex.Message}", ex);
        }

        var baseName = WordToolContext.SanitizeName(arguments.GetString("file_name") ?? (source.IsUpload ? Path.GetFileNameWithoutExtension(source.Name) : source.Name) + "-excerpt");
        var excerptName = baseName + ".docx";
        var excerptMarker = await context.ExportAsync(excerptName, bytes, DocxMediaType, cancellationToken);

        return $"Exported {kept} element(s) of {source.Describe()} as \"{excerptName}\" ({bytes.Length:N0} bytes); the working document is unchanged. WRITE THIS MARKER IN YOUR ANSWER EXACTLY AS SHOWN, ON A LINE OF ITS OWN: {excerptMarker}";
    }

    /// <summary>
    /// Writes the whole document as a download and records the version that was given.
    /// </summary>
    /// <param name="context">The Word context.</param>
    /// <param name="source">The document.</param>
    /// <param name="fileName">The download's file name.</param>
    /// <param name="updateFields">Whether the table of contents, captions and references are refreshed first.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The download marker and the size of the file.</returns>
    internal static async Task<(string Marker, int Length)> ExportWholeAsync(WordToolContext context, WordSource source, string fileName, bool updateFields, CancellationToken cancellationToken)
    {
        var bytes = await context.ReadBytesAsync(source, cancellationToken);

        if (updateFields)
        {
            using var package = WordPackage.Open(bytes);

            WordDocumentRefresher.Refresh(package, context.Services);

            bytes = package.Save();
        }

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

        return (marker, bytes.Length);
    }

    /// <summary>
    /// Gets the file name a document is downloaded under when no other name is asked for.
    /// </summary>
    /// <param name="source">The document.</param>
    /// <returns>The file name.</returns>
    internal static string DefaultFileName(WordSource source)
    {
        return WordToolContext.SanitizeName(source.Name) + ".docx";
    }

    /// <summary>
    /// Gets what identifies a document among the downloads given in a turn.
    /// </summary>
    /// <param name="source">The document.</param>
    /// <returns>The key.</returns>
    internal static string DownloadKey(WordSource source)
    {
        return "word|" + source.Name;
    }
}
