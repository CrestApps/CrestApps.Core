using System.Text.Json;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Animations.Endpoints;
using CrestApps.Core.AI.Animations.Rendering;
using CrestApps.Core.AI.Animations.Scripting;
using CrestApps.Core.AI.Animations.Services;
using CrestApps.Core.AI.Animations.Storage;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Animations.Tools;

/// <summary>
/// Renders an animation from JavaScript the model wrote for the user's request, and shows it as a video player in
/// the chat.
/// </summary>
/// <remarks>
/// The script runs in the Jint sandbox: no file, network or host access, and limits on time, statements and
/// memory for every frame. It is first run for a few frames without encoding, so an error or a script too slow to
/// finish comes back to the model in seconds, with the line it happened on, for the model to fix.
/// </remarks>
internal sealed class RenderAnimationTool : AIFunction
{
    public const string TheName = "render_animation";

    private const string ReviewCountItemKey = "CrestApps.Animations.ScriptReviews";

    // A first draft and one revision are judged before rendering; after that the script is rendered as it stands,
    // so a request never loops on reviews.
    private const int MaxReviews = 2;

    private static readonly JsonSerializerOptions _argumentSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "title": { "type": "string", "description": "A short title for the video." },
        "request": { "type": "string", "description": "What the user asked for, in full. Key frames of the script are checked against it before the video is rendered." },
        "duration_seconds": { "type": "number", "description": "Length of the video in seconds. Use the length the user asked for." },
        "width": { "type": "integer", "description": "Frame width in pixels. Omit for the default." },
        "height": { "type": "integer", "description": "Frame height in pixels. Omit for the default." },
        "frame_rate": { "type": "integer", "description": "Frames per second. Omit for the default." },
        "seed": { "type": "integer", "description": "Seed for Math.random, so the same script draws the same video. Keep it when editing." },
        "look": {
          "type": "object",
          "description": "Film look laid over every frame by the host.",
          "properties": {
            "letterbox": { "type": "boolean", "description": "Black bars cropping the picture to 2.39:1 widescreen." },
            "vignette": { "type": "number", "description": "How much the corners darken, 0 to 1. About 0.4 to 0.6 for a film look." },
            "grain": { "type": "number", "description": "Film grain, 0 to 1. About 0.2 to 0.35 for a film look." }
          },
          "additionalProperties": false
        },
        "code": { "type": "string", "description": "The whole JavaScript program: function setup(info) returning a state object, and function frame(ctx, t, state) drawing one frame at time t seconds with the canvas-like ctx." }
      },
      "required": ["request", "code"],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Renders an MP4 from JavaScript you write that draws each frame on a canvas-like ctx, and shows it as a video player in the chat.";

    /// <summary>
    /// Gets the JSON schema of the arguments.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional properties: the schema is not strict.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var services = arguments.Services;
        var logger = services.GetRequiredService<ILogger<RenderAnimationTool>>();
        var options = services.GetRequiredService<IOptions<AnimationOptions>>().Value;
        var invocation = AIInvocationScope.Current;
        var scope = GeneratedDocumentScope.Resolve();

        if (invocation is null || scope is null)
        {
            return "Animations can only be made inside a chat conversation, because the video is kept with the conversation that asked for it.";
        }

        RenderArguments request;

        try
        {
            request = JsonSerializer.Deserialize<RenderArguments>(JsonSerializer.SerializeToElement(arguments.ToDictionary(pair => pair.Key, pair => pair.Value)), _argumentSerializerOptions);
        }
        catch (JsonException ex)
        {
            return $"The arguments could not be read: {ex.Message} Fix them and call {TheName} again.";
        }

        if (string.IsNullOrWhiteSpace(request?.Code))
        {
            return "Provide the whole script in 'code': function setup(info) and function frame(ctx, t, state).";
        }

        if (request.Code.Length > options.MaxScriptCharacters)
        {
            return $"The script is {request.Code.Length} characters long; the limit is {options.MaxScriptCharacters}. Shorten it and call {TheName} again.";
        }

        var adjustments = new List<string>();
        var (format, durationSeconds) = AnimationFormatResolver.Resolve(request.DurationSeconds, request.Width, request.Height, request.FrameRate, options, adjustments);
        var look = request.Look is null
            ? CinematicLook.None
            : new CinematicLook(request.Look.Letterbox == true, Math.Clamp(request.Look.Vignette ?? 0, 0, 1), Math.Clamp(request.Look.Grain ?? 0, 0, 1));
        var scriptRequest = new AnimationScriptRequest(request.Code, format, durationSeconds, request.Seed ?? 7, look);
        var title = string.IsNullOrWhiteSpace(request.Title) ? "Animation" : request.Title.Trim();

        // Everything the tool needs later is resolved now: a render can outlive the request on providers that keep
        // running a tool after the turn is cancelled.
        var renderService = services.GetRequiredService<AnimationRenderService>();
        var reviewer = services.GetRequiredService<AnimationFrameReviewer>();
        var publishing = AnimationPublisher.PublishingServices.From(services);

        try
        {
            await renderService.TestScriptAsync(scriptRequest, cancellationToken);

            // A script is looked at before it is rendered, as a test shot is, and sent back with the director's notes
            // when it falls short.
            var reviews = invocation.Items.TryGetValue(ReviewCountItemKey, out var count) && count is int reviewCount
                ? reviewCount
                : 0;

            if (options.ReviewScriptsBeforeRender && !string.IsNullOrWhiteSpace(request.Request) && reviews < MaxReviews)
            {
                invocation.Items[ReviewCountItemKey] = reviews + 1;

                var review = await reviewer.ReviewAsync(scriptRequest, request.Request, cancellationToken);

                if (review is { Passed: false })
                {
                    var next = reviews + 1 < MaxReviews
                        ? "It is reviewed once more before rendering."
                        : "That call is rendered.";

                    return $"Before rendering, the key frames of your script were reviewed against the request, and they need work:\n\n{review.Critique}\n\nRevise the script to fix these, then call {TheName} again with the whole revised script. {next}";
                }
            }
        }
        catch (AnimationScriptException ex)
        {
            return ScriptFailure("testing", ex.Message);
        }

        try
        {
            using var output = await renderService.RenderScriptAsync(scriptRequest, cancellationToken);

            var owner = new AnimationOwner(scope.Value.ReferenceType, scope.Value.ReferenceId);

            // The settings are kept beside the script, so an edit can keep the length, size and look the same.
            var settings = JsonSerializer.Serialize(request with { Code = null, Request = null }, _argumentSerializerOptions);
            var (animationId, marker) = await AnimationPublisher.PublishAsync(publishing, invocation, owner, output, title, settings, request.Code, cancellationToken);

            if (marker is null)
            {
                logger.LogWarning("Animation '{AnimationId}' was rendered but cannot be shown: no '{RouteName}' endpoint is mapped.", animationId, AnimationAssetEndpoint.DefaultRouteName);

                return "The video was rendered, but this site cannot play animations in the chat yet: the animation endpoint is not set up.";
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Animation '{AnimationId}' rendered: {FrameCount} frames, {VideoBytes} bytes, marker '{Marker}'.", animationId, format.FrameCount, output.VideoBytes, marker);
            }

            return AnimationPublisher.Instructions(marker, format, durationSeconds, adjustments);
        }
        catch (AnimationScriptException ex)
        {
            return ScriptFailure("rendering", ex.Message);
        }
        catch (AnimationRenderException ex)
        {
            logger.LogWarning(ex, "Animation '{Title}' could not be rendered.", title);

            return $"The video could not be made: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Animation '{Title}' was rendered but could not be stored.", title);

            return "The video was rendered but could not be saved on the server.";
        }
    }

    private static string ScriptFailure(string stage, string message)
    {
        return $"The script failed while {stage}: {message}\nFix the script and call {TheName} again with the whole corrected script. If it still fails after three attempts, tell the user plainly what could not be drawn.";
    }

    private sealed record RenderArguments
    {
        public string Title { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Request { get; init; }

        public double? DurationSeconds { get; init; }

        public int? Width { get; init; }

        public int? Height { get; init; }

        public int? FrameRate { get; init; }

        public int? Seed { get; init; }

        public LookArguments Look { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Code { get; init; }
    }

    private sealed record LookArguments
    {
        public bool? Letterbox { get; init; }

        public float? Vignette { get; init; }

        public float? Grain { get; init; }
    }
}
