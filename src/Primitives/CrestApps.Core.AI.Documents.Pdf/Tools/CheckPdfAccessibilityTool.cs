using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Checks the accessibility requirements of a PDF that can be verified from the file, with a score and the
/// fixes for each failure.
/// </summary>
internal sealed class CheckPdfAccessibilityTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.CheckPdfAccessibility;

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
    /// Initializes a new instance of the <see cref="CheckPdfAccessibilityTool"/> class.
    /// </summary>
    public CheckPdfAccessibilityTool()
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
    public override string Description => "Checks whether a PDF is accessible to people using screen readers and keyboards: tagging, document language, title and whether viewers show it, alternative text for figures, extractable text (not image-only scans), fonts mapped to Unicode, bookmarks for long documents, form field tooltips, link descriptions, tab order, table header cells and headings. Returns a checklist with a score (checks passed out of those that apply) and a concrete fix for each failure; many fixes can be applied with tag_pdf_accessibility. Use it when asked whether a PDF is accessible, before publishing a PDF, or after tagging one. These are automated checks: they cannot judge reading order or whether alternative text is meaningful.";

    /// <summary>
    /// Checks the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        using var document = PdfInspectedDocument.Open(bytes, arguments.GetString("password"), requireContent: false, requireObjects: true);

        var facts = PdfAccessibilityFacts.Collect(document.Objects, document.Content, cancellationToken);
        var checks = new PdfCheckList();

        PdfAccessibilityChecks.Run(checks, facts);

        var passed = checks.Count(PdfCheckStatus.Pass);
        var applicable = passed + checks.Count(PdfCheckStatus.Warn) + checks.Count(PdfCheckStatus.Fail);
        var failures = checks.Count(PdfCheckStatus.Fail);
        var warnings = checks.Count(PdfCheckStatus.Warn);

        var verdict = failures > 0
            ? string.Create(CultureInfo.InvariantCulture, $"not accessible yet: {failures} requirement(s) not met; score {passed}/{applicable}")
            : warnings > 0
                ? string.Create(CultureInfo.InvariantCulture, $"mostly accessible, with points to review; score {passed}/{applicable}")
                : string.Create(CultureInfo.InvariantCulture, $"no accessibility problems found by these checks; score {passed}/{applicable}");

        var notes = new List<string>
        {
            "Score = checks passed out of those that apply (information items are not counted).",
            "Automated checks cannot judge reading order, whether alternative text is meaningful, colour contrast or plain language; a manual review, and a checker such as PAC or veraPDF for PDF/UA, are still needed. validate_pdf_compliance checks the PDF/UA requirements.",
        };

        return checks.Render(
            string.Create(CultureInfo.InvariantCulture, $"Accessibility of {source.Describe()}, {facts.PageCount} page(s)."),
            verdict,
            bySeverity: true,
            notes);
    }
}
