using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Loads an uploaded Word document into the workspace as a working copy, leaving the upload unchanged.
/// </summary>
internal sealed class ImportWordTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.ImportWord;

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "file": { "type": "string", "description": "The uploaded .docx, .docm, .dotx or .dotm file's name or id." },
            "name": { "type": "string", "description": "The working document's name. Defaults to the file name without its extension." }
          },
          "required": ["file"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportWordTool"/> class.
    /// </summary>
    public ImportWordTool()
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
    public override string Description => "Loads an uploaded Word document (.docx, .docm, .dotx, .dotm) into the workspace as a working copy for inspection and editing. The upload itself is never changed; a template becomes a regular document and macros are dropped. Editing tools also make this copy automatically on their first change, so call it when the user asks to open a file or to keep a separately named copy.";

    /// <summary>
    /// Imports the upload.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var fileName = arguments.GetString("file") ?? throw new WordToolException("Pass 'file': the uploaded Word document's name.");
        var upload = context.FindUpload(fileName, wordOnly: true)
            ?? throw new WordToolException($"There is no uploaded Word document named \"{fileName}\". {context.DescribeAvailable(await context.GetStateAsync(cancellationToken))}");

        var bytes = await context.ReadUploadAsync(upload, cancellationToken)
            ?? throw new WordToolException($"The stored file for \"{upload.FileName}\" is missing. Ask the user to upload it again.");

        var hadMacros = string.Equals(Path.GetExtension(upload.FileName), ".docm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(upload.FileName), ".dotm", StringComparison.OrdinalIgnoreCase);

        byte[] copy;
        List<WordBlock> blocks;
        string pageSetup;

        try
        {
            using var package = WordPackage.Open(bytes);

            blocks = WordBlockReader.Read(package);
            pageSetup = WordDescriber.DescribeSection(WordSections.All(package)[0]);
            copy = package.Save();
        }
        catch (InvalidDataException ex)
        {
            throw new WordToolException($"\"{upload.FileName}\" cannot be opened as a Word document. {ex.Message}", ex);
        }

        var document = await context.MutateAsync(
            state => context.AddDocumentAsync(
                state,
                arguments.GetString("name") ?? Path.GetFileNameWithoutExtension(upload.FileName),
                copy,
                design: null,
                $"Imported from upload \"{upload.FileName}\"",
                upload,
                cancellationToken),
            cancellationToken);

        var answer = new StringBuilder();

        answer.Append("Imported \"").Append(upload.FileName).Append("\" as working document \"").Append(document.Name).Append("\" (the upload is unchanged). ")
            .Append(blocks.Count).Append(" elements; page: ").Append(pageSetup).AppendLine(".");

        if (hadMacros)
        {
            answer.AppendLine("The file's macros are not kept: working documents are saved as .docx.");
        }

        answer.AppendLine("First elements:");

        foreach (var block in blocks.Take(40))
        {
            answer.AppendLine(WordDescriber.Line(block, 100));
        }

        if (blocks.Count > 40)
        {
            answer.Append("… call get_word_document for the rest.");
        }

        return answer.ToString().TrimEnd();
    }
}
