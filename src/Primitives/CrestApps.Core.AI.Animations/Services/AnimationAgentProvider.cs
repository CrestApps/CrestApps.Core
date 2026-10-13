using CrestApps.Core.AI.Animations.Tools;
using CrestApps.Core.AI.Animations.Video;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Animations.Services;

/// <summary>
/// Contributes the system animation agent. Like the other system agents it is always available to the primary
/// model, runs its own hidden tools, and is hidden from the user-facing agent selection list. It is offered only
/// when FFmpeg can be run, so the model is never routed to an agent that cannot finish the job.
/// </summary>
internal sealed class AnimationAgentProvider : IAIProfileProvider
{
    /// <summary>
    /// The technical name of the system animation agent.
    /// </summary>
    public const string AgentName = "animation-agent";

    /// <summary>
    /// The identifier of the embedded AI template that supplies the agent's system prompt.
    /// </summary>
    public const string SystemPromptTemplateId = "animation-agent";

    private const string AgentItemId = "system-animation-agent";

    // The description is the only thing the primary model reads when it decides where a request goes, so it
    // names what the agent makes, what it can draw, and what it cannot.
    private const string AgentDescription =
        "Creates an animated MP4 video, drawn in code, and plays it in the chat. Delegate to this agent every request to draw, animate or make a video, clip or moving scene of any subject: animals, people, vehicles, places, nature, action and story beats such as a chase, a take-off or a reveal, at the length the user asks for and with an optional cinematic look. It also changes a video already made in the conversation. The result is stylized 2D animation drawn in code, not photoreal footage, and any physical value such as a wind speed is a simulation setting, not a validated model. Do not use it for still pictures (use image generation), charts, or uploaded images or videos. Pass the user's full request, including every detail they gave, and pass its reply on to the user in full: the [fig:N] video marker and the sentences that describe the video.";

    private static readonly string[] _toolNames = [RenderAnimationTool.TheName, GetAnimationSourceTool.TheName];

    private readonly ITemplateService _templateService;
    private readonly FFmpegProbe _probe;
    private readonly AnimationOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationAgentProvider"/> class.
    /// </summary>
    /// <param name="templateService">The template service used to load the agent's system prompt.</param>
    /// <param name="probe">The FFmpeg probe.</param>
    /// <param name="options">The animation options.</param>
    public AnimationAgentProvider(
        ITemplateService templateService,
        FFmpegProbe probe,
        IOptions<AnimationOptions> options)
    {
        _templateService = templateService;
        _probe = probe;
        _options = options.Value;
    }

    /// <summary>
    /// Gets the system animation agent.
    /// </summary>
    /// <param name="type">The profile type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyList<AIProfile>> GetProfilesAsync(
        AIProfileType type,
        CancellationToken cancellationToken = default)
    {
        if (type != AIProfileType.Agent || !_options.Enabled || !(await _probe.GetResultAsync()).IsAvailable)
        {
            return [];
        }

        var systemPrompt = await _templateService.RenderAsync(SystemPromptTemplateId, cancellationToken: cancellationToken);

        return [BuildAgent(systemPrompt, _options.AgentDeploymentName)];
    }

    private static AIProfile BuildAgent(string systemPrompt, string deploymentName)
    {
        var profile = new AIProfile
        {
            ItemId = AgentItemId,
            Name = AgentName,
            DisplayText = "Animation Agent",
            Type = AIProfileType.Agent,
            Source = "System",
            Description = AgentDescription,
            ChatDeploymentName = string.IsNullOrWhiteSpace(deploymentName) ? null : deploymentName,
        };

        profile.Put(new AgentMetadata
        {
            Availability = AgentAvailability.AlwaysAvailable,
            AllowToolInvocation = true,
            IsSystem = true,
        });

        profile.Put(new FunctionInvocationMetadata
        {
            Names = [.. _toolNames],
        });

        profile.Put(new AIProfileMetadata
        {
            SystemMessage = systemPrompt,
            Temperature = null,
        });

        return profile;
    }
}
