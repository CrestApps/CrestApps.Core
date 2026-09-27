using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Removes a PDF's password protection and permission restrictions, given the password the user supplied.
/// </summary>
internal sealed class RemovePdfSecurityTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.RemovePdfSecurity;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.SaveAs}},
            "password": {
              "type": "string",
              "description": "The PDF's owner password — or its open password when that grants full access — exactly as the user supplied it. Never guess or try passwords."
            }
          },
          "required": ["password"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemovePdfSecurityTool"/> class.
    /// </summary>
    public RemovePdfSecurityTool()
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
    public override string Description => "Removes a PDF's password protection and permission restrictions, saving an unprotected working copy (the upload stays protected). It needs the owner password, or the open password when that grants full access, and only a password the user gave you: never guess, try variations or search for passwords.";

    /// <summary>
    /// Removes the protection.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var password = arguments.GetString("password")
            ?? throw new PdfToolException("Pass 'password': the PDF's owner password as the user gave it. Ask the user for it; never guess.");

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            if (!PdfProtection.IsEncrypted(bytes, password))
            {
                throw new PdfToolException($"{PdfPropertiesToolText.Capitalize(target.Describe())} is not password protected; there is nothing to remove.");
            }

            using var document = PdfFiles.OpenForEditing(bytes, password);

            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document);

            document.SecurityHandler.SetEncryptionToNoneAndResetPasswords();

            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, "Removed the password protection", cancellationToken);
            var answer = new StringBuilder();

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine("The working copy has no password and no permission restrictions; it opens and can be edited without a password." +
                (target.IsUpload ? " The upload itself is still protected." : string.Empty));

            PdfPropertiesToolText.AppendNotes(answer, signatures);

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }
}
