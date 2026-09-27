using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Checks a PDF against the most common requirements of PDF/A (archiving) and PDF/UA (accessibility).
/// </summary>
internal sealed class ValidatePdfComplianceTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ValidatePdfCompliance;

    private const string Caveat = "These are indicative checks of the most common requirements, read from the file's objects; they are not a certification. validate_pdf_compliance is not a replacement for a conformance validator such as veraPDF.";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            "standard": {
              "type": "string",
              "enum": ["auto", "pdfa-1b", "pdfa-2b", "pdfa-3b", "pdfua-1"],
              "description": "The standard to check against. 'auto' (the default) checks the standards the file claims in its XMP metadata, including their A or U level; when it claims none, it checks PDF/A-2b and PDF/UA-1."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidatePdfComplianceTool"/> class.
    /// </summary>
    public ValidatePdfComplianceTool()
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
    public override string Description => "Checks a PDF against the main requirements of PDF/A-1, PDF/A-2 or PDF/A-3 (long-term archiving: XMP identification, output intent, no encryption, embedded fonts, no scripts or forbidden actions, no LZW, transparency and attachment rules, document ID, metadata consistency) or PDF/UA-1 (accessibility: identification, tagging, language, title, fonts, figure alternative text, form field tooltips, tab order, link descriptions). With 'auto' it checks what the file claims to be. Returns each requirement as pass, fail or warning and an overall 'appears to satisfy' or 'does not satisfy' verdict per standard. Use it when a PDF must meet an archiving or accessibility standard. The checks are indicative, not a certification.";

    /// <summary>
    /// Checks the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var requested = arguments.GetString("standard")?.Trim();
        PdfStandard explicitStandard = null;

        if (!string.IsNullOrEmpty(requested) &&
            !requested.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            !PdfStandard.TryParse(requested, out explicitStandard))
        {
            throw new PdfToolException($"\"{requested}\" is not a standard this tool checks. Use 'auto', 'pdfa-1b', 'pdfa-2b', 'pdfa-3b' or 'pdfua-1'.");
        }

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        using var document = PdfInspectedDocument.Open(bytes, arguments.GetString("password"), requireContent: false, requireObjects: true);

        var facts = PdfAccessibilityFacts.Collect(document.Objects, document.Content, cancellationToken);
        var notes = new List<string>();
        var standards = ChooseStandards(explicitStandard, facts.Xmp, notes);
        var checks = new PdfCheckList();

        foreach (var standard in standards)
        {
            if (standard.IsArchive)
            {
                PdfComplianceChecks.CheckArchive(checks, standard, document, facts);
            }
            else
            {
                PdfComplianceChecks.CheckUniversal(checks, standard, facts);
            }
        }

        var verdicts = standards.Select(standard => checks.Checks.Any(check => check.Category == standard.Name && check.Status == PdfCheckStatus.Fail)
            ? $"does not satisfy {standard.Name}"
            : $"appears to satisfy {standard.Name}");

        notes.Add(Caveat);

        return checks.Render(
            string.Create(CultureInfo.InvariantCulture, $"Standards compliance of {source.Describe()}, {facts.PageCount} page(s); checked: {string.Join(", ", standards.Select(standard => standard.Name))}."),
            string.Join("; ", verdicts),
            bySeverity: false,
            notes);
    }

    private static List<PdfStandard> ChooseStandards(PdfStandard explicitStandard, PdfXmpInfo xmp, List<string> notes)
    {
        if (explicitStandard is not null)
        {
            return [explicitStandard];
        }

        var standards = new List<PdfStandard>();

        if (xmp.PdfAPart is { } part)
        {
            var claim = string.Create(CultureInfo.InvariantCulture, $"pdfa-{part}{xmp.PdfAConformance}");

            if (PdfStandard.TryParse(claim, out var archive))
            {
                standards.Add(archive);
            }
            else
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"The file claims PDF/A-{part}{xmp.PdfAConformance?.ToLowerInvariant()}, which this tool does not check; it was checked against PDF/A-2b instead."));
                standards.Add(new PdfStandard(true, 2, "B"));
            }
        }

        if (xmp.PdfUAPart is not null)
        {
            standards.Add(new PdfStandard(false, 1, null));
        }

        if (standards.Count == 0)
        {
            notes.Add("The file claims neither PDF/A nor PDF/UA in its metadata, so it was checked against PDF/A-2b and PDF/UA-1.");
            standards.Add(new PdfStandard(true, 2, "B"));
            standards.Add(new PdfStandard(false, 1, null));
        }

        return standards;
    }
}
