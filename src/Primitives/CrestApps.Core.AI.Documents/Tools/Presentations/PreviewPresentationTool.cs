using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;
using CrestApps.Core.AI.Documents.Presentations.Rendering;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Shows slides as pictures in the conversation, drawn from the deck as it now stands.
/// </summary>
/// <remarks>
/// The pictures are drawn by the same layout code that fits text when the deck is edited, so a line that
/// wraps in the preview wraps at the same word in PowerPoint. Where the host cannot show pictures the slides
/// are described in text instead.
/// </remarks>
internal sealed class PreviewPresentationTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.PreviewPresentation;

    private const string CacheKey = nameof(PreviewPresentationTool) + ".Results";

    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewPresentationTool"/> class.
    /// </summary>
    public PreviewPresentationTool()
        : base(
            TheName,
            "Shows slides to the user as pictures, drawn from the presentation as it is now, including every change made in this conversation. Call it after creating or changing slides so the user sees the result, and whenever they ask to see the deck. Returns [fig:N] markers that MUST be written in your answer exactly as given, or the user sees nothing. Shows up to 8 slides per call; name the slides that changed rather than the whole deck. This is not a download: use export_presentation for the file.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slides": { "type": ["integer", "string", "array"], "description": "The slides to show, such as [2, 3] or \"1-4\". Defaults to the first slides." },
                "width": { "type": "integer", "description": "Picture width in pixels. Defaults to 960." },
                "format": { "type": "string", "enum": ["image", "text"], "description": "'text' describes the slides in words instead; use it only when the user asks for text." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Shows the slides.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var options = call.Services.GetService<IOptions<PresentationPreviewOptions>>()?.Value ?? new PresentationPreviewOptions();
        var outline = await call.ReadAsync(deck, cancellationToken);

        if (outline.Slides.Count == 0)
        {
            return $"\"{deck.Name}\" has no slides yet, so there is nothing to show. Add slides with add_slide.";
        }

        var requested = call.Slides(outline, false);
        var slides = requested.Count == 0 ? Enumerable.Range(1, outline.Slides.Count).ToList() : requested;
        var omitted = slides.Skip(options.MaxSlides).ToList();
        slides = slides.Take(options.MaxSlides).ToList();

        var width = Math.Clamp(call.Arguments.Int("width", "pixel_width") ?? options.Width, 320, options.MaxWidth);
        var asText = string.Equals(call.Arguments.String("format"), "text", StringComparison.OrdinalIgnoreCase);
        var key = string.Create(CultureInfo.InvariantCulture, $"{deck.Id}:{deck.Revision}:{string.Join(',', slides)}:{width}:{asText}");

        if (!asText && Cached(key) is { } cached)
        {
            // Asked again in the same turn, so the same pictures are shown again.
            foreach (var marker in AIInvocationScope.Current.ToolReferences.Keys.Where(marker => cached.Contains(marker, StringComparison.Ordinal)))
            {
                AIInvocationScope.Current.RequestFigureDisplay(marker);
            }

            return cached;
        }

        if (asText)
        {
            return Describe(outline, slides, omitted);
        }

        var model = await call.Session.ReadAsync(
            deck,
            new PresentationReadOptions { IncludeImageData = true, MaxImageBytes = options.MaxImageBytesPerSlide, Slides = slides.ToHashSet() },
            cancellationToken);

        var figures = new List<(string Caption, string Markup, string FileName)>();
        var captions = new List<string>();

        foreach (var number in slides)
        {
            var slide = model.Slides[number - 1];
            var drawing = SlideDrawingBuilder.Build(model, slide, new SlideDrawingOptions { MaxImageBytes = options.MaxImageBytesPerSlide });
            var caption = Caption(slide, drawing);

            figures.Add((caption, SlideSvgWriter.Write(drawing, width), PresentationFigurePublisher.FileName(deck.Name, "slide-" + number.ToString(CultureInfo.InvariantCulture))));
            captions.Add(caption);
        }

        var markers = await PresentationFigurePublisher.PublishAsync(call.Services, call.Session, figures, call.Logger, cancellationToken);

        if (markers is null)
        {
            if (call.Logger?.IsEnabled(LogLevel.Debug) == true)
            {
                call.Logger.LogDebug("The presentation preview fell back to text because this host cannot show pictures.");
            }

            return "This host cannot show pictures, so here are the slides in words.\n" + Describe(outline, slides, omitted);
        }

        var response = new StringBuilder(PresentationFigurePublisher.Instructions(markers, captions, TheName));

        if (omitted.Count > 0)
        {
            response.AppendLine().AppendLine().Append("Only ").Append(options.MaxSlides.ToString(CultureInfo.InvariantCulture))
                .Append(" slides are shown per call; slides ").Append(PresentationDescriber.Range(omitted))
                .Append(" were not. Tell the user, and preview them with slides \"").Append(PresentationDescriber.Range(omitted)).Append("\" if they want to see them.");
        }

        var text = response.ToString();
        Remember(key, text);

        return text;
    }

    private static string Caption(PresentationSlide slide, SlideDrawing drawing)
    {
        var caption = new StringBuilder("Slide ").Append(slide.Number.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(slide.Title))
        {
            caption.Append(": ").Append(PresentationDescriber.Excerpt(slide.Title.Replace('\n', ' '), 80));
        }

        if (slide.Hidden)
        {
            caption.Append(" (hidden in the show)");
        }

        if (drawing.Placeholders.Count > 0)
        {
            caption.Append(" — drawn as labelled boxes: ").AppendJoin(", ", drawing.Placeholders.Distinct());
        }

        if (drawing.OmittedImageBytes > 0)
        {
            caption.Append(" — some pictures were too large to draw and show as boxes");
        }

        return caption.ToString();
    }

    private static string Describe(PresentationModel model, List<int> slides, List<int> omitted)
    {
        var builder = new StringBuilder();

        foreach (var number in slides)
        {
            builder.AppendLine(PresentationDescriber.SlideContent(model, model.Slides[number - 1])).AppendLine();
        }

        if (omitted.Count > 0)
        {
            builder.Append("(Slides ").Append(PresentationDescriber.Range(omitted)).AppendLine(" not described.)");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Cached(string key)
    {
        return AIInvocationScope.Current?.Items.TryGetValue(CacheKey, out var value) == true && value is Dictionary<string, string> cache && cache.TryGetValue(key, out var response)
            ? response
            : null;
    }

    private static void Remember(string key, string response)
    {
        var invocation = AIInvocationScope.Current;

        if (invocation is null)
        {
            return;
        }

        if (!invocation.Items.TryGetValue(CacheKey, out var value) || value is not Dictionary<string, string> cache)
        {
            cache = new Dictionary<string, string>(StringComparer.Ordinal);
            invocation.Items[CacheKey] = cache;
        }

        cache[key] = response;
    }
}
