using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Chat.Handlers;

internal sealed class ChatInteractionCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    private readonly ITemplateService _aiTemplateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatInteractionCompletionContextBuilderHandler"/> class.
    /// </summary>
    /// <param name="aiTemplateService">The ai template service.</param>
    public ChatInteractionCompletionContextBuilderHandler(ITemplateService aiTemplateService)
    {
        _aiTemplateService = aiTemplateService;
    }

    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public async Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is not ChatInteraction interaction)
        {
            return;
        }

        context.Context.ChatDeploymentName = interaction.ChatDeploymentName;
        context.Context.SystemMessage = await ResolveSystemMessageAsync(interaction);
        context.Context.Temperature = interaction.Temperature;
        context.Context.TopP = interaction.TopP;
        context.Context.FrequencyPenalty = interaction.FrequencyPenalty;
        context.Context.PresencePenalty = interaction.PresencePenalty;
        context.Context.MaxTokens = interaction.MaxTokens;
        context.Context.PastMessagesCount = interaction.PastMessagesCount;
        context.Context.ToolNames = interaction.ToolNames?.ToArray();
        context.Context.AgentNames = interaction.AgentNames?.ToArray();
        context.Context.McpConnectionIds = interaction.McpConnectionIds?.ToArray();
        context.Context.A2AConnectionIds = interaction.A2AConnectionIds?.ToArray();
        context.Context.AdditionalProperties[AICompletionContextKeys.Interaction] = interaction;
        context.Context.AdditionalProperties[AICompletionContextKeys.InteractionId] = interaction.ItemId;
        if (interaction.TryGet<DataSourceMetadata>(out var dataSourceMetadata) && !string.IsNullOrEmpty(dataSourceMetadata.DataSourceId))
        {
            context.Context.DataSourceId = dataSourceMetadata.DataSourceId;
        }

        if (interaction.TryGet<AIDataSourceRagMetadata>(out var ragMetadata))
        {
            context.Context.AdditionalProperties["Strictness"] = ragMetadata.Strictness;
            context.Context.AdditionalProperties["TopNDocuments"] = ragMetadata.TopNDocuments;
            context.Context.AdditionalProperties["IsInScope"] = ragMetadata.IsInScope;
            context.Context.AdditionalProperties["Filter"] = ragMetadata.Filter;
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

    private async Task<string> ResolveSystemMessageAsync(ChatInteraction interaction)
    {
        if (!interaction.TryGet<PromptTemplateMetadata>(out var promptMetadata))
        {
            return interaction.SystemMessage;
        }

        var validTemplates = promptMetadata.Templates?.Where(selection => !string.IsNullOrWhiteSpace(selection.TemplateId)).ToList();
        if (validTemplates is not { Count: > 0 })
        {
            return interaction.SystemMessage;
        }

        var parts = new List<string>(validTemplates.Count);
        foreach (var template in validTemplates)
        {
            var rendered = await _aiTemplateService.RenderAsync(template.TemplateId, template.Parameters);
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                parts.Add(rendered);
            }
        }

        if (!string.IsNullOrWhiteSpace(interaction.SystemMessage))
        {
            parts.Add(interaction.SystemMessage);
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}
