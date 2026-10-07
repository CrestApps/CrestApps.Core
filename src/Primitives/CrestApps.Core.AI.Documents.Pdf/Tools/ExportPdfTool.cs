using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Writes a PDF as a download attached to the conversation.
/// </summary>
internal sealed class ExportPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ExportPdf;

    private const string CacheKey = nameof(ExportPdfTool) + ".Markers";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            "file_name": { "type": "string", "description": "Optional download file name. Defaults to the working PDF's name." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportPdfTool"/> class.
    /// </summary>
    public ExportPdfTool()
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
    public override string Description => "Writes a working PDF (a composed document or an edited copy) as a download and returns a [doc:N] marker that MUST be returned exactly as given. Call it when the user wants the file, after previewing. Export again only after the document changed.";

    /// <summary>
    /// Exports the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var fileName = PdfToolContext.SanitizeName(arguments.GetString("file_name") ?? source.Name) + ".pdf";
        var key = string.Join('|', source.Name, source.Working?.Version.ToString(CultureInfo.InvariantCulture) ?? source.Upload?.ItemId, fileName);

        if (AIInvocationScope.Current?.Items.TryGetValue(CacheKey, out var value) == true &&
            value is Dictionary<string, string> cache &&
            cache.TryGetValue(key, out var existing))
        {
            return $"This version of \"{source.Name}\" was already exported in this turn. Return the same marker exactly as given: {existing}";
        }

        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        var marker = await context.ExportAsync(fileName, bytes, "application/pdf", cancellationToken);

        if (string.IsNullOrEmpty(marker))
        {
            return $"The file \"{fileName}\" was stored, but there is no conversation turn to attach a download link to.";
        }

        var invocation = AIInvocationScope.Current;

        if (invocation is not null)
        {
            if (!invocation.Items.TryGetValue(CacheKey, out var store) || store is not Dictionary<string, string> markers)
            {
                markers = new Dictionary<string, string>(StringComparer.Ordinal);
                invocation.Items[CacheKey] = markers;
            }

            markers[key] = marker;
        }

        var pages = PdfFiles.CountPages(bytes);
        var note = source.IsUpload
            ? " This is the uploaded file unchanged; nothing has been edited."
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Created the download \"{fileName}\" ({pages} page(s), {bytes.Length / 1024.0:N0} KB) from {source.Describe()}.{note} Return this marker in your answer exactly as given: {marker}");
    }
}
