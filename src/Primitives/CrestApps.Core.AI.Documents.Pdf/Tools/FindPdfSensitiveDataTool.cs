using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Finds personal, financial and other sensitive values in a PDF, so they can be reviewed before redaction.
/// </summary>
internal sealed class FindPdfSensitiveDataTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.FindPdfSensitiveData;

    private const int MaxListed = 150;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "categories": {
              "type": "array",
              "items": { "type": "string", "enum": ["email", "phone", "url", "ip_address", "credit_card", "iban", "us_ssn", "date", "money", "percentage", "us_zip_code"] },
              "description": "Kinds to look for. Defaults to email, phone, credit_card, iban, us_ssn and ip_address."
            },
            "show_values": { "type": "boolean", "description": "Show values in full instead of masked. Only when the user asks to see them." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FindPdfSensitiveDataTool"/> class.
    /// </summary>
    public FindPdfSensitiveDataTool()
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
    public override string Description => "Finds sensitive data in a PDF before redaction: email addresses, phone numbers, payment card numbers (Luhn-checked), IBANs (checksum-checked), US social security numbers and IP addresses by default; dates, amounts, percentages, URLs and ZIP codes on request. Returns each finding masked, with its page, context and position, and the redact_pdf call that removes them. Changes nothing.";

    /// <summary>
    /// Finds the data.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        var categories = arguments.GetStrings("categories", splitCommas: true);
        var kinds = categories.Count > 0 ? categories : [.. PdfPatternLibrary.SensitiveKinds];
        var showValues = arguments.GetBoolean("show_values") == true;

        using var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));

        var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages);
        var findings = new List<(PdfTextMatch Match, UglyToad.PdfPig.Content.Page Page)>();

        foreach (var number in pages)
        {
            var page = pdf.GetPage(number);

            foreach (var match in PdfTextFinder.FindPatterns(page, kinds))
            {
                findings.Add((match, page));
            }
        }

        if (findings.Count == 0)
        {
            return $"No {string.Join(", ", kinds)} values were found in {source.Describe()} (pages {PdfPageRange.Describe(pages)}). Scanned pages without text are not searched; ocr_pdf can read them first.";
        }

        var builder = new StringBuilder();

        builder.Append("Found ").Append(findings.Count.ToString(CultureInfo.InvariantCulture)).Append(" value(s) in ").Append(source.Describe()).Append(": ")
            .AppendLine(string.Join(", ", findings.GroupBy(finding => finding.Match.Kind).Select(group => $"{group.Count()} {group.Key}")));

        // A snippet around one finding often shows another; every value found is masked wherever it appears,
        // or the context would leak what the value itself hides.
        var masks = findings
            .Select(finding => finding.Match)
            .GroupBy(match => match.Text, StringComparer.Ordinal)
            .Select(group => (Value: group.Key, Mask: PdfPatternLibrary.Mask(group.First().Kind, group.Key)))
            .OrderByDescending(entry => entry.Value.Length)
            .ToList();

        foreach (var (match, page) in findings.Take(MaxListed))
        {
            var value = showValues ? match.Text : PdfPatternLibrary.Mask(match.Kind, match.Text);
            var snippet = match.Snippet;

            if (!showValues)
            {
                foreach (var (text, mask) in masks)
                {
                    snippet = snippet.Replace(text, mask, StringComparison.Ordinal);
                }
            }

            builder.Append("- p. ").Append(match.PageNumber).Append(' ').Append(match.Kind).Append(": ").Append(value)
                .Append(" — \"").Append(snippet).Append("\" at ").AppendLine(match.Boxes[0].Describe(page));
        }

        if (findings.Count > MaxListed)
        {
            builder.Append("… and ").Append(findings.Count - MaxListed).AppendLine(" more.");
        }

        builder.Append("To remove them permanently: redact_pdf with categories [")
            .Append(string.Join(", ", findings.Select(finding => $"\"{finding.Match.Kind}\"").Distinct()))
            .Append("]. Review the list with the user first: pattern matching can include values that are not sensitive and miss ones written unusually.");

        return builder.ToString();
    }
}
