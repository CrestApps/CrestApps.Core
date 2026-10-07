using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Formatting;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Sets the page size, orientation, margins, columns, vertical alignment, page borders and page numbering of sections.
/// </summary>
internal sealed class SetWordPageLayoutTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.SetWordPageLayout;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            {{WordSectionSelection.Schema}},
            "size": { "type": "string", "description": "letter, legal, tabloid, executive, A3, A4, A5, B5." },
            "orientation": { "type": "string", "enum": ["portrait", "landscape"] },
            "margins": { "type": ["number", "string", "object"], "description": "One length for every side, or { top, right, bottom, left, header, footer }. Points or with units (1in, 2.5cm)." },
            "gutter": { "type": ["number", "string"], "description": "Extra binding margin." },
            "columns": { "type": "integer" },
            "column_spacing": { "type": ["number", "string"] },
            "column_separator": { "type": "boolean", "description": "Draw a line between columns." },
            "vertical_alignment": { "type": "string", "enum": ["top", "center", "bottom", "both"], "description": "How text sits on a page that is not full: center for a cover page." },
            "page_borders": {
              "type": "object",
              "description": "A border around the pages.",
              "properties": {
                "style": { "type": "string", "enum": ["single", "double", "dotted", "dashed", "thick", "triple", "none"], "description": "none removes the border." },
                "color": { "type": "string" },
                "width": { "type": "number", "description": "Line width in points. Default 1." },
                "space": { "type": "number", "description": "Distance from the page edge in points. Default 24." },
                "apply_to": { "type": "string", "enum": ["all_pages", "first_page", "not_first_page"] }
              }
            },
            "number_format": { "type": "string", "enum": ["decimal", "lower_roman", "upper_roman", "lower_letter", "upper_letter"], "description": "How page numbers are written in these sections, such as lower_roman for front matter." },
            "start_at": { "type": "integer", "description": "Restart page numbering at this number." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetWordPageLayoutTool"/> class.
    /// </summary>
    public SetWordPageLayoutTool()
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
    public override string Description => "Sets the page layout of a Word document, for all sections or chosen ones: paper 'size', 'orientation', 'margins', binding 'gutter', text 'columns' with spacing and a separator line, 'vertical_alignment' of the text on the page, 'page_borders', and page numbering ('number_format', 'start_at'). Page numbers themselves go in a footer with add_word_header_footer. To lay out only part of a document differently, first split it with add_word_section.";

    /// <summary>
    /// Sets the layout.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var setup = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var name in new[] { "size", "orientation", "margins", "columns", "column_spacing", "column_separator" })
        {
            if (arguments.TryGetElement(name, out var value))
            {
                setup[name] = value;
            }
        }

        var gutter = arguments.GetLength("gutter");
        var vertical = arguments.GetString("vertical_alignment");
        var hasBorders = arguments.TryGetObject("page_borders", out var borders);
        var numberFormat = ReadNumberFormat(arguments.GetString("number_format"));
        var startAt = arguments.GetInt("start_at");

        if (setup.Count == 0 && gutter is null && vertical is null && !hasBorders && numberFormat is null && startAt is null)
        {
            throw new WordToolException("Say what to change: size, orientation, margins, gutter, columns, vertical_alignment, page_borders, number_format or start_at.");
        }

        var element = JsonSerializer.SerializeToElement(setup);

        var (lines, document) = await context.EditAsync(arguments.Document(), "Changed the page layout", edit =>
        {
            var report = new List<string>();

            foreach (var (section, number) in WordSectionSelection.Read(edit.Package, arguments))
            {
                WordSetupReader.ApplyPageSetup(section, element);

                if (gutter is not null)
                {
                    var margins = WordSections.Margins(section);

                    WordPageSizes.SetMargins(section, margins.Top, margins.Right, margins.Bottom, margins.Left, margins.Header, margins.Footer);
                    section.GetFirstChild<PageMargin>().Gutter = (uint)WordUnits.ToTwips(Math.Clamp(gutter.Value, 0, 144));
                }

                if (vertical is not null)
                {
                    WordSchemaOrder.Set(section, new VerticalTextAlignmentOnPage
                    {
                        Val = vertical switch
                        {
                            "center" => VerticalJustificationValues.Center,
                            "bottom" => VerticalJustificationValues.Bottom,
                            "both" => VerticalJustificationValues.Both,
                            _ => VerticalJustificationValues.Top,
                        },
                    });
                }

                if (hasBorders)
                {
                    SetBorders(section, borders);
                }

                if (numberFormat is not null || startAt is not null)
                {
                    var type = section.GetFirstChild<PageNumberType>()?.CloneNode(true) as PageNumberType ?? new PageNumberType();

                    type.Format = numberFormat ?? type.Format;
                    type.Start = startAt is null ? type.Start : Math.Max(0, startAt.Value);
                    WordSchemaOrder.Set(section, type);
                }

                report.Add($"Section {number}: {WordDescriber.DescribeSection(section)}.");
            }

            return Task.FromResult(report);
        }, arguments.SaveAs(), cancellationToken);

        var answer = new StringBuilder();

        answer.Append("Changed the page layout of \"").Append(document.Name).Append("\" (version ").Append(document.Version).AppendLine("):");

        foreach (var line in lines)
        {
            answer.AppendLine(line);
        }

        return answer.ToString().TrimEnd();
    }

    private static void SetBorders(SectionProperties section, JsonElement arguments)
    {
        var style = (WordJsonValues.GetString(arguments, "style") ?? "single").Trim().ToLowerInvariant();

        if (style == "none")
        {
            WordSchemaOrder.Remove<PageBorders>(section);

            return;
        }

        var value = style switch
        {
            "double" => BorderValues.Double,
            "dotted" => BorderValues.Dotted,
            "dashed" => BorderValues.Dashed,
            "thick" => BorderValues.Thick,
            "triple" => BorderValues.Triple,
            _ => BorderValues.Single,
        };
        var color = WordColor.ParseOrDefault(WordJsonValues.GetString(arguments, "color"), "000000");
        var size = (UInt32Value)(uint)Math.Clamp(Math.Round((WordJsonValues.GetDouble(arguments, "width") ?? (style == "thick" ? 3 : 1)) * 8), 2, 96);
        var space = (UInt32Value)(uint)Math.Clamp(Math.Round(WordJsonValues.GetDouble(arguments, "space") ?? 24), 0, 31);
        var pageBorders = new PageBorders
        {
            OffsetFrom = PageBorderOffsetValues.Page,
            TopBorder = new TopBorder { Val = value, Size = size, Space = space, Color = color },
            LeftBorder = new LeftBorder { Val = value, Size = size, Space = space, Color = color },
            BottomBorder = new BottomBorder { Val = value, Size = size, Space = space, Color = color },
            RightBorder = new RightBorder { Val = value, Size = size, Space = space, Color = color },
        };

        switch (WordJsonValues.GetString(arguments, "apply_to"))
        {
            case "first_page":
                pageBorders.Display = PageBorderDisplayValues.FirstPage;

                break;

            case "not_first_page":
                pageBorders.Display = PageBorderDisplayValues.NotFirstPage;

                break;
        }

        WordSchemaOrder.Set(section, pageBorders);
    }

    private static NumberFormatValues? ReadNumberFormat(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "decimal" => NumberFormatValues.Decimal,
            "lower_roman" => NumberFormatValues.LowerRoman,
            "upper_roman" => NumberFormatValues.UpperRoman,
            "lower_letter" => NumberFormatValues.LowerLetter,
            "upper_letter" => NumberFormatValues.UpperLetter,
            _ => null,
        };
    }
}
