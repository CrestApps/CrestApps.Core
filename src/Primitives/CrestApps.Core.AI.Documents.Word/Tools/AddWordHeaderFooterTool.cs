using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Formatting;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Sets or removes a header or a footer: text in up to three zones, fields such as page numbers and the date,
/// a logo, and a dividing line.
/// </summary>
internal sealed class AddWordHeaderFooterTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordHeaderFooter;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "kind": { "type": "string", "enum": ["header", "footer"] },
            "type": { "type": "string", "enum": ["default", "first", "even"], "description": "default (every page), first (a different first page) or even (even pages, with default for odd pages). Default 'default'." },
            {{WordSectionSelection.Schema}},
            "text": { "type": "string", "description": "One line of text, with inline Markdown and the tokens {page}, {pages}, {section_pages}, {date}, {title}, {author}, {file_name}." },
            "left": { "type": "string", "description": "Text at the left of the line (with tokens)." },
            "center": { "type": "string", "description": "Text in the middle (with tokens)." },
            "right": { "type": "string", "description": "Text at the right (with tokens)." },
            "alignment": { "type": "string", "enum": ["left", "center", "right"], "description": "Alignment of 'text'." },
            "image": { "type": "string", "description": "A logo: an uploaded image's name or id, a [fig:N] marker, or a data: URI." },
            "image_height": { "type": ["number", "string"], "description": "Logo height. Default 0.5in." },
            "image_position": { "type": "string", "enum": ["left", "center", "right"] },
            "border": { "type": "boolean", "description": "A line under the header or above the footer." },
            "font_size": { "type": "number" },
            "color": { "type": "string" },
            "hide_on_first_page": { "type": "boolean", "description": "Leave the first page of each section without this header or footer, for a cover page." },
            "remove": { "type": "boolean", "description": "Remove this header or footer instead." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["kind"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordHeaderFooterTool"/> class.
    /// </summary>
    public AddWordHeaderFooterTool()
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
    public override string Description => "Sets a header or footer of a Word document (all sections by default): 'text', or 'left'/'center'/'right' zones, with tokens {page}, {pages}, {section_pages}, {date}, {title}, {author}, {file_name} that become live fields, an optional logo 'image', and a dividing 'border'. 'type' first gives the first page its own (or none), even gives even pages their own. 'hide_on_first_page' keeps a cover page clean. 'remove' deletes it. Page numbers are a footer such as { kind: footer, center: \"Page {page} of {pages}\" }; set_word_page_layout sets roman numerals or a restart.";

    /// <summary>
    /// Sets the header or footer.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var header = !string.Equals(arguments.GetString("kind"), "footer", StringComparison.OrdinalIgnoreCase);
        var kind = header ? "header" : "footer";
        var type = WordHeadersFooters.ReadType(arguments.GetString("type"));
        var image = arguments.GetString("image") is { } source
            ? await context.ResolveImageAsync(source, cancellationToken) ?? throw new WordToolException($"No picture was found for \"{source}\".")
            : null;

        var (summary, document) = await context.EditAsync(arguments.Document(), (arguments.GetBoolean("remove") == true ? "Removed the " : "Set the ") + kind, edit =>
        {
            var sections = WordSectionSelection.Read(edit.Package, arguments);

            if (arguments.GetBoolean("remove") == true)
            {
                foreach (var (section, _) in sections)
                {
                    WordHeadersFooters.Remove(edit.Package, section, header, type);
                }

                return Task.FromResult($"Removed the {Describe(type)} {kind} from {sections.Count} section(s).");
            }

            if (arguments.GetString("text") is null && arguments.GetString("left") is null && arguments.GetString("center") is null && arguments.GetString("right") is null && image is null)
            {
                throw new WordToolException("Give the content: 'text', 'left'/'center'/'right', or an 'image'. Use remove: true to delete it.");
            }

            var styleId = WordStyleSheet.Ensure(edit.Package.MainPart, header ? WordStyleSheet.Header : WordStyleSheet.Footer, edit.Design);

            foreach (var (section, _) in sections)
            {
                if (type == HeaderFooterValues.First)
                {
                    WordSchemaOrder.Set(section, new TitlePage());
                }

                WordHeadersFooters.Set(edit.Package, section, header, type, part => Build(edit, arguments, part, header, styleId, section, image));

                if (arguments.GetBoolean("hide_on_first_page") == true && type != HeaderFooterValues.First)
                {
                    WordSchemaOrder.Set(section, new TitlePage());

                    if (WordHeadersFooters.Find(edit.Package, section, header, HeaderFooterValues.First) is null)
                    {
                        WordHeadersFooters.Set(edit.Package, section, header, HeaderFooterValues.First, _ => []);
                    }
                }
            }

            if (type == HeaderFooterValues.Even)
            {
                WordSchemaOrder.Set(edit.Package.GetOrCreateSettings(), new EvenAndOddHeaders());
            }

            return Task.FromResult($"Set the {Describe(type)} {kind} of {sections.Count} section(s).");
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary} preview_word shows it on the pages.";
    }

    private static List<OpenXmlElement> Build(WordEditContext edit, WordToolArguments arguments, OpenXmlPart part, bool header, string styleId, SectionProperties section, Editing.WordImageData image)
    {
        var properties = edit.Package.Document.PackageProperties;
        var tokens = new WordHeaderFooterTokens(
            part,
            new WordRunFormat { Size = arguments.GetDouble("font_size"), Color = arguments.GetString("color") },
            properties.Title,
            properties.Creator,
            edit.Source?.Name,
            edit.Now);

        var width = WordSections.TextWidthTwips(section);
        var elements = new List<OpenXmlElement>();

        if (image is not null)
        {
            var height = arguments.GetLength("image_height") ?? 36;
            var picture = WordImageWriter.CreateParagraph(
                part,
                image,
                new WordImageOptions { Height = height, Alignment = arguments.GetString("image_position") ?? "left", AltText = "Logo" },
                Editing.WordDrawingIds.For(edit.Package).Next(),
                WordUnits.FromTwips(width));

            (picture.ParagraphProperties ??= new ParagraphProperties()).ParagraphStyleId = new ParagraphStyleId { Val = styleId };
            elements.Add(picture);
        }

        Paragraph line = null;

        if (arguments.GetString("left") is not null || arguments.GetString("center") is not null || arguments.GetString("right") is not null)
        {
            line = WordHeaderFooterContent.Zones(arguments.GetRawString("left"), arguments.GetRawString("center"), arguments.GetRawString("right"), width, styleId, tokens);
        }
        else if (arguments.GetRawString("text") is { } text)
        {
            line = new Paragraph(new ParagraphProperties { ParagraphStyleId = new ParagraphStyleId { Val = styleId } });

            if (OpenXml.Word.WordTableWriter.ReadAlignment(arguments.GetString("alignment")) is { } alignment)
            {
                line.ParagraphProperties.Justification = new Justification { Val = alignment };
            }

            WordHeaderFooterContent.Append(line, text, tokens);
        }

        if (line is not null)
        {
            elements.Add(line);
        }

        if (arguments.GetBoolean("border") == true && elements.Count > 0)
        {
            var target = (Paragraph)(header ? elements[^1] : elements[0]);
            var border = new ParagraphBorders();

            if (header)
            {
                border.BottomBorder = new BottomBorder { Val = BorderValues.Single, Size = 6U, Space = 4U, Color = edit.Design.AccentColor };
            }
            else
            {
                border.TopBorder = new TopBorder { Val = BorderValues.Single, Size = 6U, Space = 4U, Color = edit.Design.AccentColor };
            }

            (target.ParagraphProperties ??= new ParagraphProperties()).ParagraphBorders = border;
        }

        return elements;
    }

    private static string Describe(HeaderFooterValues type)
    {
        return type == HeaderFooterValues.First ? "first-page" : type == HeaderFooterValues.Even ? "even-page" : "default";
    }
}
