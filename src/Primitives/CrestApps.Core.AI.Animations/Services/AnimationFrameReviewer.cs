using System.Globalization;
using CrestApps.Core.AI.Animations.Scripting;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Animations.Services;

/// <summary>
/// Draws key frames of an animation script and has the vision deployment judge them against the request, the way
/// a director looks at a test shot before the camera rolls.
/// </summary>
/// <remarks>
/// A tool result reaches the model as text, so the frames cannot be handed back to it. The vision model looks at
/// them instead and answers with a critique and a verdict <c>render_animation</c> acts on.
/// </remarks>
internal sealed class AnimationFrameReviewer
{
    private const string ReviewerInstructions =
        "You are a demanding film director and illustrator reviewing still frames of a short animation that a program drew from code. " +
        "Judge the frames strictly against the request. Say plainly whether each subject is recognisable as exactly what was asked (kind, colours and markings, pose and action), " +
        "whether every subject the request names is clearly visible — large enough, sharp and in contrast with what is behind it, not lost in the background — " +
        "whether the main subject is large enough and well framed, whether the action the request describes visibly happens when the frames are read in order, " +
        "and whether the lighting, depth, colour and composition look like a film shot. " +
        "Then list the most important fixes, at most six, each as a concrete instruction for the drawing code: sizes as fractions of the frame, positions, shapes, colours and timing. " +
        "End with a line that reads exactly \"VERDICT: PASS\" when the frames already match the request well, or \"VERDICT: REVISE\" when any subject is unrecognisable, too small, blurred into the background or missing, or the action asked for does not visibly happen across the frames.";

    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _clientFactory;
    private readonly ILogger<AnimationFrameReviewer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationFrameReviewer"/> class.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager, for the vision deployment.</param>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="logger">The logger.</param>
    public AnimationFrameReviewer(
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ILogger<AnimationFrameReviewer> logger)
    {
        _deploymentManager = deploymentManager;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Returns the moments a review looks at: six frames spread over the whole shot, enough to see whether the action
    /// happens, not only how single frames look.
    /// </summary>
    /// <param name="durationSeconds">The length of the video in seconds.</param>
    public static double[] KeyFrameTimes(double durationSeconds)
    {
        return [.. new[] { 0.08, 0.25, 0.42, 0.58, 0.75, 0.92 }.Select(fraction => durationSeconds * fraction)];
    }

    /// <summary>
    /// Reviews a script's key frames.
    /// </summary>
    /// <param name="request">The script and the video it should make.</param>
    /// <param name="userRequest">What the user asked for.</param>
    /// <param name="times">The moments the stills show, in seconds, left to right.</param>
    /// <param name="stills">The stills side by side in one PNG.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The review, or <see langword="null"/> when no vision deployment is configured or the review could not be
    /// made; the script is then rendered without one.
    /// </returns>
    public async Task<AnimationFrameReview> ReviewAsync(
        AnimationScriptRequest request,
        string userRequest,
        IReadOnlyList<double> times,
        byte[] stills,
        CancellationToken cancellationToken)
    {
        var deployment = await _deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Vision, cancellationToken: cancellationToken);

        if (deployment is null || stills is null)
        {
            return null;
        }

        var labels = string.Join(", ", times.Select(time => time.ToString("0.0", CultureInfo.InvariantCulture) + " s"));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ReviewerInstructions),
            new(ChatRole.User,
            [
                new TextContent($"The request: {userRequest}\n\nThe picture shows the frames at {labels}, in reading order (left to right, then the next row), out of a {request.DurationSeconds.ToString("0.#", CultureInfo.InvariantCulture)}-second video. Compare them in order to judge the motion."),
                new DataContent(stills, "image/png"),
            ]),
        };

        ChatResponse response;

        try
        {
            using var chatClient = await _clientFactory.CreateChatClientAsync(deployment, builder => builder.UseUsageLabels(purpose: AIUsagePurposes.ImageAnalysis));

            response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The review is optional: a throttled, filtered or failing vision deployment must not cost the user the
            // video, so the script is rendered without it.
            _logger.LogWarning(ex, "The key frames of an animation could not be reviewed by '{Deployment}'. The script is rendered without a review.", deployment.Name);

            return null;
        }

        var critique = response?.Text?.Trim();

        if (string.IsNullOrEmpty(critique))
        {
            return null;
        }

        var passed = !critique.Contains("VERDICT: REVISE", StringComparison.OrdinalIgnoreCase);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Animation script reviewed: {Verdict}. {Critique}", passed ? "pass" : "revise", critique);
        }

        return new AnimationFrameReview(passed, critique);
    }
}

/// <summary>
/// A director's review of an animation script's key frames.
/// </summary>
/// <param name="Passed">Whether the frames match the request well enough to render.</param>
/// <param name="Critique">The critique and the fixes it asks for.</param>
internal sealed record AnimationFrameReview(bool Passed, string Critique);
