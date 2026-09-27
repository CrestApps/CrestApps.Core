using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Removes the hidden information a PDF carries before it is shared.
/// </summary>
internal sealed class SanitizePdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.SanitizePdf;

    private static readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal)
    {
        [PdfSanitizer.Metadata] = "metadata entry(ies)",
        [PdfSanitizer.Scripts] = "script(s) and automatic action(s)",
        [PdfSanitizer.Attachments] = "attached file(s)",
        [PdfSanitizer.Thumbnails] = "page thumbnail(s)",
        [PdfSanitizer.HiddenText] = "invisible character(s)",
        [PdfSanitizer.HiddenLayers] = "hidden layer(s) with their content",
        [PdfSanitizer.Comments] = "comment(s) and mark(s)",
        [PdfSanitizer.FormData] = "filled form field(s)",
        [PdfSanitizer.ExternalLinks] = "link(s) to web addresses or other files",
    };

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "remove": {
              "type": "array",
              "items": { "type": "string", "enum": ["metadata", "scripts", "attachments", "thumbnails", "hidden_text", "hidden_layers", "comments", "form_data", "external_links", "all"] },
              "description": "What to remove. Defaults to everything that is not visible on the pages: metadata, scripts, attachments, thumbnails, hidden_text and hidden_layers. comments, form_data and external_links are visible and removed only when named; 'all' removes everything."
            },
            "keep": { "type": "array", "items": { "type": "string" }, "description": "Kinds to leave out of the defaults, for example [\"hidden_text\"] to keep an OCR text layer." },
            "report_only": { "type": "boolean", "description": "Report what would be removed without saving anything." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SanitizePdfTool"/> class.
    /// </summary>
    public SanitizePdfTool()
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
    public override string Description => "Removes the hidden information in a PDF before it is shared: metadata (author, titles, editing history, XMP), JavaScript and actions that run by themselves, attached files, thumbnails, invisible text and hidden layers — and, when asked, comments, form data and external links. Reports what was removed and what visible extras remain. report_only lists it without saving. Saves a working PDF; the upload is never changed. To remove visible content use redact_pdf.";

    /// <summary>
    /// Sanitizes the document.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var requested = arguments.GetStrings("remove", splitCommas: true).Select(Normalize).ToList();
        var unknown = requested.Where(kind => kind != "all" && !PdfSanitizer.All.Contains(kind)).ToList();

        if (unknown.Count > 0)
        {
            throw new PdfToolException($"{string.Join(", ", unknown.Select(kind => $"'{kind}'"))} cannot be removed; choose from {string.Join(", ", PdfSanitizer.All)}.");
        }

        var kinds = new HashSet<string>(
            requested.Contains("all") ? PdfSanitizer.All : requested.Count > 0 ? requested : PdfSanitizer.Defaults,
            StringComparer.Ordinal);

        kinds.ExceptWith(arguments.GetStrings("keep", splitCommas: true).Select(Normalize));

        if (kinds.Count == 0)
        {
            throw new PdfToolException("Nothing is left to remove; name at least one kind in 'remove'.");
        }

        var password = arguments.GetString("password");
        var reportOnly = arguments.GetBoolean("report_only") == true;

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            // Everything is looked for on a copy, so the answer can also name what this call leaves in place.
            Dictionary<string, int> found;

            using (var survey = PdfFiles.OpenForEditing(bytes, password))
            {
                found = PdfSanitizer.Apply(survey, new HashSet<string>(PdfSanitizer.All, StringComparer.Ordinal));
            }

            var response = new StringBuilder();
            var remaining = found.Where(entry => !kinds.Contains(entry.Key) && entry.Value > 0 && entry.Key is PdfSanitizer.Comments or PdfSanitizer.FormData or PdfSanitizer.ExternalLinks).ToList();

            if (reportOnly || kinds.All(kind => found.GetValueOrDefault(kind) == 0))
            {
                response.Append(reportOnly ? "Nothing was saved. " : "Nothing needed removing, so no working copy was saved. ")
                    .Append(target.Describe()).AppendLine(" carries:");

                foreach (var kind in PdfSanitizer.All)
                {
                    response.Append("- ").Append(Describe(kind, found.GetValueOrDefault(kind)))
                        .AppendLine(kinds.Contains(kind) ? string.Empty : " (not selected)");
                }

                return response.ToString().TrimEnd();
            }

            byte[] saved;
            string signatures;
            Dictionary<string, int> removed;

            using (var document = PdfFiles.OpenForEditing(bytes, password))
            {
                signatures = PdfObjects.DescribeBrokenSignatures(document);
                removed = PdfSanitizer.Apply(document, kinds);
                saved = PdfFiles.Save(document);
            }

            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, "Sanitized: " + string.Join(", ", kinds.Order(StringComparer.Ordinal)), cancellationToken);

            response.Append("Saved working PDF \"").Append(working.Name).AppendLine("\". Removed:");

            foreach (var kind in PdfSanitizer.All.Where(kinds.Contains))
            {
                response.Append("- ").AppendLine(Describe(kind, removed.GetValueOrDefault(kind)));
            }

            if (removed.GetValueOrDefault(PdfSanitizer.HiddenText) > 200)
            {
                response.AppendLine("A large amount of invisible text usually is the searchable text layer of scanned pages; without it those pages can no longer be searched or copied from. Pass keep: [\"hidden_text\"] to keep it.");
            }

            if (remaining.Count > 0)
            {
                response.Append("Still in the file (visible, so kept unless named in 'remove'): ")
                    .Append(string.Join(", ", remaining.Select(entry => Describe(entry.Key, entry.Value))))
                    .AppendLine(".");
            }

            if (signatures is not null)
            {
                response.AppendLine(signatures);
            }

            response.Append(target.IsUpload ? "The uploaded file was not changed. " : string.Empty)
                .Append("What the pages show is unchanged; use redact_pdf to remove visible content.");

            return response.ToString();
        }, cancellationToken);
    }

    private static string Normalize(string kind)
    {
        return kind.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "javascript" or "js" or "actions" or "script" => PdfSanitizer.Scripts,
            "embedded_files" or "attachment" or "files" => PdfSanitizer.Attachments,
            "annotations" or "comment" => PdfSanitizer.Comments,
            "forms" or "form_values" or "form_fields" => PdfSanitizer.FormData,
            "links" => PdfSanitizer.ExternalLinks,
            "layers" or "hidden_content" => PdfSanitizer.HiddenLayers,
            "invisible_text" => PdfSanitizer.HiddenText,
            "xmp" or "properties" or "private_data" => PdfSanitizer.Metadata,
            var other => other,
        };
    }

    private static string Describe(string kind, int count)
    {
        return count.ToString(CultureInfo.InvariantCulture) + " " + _labels[kind];
    }
}
