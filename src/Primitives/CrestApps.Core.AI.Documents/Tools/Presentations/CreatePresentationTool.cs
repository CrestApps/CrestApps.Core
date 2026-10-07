using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Starts a new deck: from a theme preset, custom colours and fonts, or an uploaded deck or template, and
/// optionally with its slides.
/// </summary>
internal sealed class CreatePresentationTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.CreatePresentation;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreatePresentationTool"/> class.
    /// </summary>
    public CreatePresentationTool()
        : base(
            TheName,
            "Starts a new presentation and makes it the active one. Choose a look with 'theme' (office, modern, corporate, dark, vibrant, minimal, nature, warm, ocean) and optionally 'colors' and fonts, or pass template_document_id to build on an uploaded .pptx/.potx so the deck inherits its masters, layouts and branding. Pass 'slides' to write the whole deck in this one call: each slide has a layout, title, body bullets, notes and optional elements such as charts, tables, pictures and icons. Preview the result with preview_presentation.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "The presentation's name, which is also its file name." },
                "theme": { "type": "string", "enum": ["office", "modern", "corporate", "dark", "vibrant", "minimal", "nature", "warm", "ocean"] },
                "colors": { "type": "object", "description": "Theme colours to use instead, keyed by slot: primary/accent1 ... accent6, text1, text2, background1, background2, hyperlink.", "additionalProperties": { "type": "string" } },
                "heading_font": { "type": "string" },
                "body_font": { "type": "string" },
                "dark": { "type": "boolean", "description": "Light text on dark slides." },
                "slide_size": { "type": "string", "enum": ["16:9", "4:3", "16:10", "a4"], "description": "Defaults to 16:9." },
                "template_document_id": { "type": "string", "description": "An uploaded .pptx or .potx (id or file name) whose design the deck starts from." },
                "keep_template_slides": { "type": "boolean", "description": "Keep the template's own slides. Defaults to false: only its design is used." },
                "slides": { "type": "array", "items": SLIDE }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Gets a value indicating whether the tool needs a deck.
    /// </summary>
    protected override bool RequiresDeck => false;

    /// <summary>
    /// Creates the deck.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var arguments = call.Arguments;
        var session = call.Session;
        var name = arguments.String("name", "title", "file_name");
        var options = new PresentationCreateOptions { Title = name };
        var notes = new List<string>();

        var templateReference = arguments.String("template_document_id", "template", "template_document");

        if (!string.IsNullOrWhiteSpace(templateReference))
        {
            var existing = session.Workspace.FindDeck(templateReference);

            if (existing is not null && session.FindDocument(templateReference) is null)
            {
                options.TemplatePackage = await session.Workspace.ReadAsync(existing);
            }
            else
            {
                var document = session.FindDocument(templateReference)
                    ?? throw new PresentationArgumentException($"There is no uploaded deck or template \"{templateReference}\" in this conversation.");

                options.TemplatePackage = await session.ImportAsync(document, cancellationToken)
                    ?? throw new PresentationArgumentException($"\"{document.FileName}\" could not be read as a presentation or template.");
            }

            options.KeepTemplateSlides = arguments.Bool("keep_template_slides") == true;
            notes.Add("Built on the design of \"" + templateReference + "\".");
        }
        else
        {
            var presetName = arguments.String("theme", "preset", "style") ?? PresentationThemePresets.DefaultName;

            if (!PresentationThemePresets.TryGet(presetName, out var preset))
            {
                throw new PresentationArgumentException($"\"{presetName}\" is not a theme. Use one of: {string.Join(", ", PresentationThemePresets.All.Select(candidate => candidate.Name))}.");
            }

            options.ThemeName = char.ToUpperInvariant(preset.Name[0]) + preset.Name[1..];
            options.HeadingFont = arguments.String("heading_font", "title_font") ?? preset.HeadingFont;
            options.BodyFont = arguments.String("body_font", "font") ?? preset.BodyFont;
            options.DarkBackground = arguments.Bool("dark", "dark_background") ?? preset.DarkBackground;

            foreach (var (slot, color) in preset.Colors)
            {
                options.ThemeColors[slot] = "#" + color;
            }

            if (arguments.Object("colors", "theme_colors", "palette") is { } colors)
            {
                foreach (var (slot, value) in colors)
                {
                    if (PresentationArguments.ReadString(value) is { } color)
                    {
                        options.ThemeColors[slot] = color;
                    }
                }
            }

            (options.SlideWidth, options.SlideHeight) = SlideSize(arguments.String("slide_size", "size", "aspect_ratio"));
        }

        byte[] package;

        try
        {
            package = await session.Engine.CreateAsync(options, cancellationToken);
        }
        catch (PresentationEditException exception)
        {
            throw new PresentationArgumentException(exception.Message);
        }

        var deck = await session.Workspace.AddAsync(name, package, null, null);
        var slides = arguments.Array("slides").Select(PresentationArguments.ReadSlide).ToList();

        if (slides.Count > session.Options.MaxSlides)
        {
            throw new PresentationArgumentException($"A presentation can have at most {session.Options.MaxSlides} slides.");
        }

        if (slides.Count == 0)
        {
            var model = await call.ReadAsync(deck, cancellationToken);
            var builder = new StringBuilder();

            builder.Append("Created \"").Append(deck.Name).Append("\" (").Append(deck.Id).AppendLine(") and made it the active presentation.");
            builder.AppendLine(PresentationDescriber.Heading(deck, model));

            if (model.Slides.Count == 0)
            {
                builder.AppendLine("It has no slides yet: add them with add_slide, using the layouts listed by get_presentation_theme.");
            }

            return builder.ToString().TrimEnd();
        }

        var edits = await PresentationRequestBuilder.SlidesAsync(session, slides, notes, cancellationToken);
        var result = await session.ApplyAsync(deck, edits, "created the slides", cancellationToken);

        PresentationRequestBuilder.RecordLinks(call.Services, deck, slides.SelectMany(slide => slide.Elements), result);
        await session.Workspace.SaveStateAsync();

        notes.Insert(0, $"Created \"{deck.Name}\" ({deck.Id}) and made it the active presentation.");

        return Changed(deck, result, notes);
    }

    private static (long Width, long Height) SlideSize(string size)
    {
        return size?.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal) switch
        {
            null or "" or "16:9" or "widescreen" or "wide" => (PresentationUnits.WideSlideWidth, PresentationUnits.WideSlideHeight),
            "4:3" or "standard" or "letter" => (9_144_000, 6_858_000),
            "16:10" => (9_144_000, 5_715_000),
            "a4" => (9_906_000, 6_858_000),
            _ => throw new PresentationArgumentException($"\"{size}\" is not a slide size. Use 16:9, 4:3, 16:10 or a4."),
        };
    }
}
