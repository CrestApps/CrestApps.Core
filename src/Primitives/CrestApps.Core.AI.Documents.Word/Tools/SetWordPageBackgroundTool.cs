using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Sets or removes the page color.
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
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["color"],
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
    public override string Description => "Sets the page color of a Word document; 'none' removes it.";

    /// <summary>
    /// Sets the page color.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var color = arguments.GetString("color") ?? throw new WordToolException("Pass 'color', or 'none' to remove the page color.");

        var (summary, document) = await context.EditAsync(arguments.Document(), "Set the page color", edit =>
        {
            var package = edit.Package;

            if (string.Equals(color, "none", StringComparison.OrdinalIgnoreCase))
            {
                package.MainPart.Document.DocumentBackground = null;
                WordSchemaOrder.Remove<DisplayBackgroundShape>(package.GetOrCreateSettings());

                return Task.FromResult("removed the page color");
            }

            var hex = WordColor.TryParse(color, out var parsed) ? parsed : throw new WordToolException($"\"{color}\" is not a color.");

            package.MainPart.Document.DocumentBackground = new DocumentBackground { Color = hex };

            // Word only shows a page color it is told to display.
            WordSchemaOrder.Set(package.GetOrCreateSettings(), new DisplayBackgroundShape());

            return Task.FromResult("page color #" + hex);
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}. preview_word shows it.";
    }
}
