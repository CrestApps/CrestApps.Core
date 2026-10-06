using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Formatting;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Sets the page color and a text watermark.
/// </summary>
internal sealed class SetWordPageBackgroundTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.SetWordPageBackground;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "color": { "type": "string", "description": "Page color, such as #FFF8E7 or 'ivory'; 'none' removes it." },
            "watermark": { "type": "string", "description": "Watermark text behind the text of every page, such as DRAFT or CONFIDENTIAL; 'none' removes it." },
            "watermark_color": { "type": "string", "description": "Default light gray." },
            "watermark_size": { "type": "number", "description": "Text size in points. Default 72." },
            "diagonal": { "type": "boolean", "description": "Run the watermark corner to corner. Default true." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetWordPageBackgroundTool"/> class.
    /// </summary>
    public SetWordPageBackgroundTool()
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
    public override string Description => "Sets the page background of a Word document: a page 'color', and a text 'watermark' (DRAFT, CONFIDENTIAL…) drawn large and light behind the text of every page. 'none' removes either.";

    /// <summary>
    /// Sets the background.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var color = arguments.GetString("color");
        var watermark = arguments.GetString("watermark");

        if (color is null && watermark is null)
        {
            throw new WordToolException("Pass 'color' and/or 'watermark' ('none' removes either).");
        }

        var (summary, document) = await context.EditAsync(arguments.Document(), "Set the page background", edit =>
        {
            var changes = new List<string>();
            var package = edit.Package;

            if (color is not null)
            {
                if (string.Equals(color, "none", StringComparison.OrdinalIgnoreCase))
                {
                    package.MainPart.Document.DocumentBackground = null;
                    WordSchemaOrder.Remove<DisplayBackgroundShape>(package.GetOrCreateSettings());
                    changes.Add("removed the page color");
                }
                else
                {
                    var hex = WordColor.TryParse(color, out var parsed) ? parsed : throw new WordToolException($"\"{color}\" is not a color.");

                    package.MainPart.Document.DocumentBackground = new DocumentBackground { Color = hex };

                    // Word only shows a page color it is told to display.
                    WordSchemaOrder.Set(package.GetOrCreateSettings(), new DisplayBackgroundShape());
                    changes.Add("page color #" + hex);
                }
            }

            if (watermark is not null)
            {
                if (string.Equals(watermark, "none", StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add($"removed {WordWatermark.Remove(package)} watermark(s)");
                }
                else
                {
                    var watermarkColor = WordColor.ParseOrDefault(arguments.GetString("watermark_color"), "D9D9D9");
                    var size = Math.Clamp(arguments.GetDouble("watermark_size") ?? 72, 12, 200);
                    var diagonal = arguments.GetBoolean("diagonal") ?? true;
                    var headers = WordWatermark.Apply(package, id => WordWatermark.Create(watermark.Trim(), watermarkColor, size, diagonal, id));

                    changes.Add($"watermark \"{watermark.Trim()}\" in {headers} header(s)");
                }
            }

            return Task.FromResult(string.Join("; ", changes));
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}. preview_word shows it.";
    }
}
