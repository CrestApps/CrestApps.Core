using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Extracts a deck's text, notes, tables, charts, pictures, links or structure as data.
/// </summary>
internal sealed class ExtractPresentationContentTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.ExtractPresentationContent;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractPresentationContentTool"/> class.
    /// </summary>
    public ExtractPresentationContentTool()
        : base(
            TheName,
            "Extracts content from the presentation as data. 'text' gives every paragraph of every element with its slide, element #id and paragraph number: the input for rewriting, summarising, expanding or translating slides, which you then write back with update_slide_text so formatting is kept. 'notes' gives the speaker notes; 'tables' every table's rows; 'charts' every chart's type, categories and series; 'images' every picture with its alt text, size and crop; 'links' every hyperlink and whether it works; 'structure' every slide's layout and elements with positions.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "kind": { "type": "string", "enum": ["text", "notes", "tables", "charts", "images", "links", "structure"] },
                "slides": { "type": ["integer", "string", "array"], "description": "Optional slides, such as [2, 3] or \"4-8\". Omit for every slide." }
              },
              "required": ["kind"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Extracts the content.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slides = call.Slides(model, false);
        var kind = call.Arguments.String("kind", "content", "type")?.Trim().ToLowerInvariant() ?? "text";
        var selected = model.Slides.Where(slide => slides.Count == 0 || slides.Contains(slide.Number)).ToList();

        return kind switch
        {
            "text" => Text(model, slides),
            "notes" => Notes(selected),
            "tables" => Json(selected.SelectMany(slide => slide.AllElements().Where(element => element.Table is not null).Select(element => (JsonNode)new JsonObject
            {
                ["slide"] = slide.Number,
                ["element"] = element.Id,
                ["name"] = element.Name,
                ["rows"] = JsonSerializer.SerializeToNode(element.Table.ToText()),
            }))),
            "charts" => Json(selected.SelectMany(slide => slide.AllElements().Where(element => element.Chart is not null).Select(element => (JsonNode)new JsonObject
            {
                ["slide"] = slide.Number,
                ["element"] = element.Id,
                ["name"] = element.Name,
                ["kind"] = element.Chart.Kind + (element.Chart.Stacked ? (element.Chart.PercentStacked ? " (100% stacked)" : " (stacked)") : string.Empty),
                ["title"] = element.Chart.Title,
                ["categories"] = JsonSerializer.SerializeToNode(element.Chart.Categories),
                ["series"] = JsonSerializer.SerializeToNode(element.Chart.Series.Select(series => new { name = series.Name, values = series.Values, color = series.Color })),
                ["number_format"] = element.Chart.NumberFormat,
                ["editable_data"] = element.Chart.HasEmbeddedWorkbook,
            }))),
            "images" => Json(selected.SelectMany(slide => slide.AllElements().Where(element => element.Kind is PresentationElementKind.Picture or PresentationElementKind.Video or PresentationElementKind.Audio || element.Image is not null).Select(element => (JsonNode)new JsonObject
            {
                ["slide"] = slide.Number,
                ["element"] = element.Id,
                ["name"] = element.Name,
                ["kind"] = element.KindName,
                ["alt_text"] = element.AltText,
                ["decorative"] = element.IsDecorative,
                ["content_type"] = element.Image?.ContentType,
                ["pixels"] = element.Image?.PixelWidth is { } width ? $"{width}x{element.Image.PixelHeight}" : null,
                ["kilobytes"] = element.Image is null ? null : Math.Round(element.Image.ByteLength / 1024d),
                ["position"] = PresentationDescriber.Bounds(element.Bounds),
                ["missing"] = element.Image?.IsMissing == true ? true : null,
                ["media_url"] = element.MediaUrl,
            }))),
            "links" => Json(Links(selected)),
            "structure" => Json(selected.Select(slide => (JsonNode)new JsonObject
            {
                ["slide"] = slide.Number,
                ["layout"] = slide.LayoutName,
                ["title"] = slide.Title,
                ["hidden"] = slide.Hidden ? true : null,
                ["elements"] = new JsonArray(slide.Elements.Select(element => (JsonNode)new JsonObject
                {
                    ["id"] = element.Id,
                    ["kind"] = PresentationDescriber.Role(element),
                    ["name"] = element.Name,
                    ["position"] = PresentationDescriber.Bounds(element.Bounds),
                }).ToArray()),
            })),
            _ => throw new PresentationArgumentException("kind must be text, notes, tables, charts, images, links or structure."),
        };
    }

    private static string Text(PresentationModel model, IReadOnlyCollection<int> slides)
    {
        var builder = new StringBuilder();
        var current = 0;

        foreach (var entry in PresentationTextEntry.Collect(model, slides, includeNotes: true))
        {
            if (entry.Slide != current)
            {
                current = entry.Slide;
                builder.Append("## Slide ").AppendLine(current.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(entry.ElementId == 0 ? "notes" : "#" + entry.ElementId.ToString(CultureInfo.InvariantCulture) + " " + entry.Where).Append(": ").AppendLine(entry.Text.Replace("\n", " / ", StringComparison.Ordinal));
        }

        return builder.Length == 0 ? "The slides hold no text." : builder.ToString().TrimEnd();
    }

    private static string Notes(List<PresentationSlide> slides)
    {
        var builder = new StringBuilder();

        foreach (var slide in slides)
        {
            builder.Append("Slide ").Append(slide.Number.ToString(CultureInfo.InvariantCulture)).Append(" \"").Append(slide.Title).Append("\": ")
                .AppendLine(string.IsNullOrWhiteSpace(slide.Notes) ? "(no notes)" : slide.Notes.Replace("\n", " / ", StringComparison.Ordinal));
        }

        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<JsonNode> Links(List<PresentationSlide> slides)
    {
        foreach (var slide in slides)
        {
            foreach (var element in slide.AllElements())
            {
                if (element.Link is { } link)
                {
                    yield return new JsonObject { ["slide"] = slide.Number, ["element"] = element.Id, ["on"] = "element", ["target"] = link.Describe(), ["broken"] = link.IsBroken ? true : null };
                }

                foreach (var run in element.Text?.Paragraphs.SelectMany(paragraph => paragraph.Runs) ?? [])
                {
                    if (run.Link is { } textLink)
                    {
                        yield return new JsonObject { ["slide"] = slide.Number, ["element"] = element.Id, ["text"] = run.Text, ["target"] = textLink.Describe(), ["broken"] = textLink.IsBroken ? true : null };
                    }
                }
            }
        }
    }

    private static string Json(IEnumerable<JsonNode> items)
    {
        var array = new JsonArray(items.ToArray());

        return array.Count == 0 ? "[] (nothing of that kind in the selected slides)" : array.ToJsonString(_json);
    }
}
