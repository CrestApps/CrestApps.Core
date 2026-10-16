using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Populates the <see cref="AICompletionContext"/> from <see cref="AIProfile"/> settings
/// including connection, deployment, metadata parameters, persona, and tool names.
/// </summary>
internal sealed class AIProfileCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    private readonly ITemplateService _aiTemplateService;
    private readonly IAIPersonaResolver _personaResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileCompletionContextBuilderHandler"/> class.
    /// </summary>
    /// <param name="aiTemplateService">The ai template service.</param>
    /// <param name="personaResolver">The resolver for the persona the profile links to.</param>
    public AIProfileCompletionContextBuilderHandler(
        ITemplateService aiTemplateService,
        IAIPersonaResolver personaResolver)
    {
        _aiTemplateService = aiTemplateService;
        _personaResolver = personaResolver;
    }

    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public async Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is not AIProfile profile)
        {
            return;
        }

        context.Context.ChatDeploymentName = profile.ChatDeploymentName;
        context.Context.UtilityDeploymentName = profile.UtilityDeploymentName;

        if (profile.TryGet<AIProfileMetadata>(out var metadata))
        {
            context.Context.SystemMessage = await ResolveSystemMessageAsync(profile, metadata);
            context.Context.Temperature = metadata.Temperature;
            context.Context.TopP = metadata.TopP;
            context.Context.FrequencyPenalty = metadata.FrequencyPenalty;
            context.Context.PresencePenalty = metadata.PresencePenalty;
            context.Context.MaxTokens = metadata.MaxTokens;
            context.Context.PastMessagesCount = metadata.PastMessagesCount;
            context.Context.UseCaching = metadata.UseCaching;
        }

        // Resolved on every request rather than once per session, like prompt templates, so an edit to the
        // persona applies from the next message.
        var persona = await _personaResolver.ResolveAsync(profile);

        if (persona is not null)
        {
            context.Context.SystemMessage = persona.PrependTo(context.Context.SystemMessage);
            context.Context.AdditionalProperties[AICompletionContextKeys.Persona] = persona;
        }

        if (profile.TryGet<AIDeploymentParametersMetadata>(out var modelParameters))
        {
            context.Context.ApplyModelParameters(modelParameters);
        }

        if (profile.TryGet<FunctionInvocationMetadata>(out var functionInvocationMetadata))
        {
            context.Context.ToolNames = functionInvocationMetadata.Names;
        }

        if (context.Context.ToolNames is not { Length: > 0 } &&
            profile.Settings.TryGetValue("AIProfileFunctionInvocationMetadata", out var legacyNode))
        {
            var legacyMetadata = legacyNode switch
            {
                JsonNode jsonNode => jsonNode.Deserialize<FunctionInvocationMetadata>(),
                JsonElement jsonElement => jsonElement.Deserialize<FunctionInvocationMetadata>(),
                _ => JsonSerializer.Deserialize<FunctionInvocationMetadata>(JsonSerializer.Serialize(legacyNode)),
            };

            if (legacyMetadata?.Names is { Length: > 0 })
            {
                context.Context.ToolNames = legacyMetadata.Names;
            }
        }

        if (profile.TryGet<AgentInvocationMetadata>(out var agentInvocationMetadata))
        {
            context.Context.AgentNames = agentInvocationMetadata.Names;
        }
    }

    /// <summary>
    /// Builts the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuiltAsync(AICompletionContextBuiltContext context)
    {
        return Task.CompletedTask;
    }

    private async Task<string> ResolveSystemMessageAsync(AIProfile profile, AIProfileMetadata metadata)
    {
        if (profile.TryGet<PromptTemplateMetadata>(out var promptMetadata))
        {
            var validTemplates = promptMetadata.Templates?.Where(t => !string.IsNullOrWhiteSpace(t.TemplateId)).ToList();
            if (validTemplates is { Count: > 0 })
            {
                var rendered = new List<string>(validTemplates.Count);
                foreach (var template in validTemplates)
                {
                    rendered.Add(await _aiTemplateService.RenderAsync(template.TemplateId, template.Parameters));
                }

                if (!string.IsNullOrWhiteSpace(metadata.SystemMessage))
                {
                    rendered.Add(metadata.SystemMessage);
                }

                return string.Join("\n\n", rendered);
            }
        }

        return metadata.SystemMessage;
    }
}
