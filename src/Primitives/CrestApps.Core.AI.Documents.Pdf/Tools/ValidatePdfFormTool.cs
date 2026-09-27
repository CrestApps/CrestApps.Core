using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Checks a form's values — the ones it holds, or ones about to be filled — against the form's own
/// constraints and any rules the request adds.
/// </summary>
internal sealed class ValidatePdfFormTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ValidatePdfForm;

    private static readonly TimeSpan _patternTimeout = TimeSpan.FromSeconds(1);

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            "values": {
              "type": "object",
              "description": "Optional values to check as though they were filled (field name → value); fields not given are checked with their current values.",
              "additionalProperties": { "type": ["string", "number", "boolean"] }
            },
            "rules": {
              "type": "array",
              "description": "Optional extra rules, on top of the form's own required flags, lengths and choices.",
              "items": {
                "type": "object",
                "properties": {
                  "field": { "type": "string" },
                  "required": { "type": "boolean" },
                  "format": { "type": "string", "enum": ["email", "phone", "date", "number", "integer", "url", "us_zip_code"] },
                  "pattern": { "type": "string", "description": "A regular expression the whole value must match." },
                  "min_length": { "type": "integer" },
                  "max_length": { "type": "integer" },
                  "min": { "type": "number" },
                  "max": { "type": "number" },
                  "allowed": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["field"]
              }
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidatePdfFormTool"/> class.
    /// </summary>
    public ValidatePdfFormTool()
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
    public override string Description => "Validates a PDF form before it is submitted: required fields filled, lengths within limits, choices valid, plus optional rules (email, phone, date, number, URL, ZIP, regex pattern, min/max, allowed values). Checks the current values, or values passed in 'values' as though they were filled. Changes nothing.";

    /// <summary>
    /// Validates the form.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        using var document = PdfFiles.OpenForImport(bytes, arguments.GetString("password"));

        var fields = PdfFormFields.Read(document);

        if (fields.Count == 0)
        {
            return $"{source.Describe()} has no form fields to validate.";
        }

        var values = fields.ToDictionary(field => field.Name, field => field.Type == "checkbox" ? (field.Value == "checked" ? "true" : string.Empty) : field.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();

        if (arguments.TryGetElement("values", out var proposed) && proposed.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in proposed.EnumerateObject())
            {
                var field = PdfFormFields.Find(fields, property.Name);

                if (field is null)
                {
                    unknown.Add(property.Name);

                    continue;
                }

                values[field.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => string.Empty,
                    _ => property.Value.GetRawText(),
                };
            }
        }

        var failures = new List<string>();

        foreach (var field in fields)
        {
            var value = values[field.Name];

            if (field.Required && string.IsNullOrWhiteSpace(value) && field.Type != "signature")
            {
                failures.Add($"\"{field.Name}\" is required but empty");
            }

            if (field.MaxLength is > 0 && value.Length > field.MaxLength)
            {
                failures.Add($"\"{field.Name}\" is {value.Length} characters, over its limit of {field.MaxLength}");
            }

            if (field.Type is "radio" or "listbox" && !string.IsNullOrEmpty(value) && value != "Off" &&
                !field.Options.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                failures.Add($"\"{field.Name}\" holds \"{value}\", which is not one of its choices ({string.Join(", ", field.Options)})");
            }
        }

        var rules = arguments.Get<List<RuleDefinition>>("rules") ?? [];

        foreach (var rule in rules)
        {
            var field = PdfFormFields.Find(fields, rule.Field);

            if (field is null)
            {
                failures.Add($"a rule names \"{rule.Field}\", which is not a field");

                continue;
            }

            CheckRule(rule, field.Name, values[field.Name], failures);
        }

        var builder = new StringBuilder();

        builder.Append(failures.Count == 0 ? "Valid: " : "Not valid: ")
            .Append(fields.Count.ToString(CultureInfo.InvariantCulture)).Append(" field(s) checked, ")
            .Append(failures.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" problem(s).");

        foreach (var failure in failures)
        {
            builder.Append("- ").AppendLine(failure);
        }

        if (unknown.Count > 0)
        {
            builder.Append("Values given for fields that do not exist: ").AppendLine(string.Join(", ", unknown));
        }

        return builder.ToString().TrimEnd();
    }

    private static void CheckRule(RuleDefinition rule, string name, string value, List<string> failures)
    {
        var empty = string.IsNullOrWhiteSpace(value);

        if (rule.Required == true && empty)
        {
            failures.Add($"\"{name}\" is required but empty");

            return;
        }

        if (empty)
        {
            return;
        }

        if (rule.MinLength is > 0 && value.Length < rule.MinLength)
        {
            failures.Add($"\"{name}\" is shorter than {rule.MinLength} characters");
        }

        if (rule.MaxLength is > 0 && value.Length > rule.MaxLength)
        {
            failures.Add($"\"{name}\" is longer than {rule.MaxLength} characters");
        }

        if (rule.Allowed is { Count: > 0 } && !rule.Allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            failures.Add($"\"{name}\" holds \"{value}\", not one of: {string.Join(", ", rule.Allowed)}");
        }

        var isNumber = PdfCellValuesCompatible(value, out var number);

        if ((rule.Min.HasValue || rule.Max.HasValue) && !isNumber)
        {
            failures.Add($"\"{name}\" should be a number");
        }
        else if (isNumber && ((rule.Min.HasValue && number < rule.Min) || (rule.Max.HasValue && number > rule.Max)))
        {
            failures.Add($"\"{name}\" is {value}, outside {rule.Min?.ToString(CultureInfo.InvariantCulture) ?? "-∞"}–{rule.Max?.ToString(CultureInfo.InvariantCulture) ?? "∞"}");
        }

        var formatOk = rule.Format?.Trim().ToLowerInvariant() switch
        {
            null or "" => true,
            "email" => PdfPatternLibrary.Find(value, [PdfPatternLibrary.Email]).Any(match => match.Value.Length == value.Trim().Length),
            "phone" => PdfPatternLibrary.Find(value, [PdfPatternLibrary.Phone]).Count > 0,
            "date" => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || PdfPatternLibrary.Find(value, [PdfPatternLibrary.Date]).Count > 0,
            "number" => isNumber,
            "integer" => long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            "url" => Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
            "us_zip_code" => Regex.IsMatch(value.Trim(), @"^\d{5}(-\d{4})?$", RegexOptions.None, _patternTimeout),
            _ => true,
        };

        if (!formatOk)
        {
            failures.Add($"\"{name}\" holds \"{value}\", which is not a valid {rule.Format}");
        }

        if (!string.IsNullOrWhiteSpace(rule.Pattern))
        {
            try
            {
                if (!Regex.IsMatch(value, "^(?:" + rule.Pattern + ")$", RegexOptions.CultureInvariant, _patternTimeout))
                {
                    failures.Add($"\"{name}\" does not match the pattern {rule.Pattern}");
                }
            }
            catch (ArgumentException)
            {
                failures.Add($"the pattern for \"{name}\" is not a valid regular expression");
            }
            catch (RegexMatchTimeoutException)
            {
                failures.Add($"the pattern for \"{name}\" took too long to evaluate");
            }
        }
    }

    private static bool PdfCellValuesCompatible(string value, out double number)
    {
        return Composition.PdfCellValues.TryParseNumber(value, percentAsFraction: false, out number);
    }

    private sealed class RuleDefinition
    {
        public string Field { get; set; }

        public bool? Required { get; set; }

        public string Format { get; set; }

        public string Pattern { get; set; }

        public int? MinLength { get; set; }

        public int? MaxLength { get; set; }

        public double? Min { get; set; }

        public double? Max { get; set; }

        public List<string> Allowed { get; set; }
    }
}
