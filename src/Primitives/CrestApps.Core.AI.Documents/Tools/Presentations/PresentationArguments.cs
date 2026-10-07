using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Reads the arguments of a presentation tool call into the edit specifications the engine applies.
/// </summary>
/// <remarks>
/// Models write the same thing many ways — a length as <c>72</c>, <c>"1in"</c> or <c>"10%"</c>; a bullet list
/// as an array, as Markdown, or as lines; a style nested under <c>style</c> or spread across the element — so
/// this reads all of them, and says precisely what it could not read rather than silently dropping it.
/// </remarks>
internal sealed partial class PresentationArguments
{
    private PresentationArguments(JsonObject root)
    {
        Root = root;
    }

    /// <summary>
    /// Gets the arguments as JSON.
    /// </summary>
    public JsonObject Root { get; }

    /// <summary>
    /// Reads a tool call's arguments.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The reader.</returns>
    public static PresentationArguments From(AIFunctionArguments arguments)
    {
        var root = new JsonObject();

        foreach (var (key, value) in arguments)
        {
            root[key] = ToNode(value);
        }

        return new PresentationArguments(root);
    }

    /// <summary>
    /// Creates a reader over a JSON object.
    /// </summary>
    /// <param name="root">The object.</param>
    /// <returns>The reader.</returns>
    public static PresentationArguments From(JsonObject root)
    {
        return new PresentationArguments(root ?? new JsonObject());
    }

    /// <summary>
    /// Reads a string argument.
    /// </summary>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public string String(params string[] names)
    {
        return ReadString(Find(Root, names));
    }

    /// <summary>
    /// Reads an integer argument.
    /// </summary>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public int? Int(params string[] names)
    {
        return ReadInt(Find(Root, names));
    }

    /// <summary>
    /// Reads a number argument.
    /// </summary>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public double? Number(params string[] names)
    {
        return ReadNumber(Find(Root, names));
    }

    /// <summary>
    /// Reads a boolean argument.
    /// </summary>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public bool? Bool(params string[] names)
    {
        return ReadBool(Find(Root, names));
    }

    /// <summary>
    /// Returns an argument as JSON.
    /// </summary>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    public JsonNode Node(params string[] names)
    {
        return Find(Root, names);
    }

    /// <summary>
    /// Returns an object argument.
    /// </summary>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The object, or <see langword="null"/>.</returns>
    public JsonObject Object(params string[] names)
    {
        return Find(Root, names) as JsonObject;
    }

    /// <summary>
    /// Returns an array argument; a single value is read as an array of one.
    /// </summary>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The items.</returns>
    public List<JsonNode> Array(params string[] names)
    {
        return Items(Find(Root, names));
    }

    /// <summary>
    /// Reads a list of strings; a comma-separated string is split.
    /// </summary>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The strings.</returns>
    public List<string> Strings(params string[] names)
    {
        return ReadStrings(Find(Root, names));
    }

    /// <summary>
    /// Reads slide numbers written as a number, an array, a range such as <c>"2-5, 7"</c>, or <c>"all"</c>.
    /// </summary>
    /// <param name="slideCount">The number of slides in the deck, which <c>all</c> and open ranges use.</param>
    /// <param name="names">The argument's name and its aliases.</param>
    /// <returns>The slide numbers, in order and without repeats; empty when the argument is missing.</returns>
    public List<int> Slides(int slideCount, params string[] names)
    {
        return ReadSlides(Find(Root, names), slideCount);
    }

    /// <summary>
    /// Reads slide numbers from JSON.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="slideCount">The number of slides in the deck.</param>
    /// <returns>The slide numbers.</returns>
    public static List<int> ReadSlides(JsonNode node, int slideCount)
    {
        var numbers = new List<int>();

        foreach (var item in Items(node))
        {
            if (ReadInt(item) is { } single)
            {
                numbers.Add(single);
                continue;
            }

            var text = ReadString(item);

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (text.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                numbers.AddRange(Enumerable.Range(1, slideCount));
                continue;
            }

            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cleaned = part.Replace("slide", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("s", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                var range = cleaned.Split(['-', '–'], StringSplitOptions.TrimEntries);

                if (range.Length == 2 && int.TryParse(range[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from))
                {
                    var to = int.TryParse(range[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end) ? end : slideCount;
                    numbers.AddRange(Enumerable.Range(from, Math.Max(0, to - from + 1)));
                }
                else if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    numbers.Add(number);
                }
                else
                {
                    throw new PresentationArgumentException($"\"{part}\" is not a slide number. Use numbers such as 3, a range such as \"2-5\", or \"all\".");
                }
            }
        }

        return numbers.Distinct().ToList();
    }

    /// <summary>
    /// Reads a slide to add, as <c>create_presentation</c> and <c>add_slide</c> describe one.
    /// </summary>
    /// <param name="slide">The slide object, or a string taken as its title.</param>
    /// <returns>The slide request.</returns>
    public static PresentationSlideRequest ReadSlide(JsonNode slide)
    {
        if (slide is JsonValue && ReadString(slide) is { } titleOnly)
        {
            return new PresentationSlideRequest { Edit = new AddSlideEdit { Title = titleOnly } };
        }

        if (slide is not JsonObject value)
        {
            throw new PresentationArgumentException("Each slide must be an object with at least a title, such as {\"title\": \"Overview\", \"body\": [\"First point\", \"Second point\"]}.");
        }

        var edit = new AddSlideEdit
        {
            Position = ReadInt(Find(value, "position", "index", "at")),
            Layout = ReadString(Find(value, "layout", "kind", "type")),
            Title = ReadString(Find(value, "title", "heading")),
            Subtitle = ReadString(Find(value, "subtitle", "sub_title", "tagline")),
            Body = ReadParagraphs(Find(value, "body", "bullets", "content", "text", "points", "left", "left_body")),
            SecondBody = ReadParagraphs(Find(value, "right_body", "second_body", "right", "right_content")),
            FirstHeading = ReadString(Find(value, "left_heading", "first_heading")),
            SecondHeading = ReadString(Find(value, "right_heading", "second_heading")),
            Notes = ReadString(Find(value, "notes", "speaker_notes")),
            Hidden = ReadBool(Find(value, "hidden")) == true,
        };

        var request = new PresentationSlideRequest { Edit = edit };
        var background = Find(value, "background");

        if (background is not null)
        {
            edit.Background = ReadBackground(background, out var backgroundImage);
            request.BackgroundImageDocument = backgroundImage;
        }

        foreach (var element in Items(Find(value, "elements", "shapes", "objects")))
        {
            request.Elements.Add(ReadElement(element));
        }

        return request;
    }

    /// <summary>
    /// Reads an element to insert.
    /// </summary>
    /// <param name="node">The element object.</param>
    /// <returns>The element request.</returns>
    public static PresentationElementRequest ReadElement(JsonNode node)
    {
        if (node is not JsonObject value)
        {
            throw new PresentationArgumentException("Each element must be an object with a \"type\", such as {\"type\": \"text\", \"text\": \"Hello\", \"position\": \"left_half\"}.");
        }

        var type = (ReadString(Find(value, "type", "kind", "element")) ?? Guess(value)).Trim().ToLowerInvariant().Replace('-', '_');
        var spec = new PresentationElementSpec
        {
            Name = ReadString(Find(value, "name")),
            Bounds = ReadBounds(value),
            Rotation = ReadNumber(Find(value, "rotation", "rotate", "angle")),
            FlipHorizontal = ReadBool(Find(value, "flip_horizontal", "flip_h")),
            FlipVertical = ReadBool(Find(value, "flip_vertical", "flip_v")),
            Paragraphs = ReadParagraphs(Find(value, "text", "paragraphs", "bullets", "label", "content")) ?? [],
            TextStyle = ReadTextStyle(value),
            ShapeStyle = ReadShapeStyle(value),
            Geometry = ReadString(Find(value, "shape", "geometry")),
            Icon = ReadString(Find(value, "icon", "icon_name")),
            ImageFit = ReadString(Find(value, "fit", "image_fit")),
            MediaUrl = ReadString(Find(value, "media_url", "video_url", "audio_url")),
            AltText = ReadString(Find(value, "alt_text", "alt", "description")),
            Decorative = ReadBool(Find(value, "decorative")),
            Link = ReadLink(Find(value, "link", "hyperlink", "url", "link_to_slide")),
            ConnectFrom = ReadString(Find(value, "from", "connect_from", "from_element")),
            ConnectTo = ReadString(Find(value, "to", "connect_to", "to_element")),
            StartArrow = ReadString(Find(value, "start_arrow", "arrow_start")),
            EndArrow = ReadString(Find(value, "end_arrow", "arrow_end", "arrow")),
            LineStartX = ReadLength(Find(value, "start_x", "x1")),
            LineStartY = ReadLength(Find(value, "start_y", "y1")),
            LineEndX = ReadLength(Find(value, "end_x", "x2")),
            LineEndY = ReadLength(Find(value, "end_y", "y2")),
        };

        var request = new PresentationElementRequest
        {
            Spec = spec,
            ImageDocument = ReadString(Find(value, "image_document_id", "image_document", "document_id", "image")),
            ImagePrompt = ReadString(Find(value, "image_prompt", "prompt", "generate")),
            TabularSql = ReadString(Find(value, "tabular_sql", "sql", "query")),
            CategoryColumn = ReadString(Find(value, "category_column")),
            ValueColumns = ReadStrings(Find(value, "value_columns", "series_columns")),
            MaxRows = Math.Clamp(ReadInt(Find(value, "max_rows", "limit")) ?? 25, 1, 60),
            Link = ReadBool(Find(value, "link_data", "keep_linked", "linked")) == true,
        };

        // Items make a diagram whatever the type says, unless the type is one that has no use for them.
        if (type is "diagram" or "smartart" or "smart_art" or "process" or "cycle" or "timeline" or "hierarchy" or "org_chart" or "pyramid" or "matrix" or "venn" or "cards" ||
            (Find(value, "items", "steps", "nodes", "stages") is not null && type is not ("text" or "bullets" or "table" or "chart" or "group")))
        {
            spec.Kind = PresentationElementSpecKind.Diagram;
            spec.Diagram = ReadDiagram(value, type is "diagram" or "smartart" or "smart_art" ? null : type);

            return request;
        }

        switch (type)
        {
            case "text" or "text_box" or "textbox" or "title" or "caption" or "label":
                spec.Kind = PresentationElementSpecKind.Text;
                break;

            case "bullets" or "bullet_list" or "list" or "numbered_list":
                spec.Kind = PresentationElementSpecKind.Text;

                foreach (var paragraph in spec.Paragraphs)
                {
                    paragraph.Bullet ??= type == "numbered_list" ? "number" : "bullet";
                }

                break;

            case "shape":
                spec.Kind = PresentationElementSpecKind.Shape;
                break;

            case "line" or "arrow" or "connector":
                spec.Kind = PresentationElementSpecKind.Line;

                if (type == "arrow")
                {
                    spec.EndArrow ??= "triangle";
                }

                break;

            case "table":
                spec.Kind = PresentationElementSpecKind.Table;
                spec.Table = ReadTable(value);
                break;

            case "chart" or "graph":
                spec.Kind = PresentationElementSpecKind.Chart;
                spec.Chart = ReadChart(Find(value, "chart") as JsonObject ?? value);
                break;

            case "image" or "picture" or "photo" or "logo":
                spec.Kind = PresentationElementSpecKind.Image;
                break;

            case "icon":
                spec.Kind = PresentationElementSpecKind.Icon;
                spec.Icon ??= ReadString(Find(value, "name"));
                break;

            case "video":
                spec.Kind = PresentationElementSpecKind.Video;
                break;

            case "audio" or "sound":
                spec.Kind = PresentationElementSpecKind.Audio;
                break;

            case "group":
                spec.Kind = PresentationElementSpecKind.Group;

                foreach (var child in Items(Find(value, "children", "elements")))
                {
                    var childRequest = ReadElement(child);
                    request.Children.Add(childRequest);
                    spec.Children.Add(childRequest.Spec);
                }

                break;

            default:
                if (PresentationShapeCatalog.TryGetShape(type, out _))
                {
                    spec.Kind = PresentationElementSpecKind.Shape;
                    spec.Geometry ??= type;
                    break;
                }

                throw new PresentationArgumentException($"\"{type}\" is not an element type. Use text, bullets, shape, line, arrow, table, chart, image, icon, video, audio or group.");
        }

        return request;
    }

    /// <summary>
    /// Reads a diagram: its kind, its items as strings or objects, the text in its middle and its colours.
    /// </summary>
    /// <param name="value">The object holding the diagram's properties.</param>
    /// <param name="kind">The kind the element's type already gave, if any.</param>
    /// <returns>The diagram.</returns>
    public static PresentationDiagramSpec ReadDiagram(JsonObject value, string kind)
    {
        var diagram = new PresentationDiagramSpec
        {
            Kind = PresentationDiagramComposer.Normalize(ReadString(Find(value, "diagram_type", "diagram_kind", "layout")) ?? kind),
            CenterText = ReadString(Find(value, "center_text", "center", "hub", "middle")),
            Colors = Find(value, "colors", "palette") is { } colors ? ReadStrings(colors) : [],
        };

        foreach (var item in Items(Find(value, "items", "steps", "nodes", "stages", "levels")))
        {
            if (item is JsonObject entry)
            {
                diagram.Items.Add(new PresentationDiagramItem
                {
                    Text = ReadString(Find(entry, "text", "label", "title", "name", "heading")),
                    Detail = ReadString(Find(entry, "detail", "description", "subtitle", "body", "date")),
                    Parent = ReadString(Find(entry, "parent", "reports_to", "manager", "under")),
                    Color = ReadString(Find(entry, "color", "colour", "fill")),
                });
            }
            else if (ReadString(item) is { } text)
            {
                diagram.Items.Add(new PresentationDiagramItem { Text = text });
            }
        }

        if (!PresentationDiagramComposer.Kinds.Contains(diagram.Kind))
        {
            throw new PresentationArgumentException($"\"{diagram.Kind}\" is not a diagram. Use one of: {string.Join(", ", PresentationDiagramComposer.Kinds)}.");
        }

        return diagram;
    }

    private static string Guess(JsonObject value)
    {
        if (Find(value, "rows") is not null)
        {
            return "table";
        }

        if (Find(value, "items", "steps", "nodes", "diagram_type") is not null)
        {
            return "diagram";
        }

        if (Find(value, "series", "chart") is not null)
        {
            return "chart";
        }

        if (Find(value, "image_document_id", "image", "image_prompt") is not null)
        {
            return "image";
        }

        if (Find(value, "icon") is not null)
        {
            return "icon";
        }

        if (Find(value, "shape") is not null)
        {
            return "shape";
        }

        return "text";
    }

    /// <summary>
    /// Reads a table from an element or tool argument.
    /// </summary>
    /// <param name="value">The object holding the table's properties.</param>
    /// <returns>The table.</returns>
    public static PresentationTableSpec ReadTable(JsonObject value)
    {
        var table = new PresentationTableSpec
        {
            HeaderRow = ReadBool(Find(value, "header", "header_row", "has_header")) ?? true,
            ColumnWidths = Items(Find(value, "column_widths", "widths")).Select(ReadLength).ToList(),
            ColumnAlignments = ReadStrings(Find(value, "column_alignments", "alignments")),
            Style = ReadTableStyle(Find(value, "table_style", "style") as JsonObject ?? value),
        };

        foreach (var row in Items(Find(value, "rows", "data", "cells")))
        {
            table.Rows.Add(Items(row).Select(cell => ReadString(cell) ?? string.Empty).ToList());
        }

        if (Find(value, "columns", "headers") is { } headers && table.Rows.Count > 0 && table.HeaderRow)
        {
            // A header given separately goes first.
            table.Rows.Insert(0, ReadStrings(headers));
        }

        return table;
    }

    /// <summary>
    /// Reads a chart from an element or tool argument.
    /// </summary>
    /// <param name="value">The object holding the chart's properties.</param>
    /// <returns>The chart.</returns>
    public static PresentationChartSpec ReadChart(JsonObject value)
    {
        var chart = new PresentationChartSpec
        {
            Kind = ReadString(Find(value, "chart_type", "chart_kind", "kind", "type")) is { } kind && kind is not "chart" and not "graph" ? kind : null,
            Title = ReadString(Find(value, "title", "chart_title")),
            CategoryAxisTitle = ReadString(Find(value, "category_axis_title", "x_axis_title", "x_title")),
            ValueAxisTitle = ReadString(Find(value, "value_axis_title", "y_axis_title", "y_title")),
            Style = ReadChartStyle(Find(value, "chart_style", "style") as JsonObject ?? value),
        };

        if (Find(value, "categories", "labels") is { } categories)
        {
            chart.Categories = ReadStrings(categories);
        }

        if (Find(value, "series", "datasets", "data") is { } series)
        {
            chart.Series = [];

            foreach (var item in Items(series))
            {
                if (item is JsonObject seriesObject)
                {
                    chart.Series.Add(new PresentationChartSeriesSpec
                    {
                        Name = ReadString(Find(seriesObject, "name", "label", "title")),
                        Values = Items(Find(seriesObject, "values", "data", "points")).Select(ReadNumber).ToList(),
                        XValues = Find(seriesObject, "x_values", "x") is { } x ? Items(x).Select(ReadNumber).ToList() : null,
                        Color = ReadString(Find(seriesObject, "color", "colour", "fill")),
                    });
                }
                else if (item is JsonArray numbers)
                {
                    // A bare list of numbers is a single unnamed series.
                    chart.Series.Add(new PresentationChartSeriesSpec { Values = numbers.Select(ReadNumber).ToList() });
                }
            }

            if (chart.Series.Count == 0 && Items(series).All(item => ReadNumber(item) is not null))
            {
                chart.Series.Add(new PresentationChartSeriesSpec { Values = Items(series).Select(ReadNumber).ToList() });
            }
        }

        return chart;
    }

    /// <summary>
    /// Reads where an element goes from its <c>position</c>, <c>x</c>, <c>y</c>, <c>width</c> and
    /// <c>height</c>.
    /// </summary>
    /// <param name="value">The object holding the properties.</param>
    /// <returns>The bounds, or <see langword="null"/> when none are given.</returns>
    public static PresentationBoundsSpec ReadBounds(JsonObject value)
    {
        var nested = Find(value, "bounds", "box", "frame") as JsonObject ?? value;
        var bounds = new PresentationBoundsSpec
        {
            X = ReadLength(Find(nested, "x", "left")),
            Y = ReadLength(Find(nested, "y", "top")),
            Width = ReadLength(Find(nested, "width", "w")),
            Height = ReadLength(Find(nested, "height", "h")),
            Placement = ReadString(Find(nested, "position", "placement", "area", "region")),
        };

        if (Find(nested, "size") is { } size && bounds.Width is null && bounds.Height is null && ReadLength(size) is { } square)
        {
            bounds.Width = square;
            bounds.Height = square;
        }

        return bounds.IsEmpty ? null : bounds;
    }

    /// <summary>
    /// Reads a length written as points or with a unit.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The length, or <see langword="null"/>.</returns>
    public static PresentationLength? ReadLength(JsonNode node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value &&
            (value.TryGetValue<double>(out _) || (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number)) &&
            ReadNumber(node) is { } points)
        {
            return PresentationLength.FromPoints(points);
        }

        var text = ReadString(node);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (PresentationLength.TryParse(text, out var length))
        {
            return length;
        }

        throw new PresentationArgumentException($"\"{text}\" is not a length. Use points (72), or a number with a unit: 1in, 2.5cm, 20mm, 96px, 12pt or 50%.");
    }

    /// <summary>
    /// Reads paragraphs written as text (one paragraph per line, with Markdown bullets), an array of strings,
    /// or an array of paragraph objects.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The paragraphs, or <see langword="null"/> when the node is missing.</returns>
    public static List<PresentationParagraphSpec> ReadParagraphs(JsonNode node)
    {
        if (node is null)
        {
            return null;
        }

        var paragraphs = new List<PresentationParagraphSpec>();

        if (node is JsonValue)
        {
            var text = ReadString(node) ?? string.Empty;

            foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                paragraphs.Add(ParseLine(line, null));
            }

            // Leading and trailing blank lines are formatting, not content.
            while (paragraphs.Count > 1 && paragraphs[^1].Text.Length == 0)
            {
                paragraphs.RemoveAt(paragraphs.Count - 1);
            }

            return paragraphs;
        }

        foreach (var item in Items(node))
        {
            if (item is JsonObject paragraph)
            {
                var text = ReadString(Find(paragraph, "text", "content", "value")) ?? string.Empty;
                var level = ReadInt(Find(paragraph, "level", "indent"));
                var spec = ParseLine(text, level is null ? null : Math.Max(0, level.Value - 1));
                spec.Bullet = ReadString(Find(paragraph, "bullet", "marker")) ?? spec.Bullet;
                spec.Style = PresentationTextStyle.Combine(spec.Style, ReadTextStyle(paragraph));

                if (Find(paragraph, "runs") is { } runs)
                {
                    spec.Runs = Items(runs).Select(ReadRun).ToList();
                }

                if (ReadLink(Find(paragraph, "link", "url", "hyperlink")) is { } link)
                {
                    foreach (var run in spec.Runs)
                    {
                        run.Link ??= link;
                    }
                }

                paragraphs.Add(spec);
            }
            else if (item is JsonArray nested)
            {
                // A nested array is a sub-list of the paragraph before it.
                var level = paragraphs.Count == 0 ? 1 : paragraphs[^1].Level + 1;

                foreach (var child in nested)
                {
                    var parsed = ParseLine(ReadString(child) ?? string.Empty, level);
                    parsed.Bullet ??= "auto";
                    paragraphs.Add(parsed);
                }
            }
            else
            {
                paragraphs.Add(ParseLine(ReadString(item) ?? string.Empty, null));
            }
        }

        return paragraphs;
    }

    private static PresentationRunSpec ReadRun(JsonNode node)
    {
        if (node is JsonObject run)
        {
            return new PresentationRunSpec
            {
                Text = ReadString(Find(run, "text")) ?? string.Empty,
                Style = ReadTextStyle(run),
                Link = ReadLink(Find(run, "link", "url", "hyperlink")),
            };
        }

        return new PresentationRunSpec { Text = ReadString(node) ?? string.Empty };
    }

    /// <summary>
    /// Reads one line of paragraph text: its Markdown bullet or number and its indent set the level and
    /// bullet, and <c>**bold**</c>, <c>*italic*</c> and <c>[links](https://…)</c> become styled runs.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="level">The level, when the caller gave one; otherwise taken from the indent.</param>
    /// <returns>The paragraph.</returns>
    public static PresentationParagraphSpec ParseLine(string line, int? level)
    {
        var text = line ?? string.Empty;
        var indent = 0;

        while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t'))
        {
            indent += text[indent] == '\t' ? 4 : 1;
        }

        text = text.TrimStart(' ', '\t');
        string bullet = null;

        var marker = BulletExpression().Match(text);

        if (marker.Success)
        {
            text = text[marker.Length..];
            bullet = char.IsDigit(marker.Value[0]) ? "number" : "auto";
        }

        var paragraph = new PresentationParagraphSpec
        {
            Level = Math.Clamp(level ?? (indent / 2), 0, 8),
            Bullet = bullet,
            Runs = ParseInline(text),
        };

        return paragraph;
    }

    private static List<PresentationRunSpec> ParseInline(string text)
    {
        var runs = new List<PresentationRunSpec>();
        var position = 0;

        foreach (Match match in InlineExpression().Matches(text))
        {
            if (match.Index > position)
            {
                runs.Add(new PresentationRunSpec { Text = text[position..match.Index] });
            }

            if (match.Groups["bold"].Success)
            {
                runs.Add(new PresentationRunSpec { Text = match.Groups["bold"].Value, Style = new PresentationTextStyle { Bold = true } });
            }
            else if (match.Groups["italic"].Success)
            {
                runs.Add(new PresentationRunSpec { Text = match.Groups["italic"].Value, Style = new PresentationTextStyle { Italic = true } });
            }
            else if (match.Groups["label"].Success)
            {
                runs.Add(new PresentationRunSpec { Text = match.Groups["label"].Value, Link = ReadLink(JsonValue.Create(match.Groups["target"].Value)) });
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length || runs.Count == 0)
        {
            runs.Add(new PresentationRunSpec { Text = text[position..] });
        }

        return runs;
    }

    /// <summary>
    /// Reads a link written as an address, as <c>slide 3</c> or <c>#3</c>, as a jump such as <c>next</c>, or
    /// as an object.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The link, or <see langword="null"/>.</returns>
    public static PresentationLinkSpec ReadLink(JsonNode node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject value)
        {
            return new PresentationLinkSpec
            {
                Url = ReadString(Find(value, "url", "href", "address")),
                SlideNumber = ReadInt(Find(value, "slide", "slide_number")),
                Action = ReadString(Find(value, "action", "jump")),
                Tooltip = ReadString(Find(value, "tooltip", "title")),
                Remove = ReadBool(Find(value, "remove")) == true,
            };
        }

        if (ReadInt(node) is { } slideNumber && node is JsonValue)
        {
            return new PresentationLinkSpec { SlideNumber = slideNumber };
        }

        var text = ReadString(node)?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Equals("none", StringComparison.OrdinalIgnoreCase) || text.Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            return new PresentationLinkSpec { Remove = true };
        }

        var slide = SlideLinkExpression().Match(text);

        if (slide.Success)
        {
            return new PresentationLinkSpec { SlideNumber = int.Parse(slide.Groups["number"].Value, CultureInfo.InvariantCulture) };
        }

        if (text.ToLowerInvariant() is "next" or "previous" or "prev" or "first" or "last" or "end")
        {
            return new PresentationLinkSpec { Action = text };
        }

        return new PresentationLinkSpec { Url = text };
    }

    /// <summary>
    /// Reads a text style from an object's own properties or its <c>style</c>, <c>text_style</c> or
    /// <c>font</c> object.
    /// </summary>
    /// <param name="value">The object.</param>
    /// <returns>The style, or <see langword="null"/> when it sets nothing.</returns>
    public static PresentationTextStyle ReadTextStyle(JsonObject value)
    {
        if (value is null)
        {
            return null;
        }

        var style = ReadTextStyleCore(value, isFont: false);

        foreach (var name in new[] { "style", "text_style", "font_style" })
        {
            if (Find(value, name) is JsonObject nested)
            {
                style = PresentationTextStyle.Combine(style, ReadTextStyleCore(nested, isFont: false));
            }
        }

        if (Find(value, "font") is JsonObject fontObject)
        {
            style = PresentationTextStyle.Combine(style, ReadTextStyleCore(fontObject, isFont: true));
        }

        return style is null || style.IsEmpty ? null : style;
    }

    private static PresentationTextStyle ReadTextStyleCore(JsonObject value, bool isFont)
    {
        // Inside a "font" object the family may be called "name"; anywhere else "name" names the element.
        var family = isFont ? Find(value, "family", "name", "typeface", "font") : Find(value, "font", "font_family", "typeface");

        return new PresentationTextStyle
        {
            Font = family is JsonValue font ? ReadString(font) : null,
            Size = ReadNumber(Find(value, "font_size", "size", "text_size")),
            Bold = ReadBool(Find(value, "bold")),
            Italic = ReadBool(Find(value, "italic")),
            Underline = ReadBool(Find(value, "underline")),
            Strikethrough = ReadBool(Find(value, "strikethrough", "strike")),
            Color = ReadString(Find(value, "color", "text_color", "font_color", "colour")),
            Highlight = ReadString(Find(value, "highlight")),
            Alignment = ReadString(Find(value, "align", "alignment", "text_align", "horizontal_alignment")),
            VerticalAlignment = ReadString(Find(value, "vertical_align", "vertical_alignment", "valign", "anchor")),
            LineSpacing = ReadNumber(Find(value, "line_spacing")),
            SpaceBefore = ReadNumber(Find(value, "space_before", "spacing_before")),
            SpaceAfter = ReadNumber(Find(value, "space_after", "spacing_after")),
            Capitalization = ReadString(Find(value, "caps", "capitalization", "case")),
            AutoFit = ReadString(Find(value, "autofit", "auto_fit")),
            Wrap = ReadBool(Find(value, "wrap", "word_wrap")),
        };
    }

    /// <summary>
    /// Reads a shape style from an object's own properties or its <c>shape_style</c> object.
    /// </summary>
    /// <param name="value">The object.</param>
    /// <returns>The style, or <see langword="null"/> when it sets nothing.</returns>
    public static PresentationShapeStyle ReadShapeStyle(JsonObject value)
    {
        if (value is null)
        {
            return null;
        }

        var source = Find(value, "shape_style", "fill_style") as JsonObject;
        var style = ReadShapeStyleCore(value);

        if (source is not null)
        {
            style = PresentationShapeStyle.Combine(style, ReadShapeStyleCore(source));
        }

        return style is null || style.IsEmpty ? null : style;
    }

    private static PresentationShapeStyle ReadShapeStyleCore(JsonObject value)
    {
        var gradient = Find(value, "gradient", "gradient_colors");

        return new PresentationShapeStyle
        {
            Fill = ReadString(Find(value, "fill", "fill_color", "background", "background_color", "shape_color")) is { } fill && gradient is null ? fill : null,
            GradientColors = gradient is null ? null : ReadStrings(gradient),
            GradientAngle = ReadNumber(Find(value, "gradient_angle")),
            Transparency = ReadNumber(Find(value, "transparency", "fill_transparency")),
            OutlineColor = ReadString(Find(value, "outline", "outline_color", "border_color", "line_color", "stroke")),
            OutlineWidth = ReadNumber(Find(value, "outline_width", "border_width", "line_width", "stroke_width")),
            OutlineDash = ReadString(Find(value, "outline_dash", "dash", "line_dash", "border_style")),
            Shadow = ReadBool(Find(value, "shadow")),
            CornerRadius = ReadNumber(Find(value, "corner_radius", "rounding")),
        };
    }

    /// <summary>
    /// Reads a table style.
    /// </summary>
    /// <param name="value">The object.</param>
    /// <returns>The style, or <see langword="null"/> when it sets nothing.</returns>
    public static PresentationTableStyle ReadTableStyle(JsonObject value)
    {
        if (value is null)
        {
            return null;
        }

        var style = new PresentationTableStyle
        {
            HeaderFill = ReadString(Find(value, "header_fill", "header_color", "header_background")),
            HeaderTextColor = ReadString(Find(value, "header_text_color", "header_font_color")),
            HeaderBold = ReadBool(Find(value, "header_bold")),
            BodyFill = ReadString(Find(value, "body_fill", "row_fill", "cell_fill")),
            BandFill = ReadString(Find(value, "band_fill", "banded_fill", "alternate_fill", "stripe_color")),
            TextColor = ReadString(Find(value, "text_color", "body_text_color", "font_color")),
            BorderColor = ReadString(Find(value, "border_color", "grid_color", "table_border_color")),
            BorderWidth = ReadNumber(Find(value, "border_width")),
            Font = ReadString(Find(value, "table_font", "font")) is { } font && Find(value, "font") is not JsonObject ? font : null,
            FontSize = ReadNumber(Find(value, "font_size", "text_size", "size")),
            FirstColumnBold = ReadBool(Find(value, "first_column_bold", "bold_first_column")),
            TotalRow = ReadBool(Find(value, "total_row", "last_row_total")),
            BandedRows = ReadBool(Find(value, "banded_rows", "banded", "stripes")),
        };

        return style.IsEmpty ? null : style;
    }

    /// <summary>
    /// Reads a chart style.
    /// </summary>
    /// <param name="value">The object.</param>
    /// <returns>The style, or <see langword="null"/> when it sets nothing.</returns>
    public static PresentationChartStyle ReadChartStyle(JsonObject value)
    {
        if (value is null)
        {
            return null;
        }

        var style = new PresentationChartStyle
        {
            Colors = Find(value, "colors", "colours", "palette", "series_colors") is { } colors ? ReadStrings(colors) : null,
            Legend = Find(value, "legend") is { } legend ? (ReadBool(legend) is { } show ? (show ? "bottom" : "none") : ReadString(legend)) : null,
            DataLabels = ReadBool(Find(value, "data_labels", "show_values", "labels")),
            Gridlines = ReadBool(Find(value, "gridlines", "grid")),
            Font = ReadString(Find(value, "chart_font", "font")) is { } font && Find(value, "font") is not JsonObject ? font : null,
            FontSize = ReadNumber(Find(value, "chart_font_size", "font_size")),
            TextColor = ReadString(Find(value, "chart_text_color", "text_color")),
            NumberFormat = ReadString(Find(value, "number_format", "format")),
        };

        return style.IsEmpty ? null : style;
    }

    /// <summary>
    /// Reads a background: a colour, a list of gradient colours, or an object with <c>color</c>,
    /// <c>gradient</c>, <c>image_document_id</c> or <c>reset</c>.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="imageDocument">The uploaded picture the background uses, when it names one.</param>
    /// <returns>The background.</returns>
    public static PresentationBackgroundSpec ReadBackground(JsonNode node, out string imageDocument)
    {
        imageDocument = null;

        if (node is JsonArray)
        {
            return new PresentationBackgroundSpec { GradientColors = ReadStrings(node) };
        }

        if (node is not JsonObject value)
        {
            var text = ReadString(node);

            return text is null ? null : new PresentationBackgroundSpec
            {
                Reset = text.Equals("reset", StringComparison.OrdinalIgnoreCase) || text.Equals("default", StringComparison.OrdinalIgnoreCase),
                Color = text.Equals("reset", StringComparison.OrdinalIgnoreCase) || text.Equals("default", StringComparison.OrdinalIgnoreCase) ? null : text,
            };
        }

        imageDocument = ReadString(Find(value, "image_document_id", "image", "picture"));

        return new PresentationBackgroundSpec
        {
            Color = ReadString(Find(value, "color", "colour", "fill")),
            GradientColors = Find(value, "gradient", "gradient_colors") is { } gradient ? ReadStrings(gradient) : null,
            GradientAngle = ReadNumber(Find(value, "gradient_angle", "angle")),
            ImageTransparency = ReadNumber(Find(value, "image_transparency", "transparency")),
            Reset = ReadBool(Find(value, "reset")) == true,
        };
    }

    /// <summary>
    /// Finds a property by any of its names, ignoring case and separators.
    /// </summary>
    /// <param name="value">The object.</param>
    /// <param name="names">The property's names.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static JsonNode Find(JsonObject value, params string[] names)
    {
        if (value is null)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (value.TryGetPropertyValue(name, out var direct) && direct is not null)
            {
                return direct;
            }
        }

        foreach (var name in names)
        {
            var key = Normalize(name);

            foreach (var (property, node) in value)
            {
                if (node is not null && Normalize(property) == key)
                {
                    return node;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the items of an array node; any other node is one item.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The items.</returns>
    public static List<JsonNode> Items(JsonNode node)
    {
        if (node is null)
        {
            return [];
        }

        if (node is JsonArray array)
        {
            return array.Where(item => item is not null).ToList();
        }

        // A string holding a JSON array, which some models send for array arguments.
        if (node is JsonValue && ReadString(node) is { } text && text.TrimStart().StartsWith('['))
        {
            try
            {
                if (JsonNode.Parse(text) is JsonArray parsed)
                {
                    return parsed.Where(item => item is not null).ToList();
                }
            }
            catch (JsonException)
            {
                // Not JSON after all; it is read as a single value.
            }
        }

        return [node];
    }

    /// <summary>
    /// Reads a string from a node; numbers and booleans are written out.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    public static string ReadString(JsonNode node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
        }

        return value.ToString();
    }

    /// <summary>
    /// Reads strings from a node: an array, or a comma-separated string.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The strings.</returns>
    public static List<string> ReadStrings(JsonNode node)
    {
        if (node is JsonValue && ReadString(node) is { } text && !text.TrimStart().StartsWith('['))
        {
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        return Items(node).Select(item => ReadString(item) ?? string.Empty).ToList();
    }

    /// <summary>
    /// Reads an integer from a node.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static int? ReadInt(JsonNode node)
    {
        var number = ReadNumber(node);

        return number is null ? null : (int)Math.Round(Math.Clamp(number.Value, int.MinValue, int.MaxValue));
    }

    /// <summary>
    /// Reads a number from a node; a string such as <c>"1,234.5"</c>, <c>"$12"</c> or <c>"45%"</c> is read too.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static double? ReadNumber(JsonNode node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var fromElement))
        {
            return fromElement;
        }

        var text = ReadString(node)?.Trim();

        if (string.IsNullOrEmpty(text) || text.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var negative = text.StartsWith('(') && text.EndsWith(')');
        var cleaned = text.Trim('(', ')').Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Trim('$', '€', '£', '¥', '%');

        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return negative ? -parsed : parsed;
        }

        return null;
    }

    /// <summary>
    /// Reads a boolean from a node; <c>"yes"</c>, <c>"true"</c> and <c>1</c> are true.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static bool? ReadBool(JsonNode node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            return flag;
        }

        if (value.TryGetValue<JsonElement>(out var element))
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return element.GetBoolean();
            }
        }

        return ReadString(node)?.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "1" or "on" or "show" => true,
            "false" or "no" or "0" or "off" or "hide" => false,
            _ => null,
        };
    }

    private static string Normalize(string name)
    {
        return name.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static JsonNode ToNode(object value)
    {
        return value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            int integer => JsonValue.Create(integer),
            long number => JsonValue.Create(number),
            double real => JsonValue.Create(real),
            float single => JsonValue.Create(single),
            decimal money => JsonValue.Create(money),
            _ => JsonSerializer.SerializeToNode(value),
        };
    }

    [GeneratedRegex(@"^([-*•▪–]|\d+[.)])\s+")]
    private static partial Regex BulletExpression();

    [GeneratedRegex(@"\*\*(?<bold>[^*]+?)\*\*|(?<![\w*])\*(?<italic>[^*\s][^*]*?)\*(?![\w*])|\[(?<label>[^\]]+)\]\((?<target>[^)\s]+)\)")]
    private static partial Regex InlineExpression();

    [GeneratedRegex(@"^(#|slide\s*#?\s*)(?<number>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SlideLinkExpression();
}
