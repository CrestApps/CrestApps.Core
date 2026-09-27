using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Lists a PDF's layers (optional content groups) and sets which of them are shown when it opens.
/// </summary>
internal sealed class ManagePdfLayersTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ManagePdfLayers;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "action": {
              "type": "string",
              "enum": ["list", "show", "hide"],
              "description": "list (default) shows the layers without saving; show or hide sets whether the named layers are visible when the document opens."
            },
            "layers": {
              "type": "array",
              "items": { "type": "string" },
              "description": "For show and hide: the layer names, as list shows them, or [\"all\"]."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagePdfLayersTool"/> class.
    /// </summary>
    public ManagePdfLayersTool()
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
    public override string Description => "Lists a PDF's layers (optional content): name, whether each is shown when the document opens, whether it is locked, and the pages that use it; or shows or hides layers by default and saves a working copy. Hiding only changes the default view — the layer's content stays in the file and readers can switch it back on, so use redact_pdf or sanitize_pdf to remove content permanently.";

    /// <summary>
    /// Carries out the layer request.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var action = (arguments.GetString("action") ?? "list").Trim().ToLowerInvariant();
        var password = arguments.GetString("password");

        if (action == "list")
        {
            var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
            var bytes = await context.ReadPdfAsync(source, cancellationToken);

            using var document = PdfFiles.OpenForImport(bytes, password);

            return Describe(source, PdfLayers.List(document));
        }

        if (action is not ("show" or "hide"))
        {
            throw new PdfToolException($"'{action}' is not an action. Use list, show or hide.");
        }

        var names = arguments.GetStrings("layers");

        if (names.Count == 0)
        {
            throw new PdfToolException($"Pass 'layers': the names of the layers to {action}, as action 'list' shows them, or [\"all\"].");
        }

        var visible = action == "show";

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var document = PdfFiles.OpenForEditing(bytes, password);

            var layers = PdfLayers.List(document);

            if (layers.Count == 0)
            {
                throw new PdfToolException($"{PdfPropertiesToolText.Capitalize(target.Describe())} has no layers (optional content).");
            }

            var selected = Select(layers, names);
            var changing = selected.Where(layer => layer.Visible != visible).ToList();

            if (changing.Count == 0)
            {
                throw new PdfToolException($"Nothing changed: {Join(selected)} {(selected.Count == 1 ? "is" : "are")} already {(visible ? "shown" : "hidden")} when the document opens.");
            }

            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document);

            PdfLayers.SetVisibility(document, changing, visible);

            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, $"{(visible ? "Showed" : "Hid")} layers {Join(changing)}", cancellationToken);
            var answer = new StringBuilder();

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine($"{(visible ? "Showed" : "Hid")} {Join(changing)} by default when the document opens.");

            var locked = changing.Where(layer => layer.Locked).ToList();

            if (locked.Count > 0)
            {
                answer.AppendLine($"{Join(locked)} {(locked.Count == 1 ? "is" : "are")} locked, so readers cannot switch {(locked.Count == 1 ? "it" : "them")} in a viewer.");
            }

            answer.AppendLine(visible
                ? "Viewers show these layers on opening."
                : "The hidden content is still in the file and readers can switch it back on in a viewer's layers panel; this tool does not remove layer content permanently.");

            PdfPropertiesToolText.AppendNotes(answer, signatures);

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static List<PdfLayer> Select(List<PdfLayer> layers, List<string> names)
    {
        if (names.Any(name => string.Equals(name, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return layers;
        }

        var selected = new List<PdfLayer>();

        foreach (var name in names)
        {
            var matches = layers.Where(layer => string.Equals(layer.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0)
            {
                throw new PdfToolException($"There is no layer named \"{name}\". Layers: {Join(layers)}.");
            }

            selected.AddRange(matches.Where(match => !selected.Contains(match)));
        }

        return selected;
    }

    private static string Describe(PdfSource source, List<PdfLayer> layers)
    {
        if (layers.Count == 0)
        {
            return $"{PdfPropertiesToolText.Capitalize(source.Describe())} has no layers (optional content).";
        }

        var answer = new StringBuilder();

        answer.AppendLine($"{PdfPropertiesToolText.Capitalize(source.Describe())} has {layers.Count} layer(s):");

        foreach (var layer in layers.Take(200))
        {
            answer.Append("- \"").Append(layer.Name).Append("\": ")
                .Append(layer.Visible ? "shown" : "hidden")
                .Append(" when the document opens");

            if (layer.Locked)
            {
                answer.Append(", locked");
            }

            if (!string.IsNullOrEmpty(layer.Intent) && !string.Equals(layer.Intent, "View", StringComparison.Ordinal))
            {
                answer.Append(", intent ").Append(layer.Intent);
            }

            answer.Append(layer.Pages.Count == 0
                ? ", not referenced by any page's resources"
                : ", used on pages " + PdfPageRange.Describe(layer.Pages));

            answer.AppendLine();
        }

        return answer.ToString().TrimEnd();
    }

    private static string Join(List<PdfLayer> layers)
    {
        return string.Join(", ", layers.Take(30).Select(layer => $"\"{layer.Name}\"")) + (layers.Count > 30 ? " and more" : string.Empty);
    }
}
