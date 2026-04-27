using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Chat.Handlers;

internal sealed class ChatInteractionEntryHandler : CatalogEntryHandlerBase<ChatInteraction>
{
    private readonly TimeProvider _timeProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatInteractionEntryHandler"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    public ChatInteractionEntryHandler(
        TimeProvider timeProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        _timeProvider = timeProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Initializings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializingAsync(InitializingContext<ChatInteraction> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <summary>
    /// Updatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task UpdatingAsync(UpdatingContext<ChatInteraction> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <summary>
    /// Initializeds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializedAsync(InitializedContext<ChatInteraction> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task CreatingAsync(CreatingContext<ChatInteraction> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    private void EnsureCreatedDefaults(ChatInteraction interaction)
    {
        if (interaction.CreatedUtc == default)
        {
            interaction.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        interaction.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Identity?.Name;
        interaction.Author ??= user.Identity?.Name;
    }

    private static Task PopulateAsync(ChatInteraction interaction, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.Title), value => interaction.Title = value);
        json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.OwnerId), value => interaction.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.Author), value => interaction.Author = value);
        json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.ConnectionName), value => interaction.ConnectionName = value);
        json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.SystemMessage), value => interaction.SystemMessage = value);
        json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.OrchestratorName), value => interaction.OrchestratorName = value);
        json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.ResponseHandlerName), value => interaction.ResponseHandlerName = value);

        if (!json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.ChatDeploymentName), value => interaction.ChatDeploymentName = value))
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (!json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.ChatDeploymentId), value => interaction.ChatDeploymentName = value))
            {
                json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.DeploymentId), value => interaction.ChatDeploymentName = value);
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        if (!json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.UtilityDeploymentName), value => interaction.UtilityDeploymentName = value))
        {
#pragma warning disable CS0618 // Type or member is obsolete
            json.TryUpdateTrimmedStringValue(nameof(ChatInteraction.UtilityDeploymentId), value => interaction.UtilityDeploymentName = value);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        if (json.TryGetNullableSingleValue(nameof(ChatInteraction.Temperature), out var temperature))
        {
            interaction.Temperature = temperature;
        }

        if (json.TryGetNullableSingleValue(nameof(ChatInteraction.TopP), out var topP))
        {
            interaction.TopP = topP;
        }

        if (json.TryGetNullableSingleValue(nameof(ChatInteraction.FrequencyPenalty), out var frequencyPenalty))
        {
            interaction.FrequencyPenalty = frequencyPenalty;
        }

        if (json.TryGetNullableSingleValue(nameof(ChatInteraction.PresencePenalty), out var presencePenalty))
        {
            interaction.PresencePenalty = presencePenalty;
        }

        if (json.TryGetNullableInt32Value(nameof(ChatInteraction.MaxTokens), out var maxTokens))
        {
            interaction.MaxTokens = maxTokens;
        }

        if (json.TryGetNullableInt32Value(nameof(ChatInteraction.PastMessagesCount), out var pastMessagesCount))
        {
            interaction.PastMessagesCount = pastMessagesCount;
        }

        if (json.TryGetDateTimeValue(nameof(ChatInteraction.CreatedUtc), out var createdUtc))
        {
            interaction.CreatedUtc = createdUtc;
        }

        if (json.TryGetNullableInt32Value(nameof(ChatInteraction.DocumentIndex), out var documentIndex) && documentIndex.HasValue)
        {
            interaction.DocumentIndex = documentIndex.Value;
        }

        if (json.TryGetTrimmedStringListValue(nameof(ChatInteraction.ToolNames), out var toolNames))
        {
            interaction.ToolNames = toolNames;
        }

        if (json.TryGetTrimmedStringListValue(nameof(ChatInteraction.AgentNames), out var agentNames))
        {
            interaction.AgentNames = agentNames;
        }

        if (json.TryGetTrimmedStringListValue(nameof(ChatInteraction.McpConnectionIds), out var mcpConnectionIds))
        {
            interaction.McpConnectionIds = mcpConnectionIds;
        }

        if (json.TryGetTrimmedStringListValue(nameof(ChatInteraction.A2AConnectionIds), out var a2aConnectionIds))
        {
            interaction.A2AConnectionIds = a2aConnectionIds;
        }

        UpdateDocuments(json, nameof(ChatInteraction.Documents), values => interaction.Documents = values);

        return Task.CompletedTask;
    }

    private static void UpdateDocuments(JsonObject data, string propertyName, Action<List<ChatDocumentInfo>> update)
    {
        if (!data.TryGetPropertyValue(propertyName, out var node))
        {
            return;
        }

        update(node?.Deserialize<List<ChatDocumentInfo>>() ?? []);
    }
}
