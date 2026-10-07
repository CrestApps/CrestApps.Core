using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Checks a PDF's integrity: header and end of file, revisions, whether strict and repairing readers open
/// it, the page tree and page boxes, content streams and the resources they name, fonts, references,
/// encryption and active content.
/// </summary>
internal sealed class ValidatePdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ValidatePdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidatePdfTool"/> class.
    /// </summary>
    public ValidatePdfTool()
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
    public override string Description => "Checks whether a PDF file is intact and well formed: its header and end, incremental updates and linearization, whether strict and repairing readers open it, page count, page boxes, content-stream syntax, resources the content names but lacks, fonts whose text would extract as garbage, broken references, encryption, and JavaScript or launch actions. Use it when a PDF will not open, looks wrong, came from an unknown source, or before relying on edits. Returns errors, warnings and information with page numbers. It does not check layout, accessibility or standards; use check_pdf_quality, check_pdf_accessibility or validate_pdf_compliance for those.";

    /// <summary>
    /// Validates the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        var password = arguments.GetString("password");

        using var document = PdfInspectedDocument.Open(bytes, password, requireContent: false, requireObjects: false);

        var checks = PdfIntegrityChecks.Run(document, password, cancellationToken);
        var failures = checks.Count(PdfCheckStatus.Fail);
        var warnings = checks.Count(PdfCheckStatus.Warn);

        var verdict = failures > 0
            ? "the file has integrity problems that some viewers may not recover from"
            : warnings > 0
                ? "the file is readable, with points to review"
                : "the file is structurally sound";

        var title = string.Create(
            CultureInfo.InvariantCulture,
            $"Integrity of {source.Describe()}: {document.PageCount} page(s), {bytes.LongLength:N0} bytes.");

        return checks.Render(
            title,
            verdict,
            bySeverity: true,
            ["These checks read the file's structure; they do not judge how the pages look (check_pdf_quality) or standards conformance (validate_pdf_compliance)."]);
    }
}
