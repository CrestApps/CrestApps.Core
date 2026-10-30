using System.Text;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Resilience;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.AI.Tools;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Gives the model memory beyond the past-messages window. With session memory on, the messages that fall out
/// of the window are folded into a running summary stored on the conversation, the few not yet folded in are
/// shown verbatim, and older turns that bear on the current message are recalled. With past conversations on,
/// the model can look up the signed-in user's earlier conversations with the same profile.
/// </summary>
internal sealed class ConversationMemoryOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    // Summarize once this many messages have fallen out of the window unsummarized, so a long conversation
    // costs one utility call every few turns rather than one per turn. Until then they are shown verbatim.
    internal const int SummaryBatchSize = 6;

    // If summarizing keeps failing, at most this many unsummarized messages are shown verbatim; the rest
    // stay reachable through recall and the search tool.
    internal const int MaxVerbatimMessages = 8;

    internal const int MaxRecalledTurns = 3;

    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _clientFactory;
    private readonly ITemplateService _templateService;
    private readonly ITextTokenizer _tokenizer;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<ChatInteractionMemoryOptions> _chatInteractionMemoryOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConversationMemoryOrchestrationHandler> _logger;

    public ConversationMemoryOrchestrationHandler(
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ITemplateService templateService,
        ITextTokenizer tokenizer,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<ChatInteractionMemoryOptions> chatInteractionMemoryOptions,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        ILogger<ConversationMemoryOrchestrationHandler> logger)
    {
        _deploymentManager = deploymentManager;
        _clientFactory = clientFactory;
        _templateService = templateService;
        _tokenizer = tokenizer;
        _httpContextAccessor = httpContextAccessor;
        _chatInteractionMemoryOptions = chatInteractionMemoryOptions;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        if (context.OrchestrationContext.CompletionContext is null)
        {
            return;
        }

        if (IsSessionMemoryEnabled(context.Resource))
        {
            await ApplySessionMemoryAsync(context, cancellationToken);
        }

        if (context.Resource is AIProfile profile &&
            profile.TryGet<MemoryMetadata>(out var metadata) &&
            metadata.EnablePastConversations == true)
        {
            await ApplyPastConversationsAsync(context, profile, cancellationToken);
        }
    }

    private bool IsSessionMemoryEnabled(object resource)
    {
        return resource switch
        {
            AIProfile profile => profile.TryGet<MemoryMetadata>(out var metadata) && metadata.EnableSessionMemory == true,
            ChatInteraction => _chatInteractionMemoryOptions.CurrentValue.EnableSessionMemory,
            _ => false,
        };
    }

    private async Task ApplySessionMemoryAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken)
    {
        var orchestration = context.OrchestrationContext;
        var window = orchestration.CompletionContext.PastMessagesCount;

        // With no positive past-messages count the whole conversation is sent, so nothing is forgotten.
        if (window is not > 0)
        {
            return;
        }

        var messages = ConversationRecall.GetEligibleMessages(orchestration.ConversationHistory);
        var droppedCount = messages.Count - window.Value;

        if (droppedCount <= 0)
        {
            return;
        }

        var holder = GetMemoryHolder(context);
        var memory = holder is not null && holder.TryGet<AIConversationMemory>(out var stored) ? stored : new AIConversationMemory();

        // Fewer messages than the summary covers: the history was cleared or edited, so the summary no longer
        // describes it.
        if (memory.SummarizedMessageCount > droppedCount)
        {
            memory = new AIConversationMemory();
        }

        // Without somewhere to keep it, a summary would be rebuilt on every turn; show what fell out instead.
        if (holder is not null && droppedCount - memory.SummarizedMessageCount >= SummaryBatchSize)
        {
            memory = await UpdateSummaryAsync(context, holder, memory, messages, droppedCount, cancellationToken);
        }

        var pending = messages
            .Skip(memory.SummarizedMessageCount)
            .Take(droppedCount - memory.SummarizedMessageCount)
            .TakeLast(MaxVerbatimMessages)
            .Select(message => new Dictionary<string, object>
            {
                ["Role"] = message.Role == ChatRole.User ? "User" : "Assistant",
                ["Text"] = ConversationRecall.Truncate(message.Text),
            })
            .ToList();

        // Recall only from the turns that start before the verbatim messages; those are already in the prompt.
        var olderMessages = messages.Take(droppedCount).ToList();
        var firstVerbatimIndex = droppedCount - pending.Count;
        var recalled = ConversationRecall.Search(
            _tokenizer,
            ConversationRecall.GetTurns(olderMessages).Where(turn => turn.StartIndex < firstVerbatimIndex),
            [orchestration.UserMessage],
            MaxRecalledTurns);

        // Dictionaries rather than objects: Fluid only reads members of registered types.
        var arguments = new Dictionary<string, object>
        {
            ["summary"] = memory.Summary,
            ["pendingMessages"] = pending,
            ["recalledTurns"] = recalled.Select(turn => new Dictionary<string, object>
            {
                ["Number"] = turn.Number,
                ["UserText"] = turn.UserText,
                ["AssistantText"] = turn.AssistantText,
            }).ToList(),
        };

        if (!orchestration.DisableTools)
        {
            // The tool reads the older messages from the invocation scope, never from a store, so it can only
            // search the conversation it is running in.
            AIInvocationScope.Current?.Items.TryAdd(MemoryConstants.CompletionContextKeys.OlderConversationMessages, olderMessages);
            orchestration.CompletionContext.AdditionalProperties[AICompletionContextKeys.HasConversationMemory] = true;
            orchestration.MustIncludeTools.Add(SearchConversationTool.TheName);
            arguments["searchToolName"] = SearchConversationTool.TheName;
        }

        var block = await _templateService.RenderAsync(MemoryConstants.TemplateIds.ConversationMemoryContext, arguments, cancellationToken);

        if (!string.IsNullOrWhiteSpace(block))
        {
            orchestration.SystemMessageBuilder.AppendLine();
            orchestration.SystemMessageBuilder.AppendLine();
            orchestration.SystemMessageBuilder.Append(block);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Conversation memory: {DroppedCount} message(s) outside the window of {Window}; summary covers {SummarizedCount}, {PendingCount} shown verbatim, {RecalledCount} turn(s) recalled.",
                droppedCount,
                window.Value,
                memory.SummarizedMessageCount,
                pending.Count,
                recalled.Count);
        }
    }

    private async Task<AIConversationMemory> UpdateSummaryAsync(
        OrchestrationContextBuiltContext context,
        ExtensibleEntity holder,
        AIConversationMemory memory,
        List<ChatMessage> messages,
        int droppedCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var newMessages = messages.Skip(memory.SummarizedMessageCount).Take(droppedCount - memory.SummarizedMessageCount).ToList();
            var summary = await SummarizeAsync(context, memory.Summary, newMessages, cancellationToken);

            if (string.IsNullOrWhiteSpace(summary))
            {
                return memory;
            }

            var updated = new AIConversationMemory
            {
                Summary = summary.Trim(),
                SummarizedMessageCount = droppedCount,
                UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
            };

            holder.Put(updated);
            await PersistAsync(holder, cancellationToken);

            return updated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The turn still goes ahead with the older summary and the unsummarized messages shown verbatim.
            _logger.LogWarning(ex, "Failed to update the conversation summary; the previous summary is kept.");

            return memory;
        }
    }

    private async Task<string> SummarizeAsync(
        OrchestrationContextBuiltContext context,
        string existingSummary,
        List<ChatMessage> newMessages,
        CancellationToken cancellationToken)
    {
        var chatClient = await CreateUtilityChatClientAsync(context);

        if (chatClient is null)
        {
            _logger.LogWarning("Conversation memory could not resolve a utility deployment to summarize the conversation with.");

            return null;
        }

        var systemPrompt = await _templateService.RenderAsync(MemoryConstants.TemplateIds.ConversationSummarization, cancellationToken: cancellationToken);
        var input = new StringBuilder();
        input.AppendLine("Current summary:");
        input.AppendLine(string.IsNullOrWhiteSpace(existingSummary) ? "(none yet)" : existingSummary);
        input.AppendLine();
        input.AppendLine("Next messages:");

        foreach (var message in newMessages)
        {
            input.Append(message.Role == ChatRole.User ? "User: " : "Assistant: ");
            input.AppendLine(message.Text);
        }

        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = 1200,
        }.AddUsageTracking(context.OrchestrationContext.CompletionContext, purpose: AIUsagePurposes.ConversationSummarization);

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.System, systemPrompt), new ChatMessage(ChatRole.User, input.ToString())],
            chatOptions,
            cancellationToken);

        return response?.Text;
    }

    private async Task<IChatClient> CreateUtilityChatClientAsync(OrchestrationContextBuiltContext context)
    {
        var (utilityDeploymentName, chatDeploymentName) = context.Resource switch
        {
            AIProfile profile => (profile.UtilityDeploymentName, profile.ChatDeploymentName),
            ChatInteraction interaction => (interaction.UtilityDeploymentName, interaction.ChatDeploymentName),
            _ => (null, null),
        };

        var deployment = await _deploymentManager.ResolveUtilityOrDefaultAsync(
            utilityDeploymentName: utilityDeploymentName ?? context.OrchestrationContext.CompletionContext.UtilityDeploymentName,
            chatDeploymentName: chatDeploymentName ?? context.OrchestrationContext.CompletionContext.ChatDeploymentName);

        if (deployment is null || string.IsNullOrEmpty(deployment.ConnectionName) || string.IsNullOrEmpty(deployment.ModelName))
        {
            return null;
        }

        var parameterApplier = _serviceProvider.GetService<IAIDeploymentParameterApplier>();

        return await _clientFactory.CreateChatClientAsync(
            deployment,
            builder => builder
                .UseDefaultResilience()
                .UseUtilityModelParameters(parameterApplier, deployment, context.Resource as AIProfile));
    }

    private static ExtensibleEntity GetMemoryHolder(OrchestrationContextBuiltContext context)
    {
        if (context.Resource is ChatInteraction interaction)
        {
            return interaction;
        }

        if (context.OrchestrationContext.CompletionContext.AdditionalProperties.TryGetValue(AICompletionContextKeys.Session, out var value) &&
            value is AIChatSession session)
        {
            return session;
        }

        return AIInvocationScope.Current?.ChatSession;
    }

    private async Task PersistAsync(ExtensibleEntity holder, CancellationToken cancellationToken)
    {
        // A chat session is saved by the hub at the end of the turn; an interaction is only saved there when its
        // title changes, so save it here.
        if (holder is ChatInteraction interaction)
        {
            var manager = _serviceProvider.GetService<ICatalogManager<ChatInteraction>>();

            if (manager is not null)
            {
                await manager.UpdateAsync(interaction, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task ApplyPastConversationsAsync(OrchestrationContextBuiltContext context, AIProfile profile, CancellationToken cancellationToken)
    {
        var orchestration = context.OrchestrationContext;

        // Past conversations belong to a signed-in user; a visitor has none that can be tied to them safely.
        var userId = AIMemoryOrchestrationContextHelper.GetAuthenticatedUserId(_httpContextAccessor);

        if (string.IsNullOrEmpty(userId) || orchestration.DisableTools || AIInvocationScope.Current is null)
        {
            return;
        }

        var currentSessionId = (GetMemoryHolder(context) as AIChatSession)?.SessionId;
        AIInvocationScope.Current.Items[MemoryConstants.CompletionContextKeys.PastConversationsScope] = new PastConversationsScope(userId, profile.ItemId, currentSessionId);
        orchestration.CompletionContext.AdditionalProperties[AICompletionContextKeys.HasPastConversations] = true;
        orchestration.MustIncludeTools.Add(SearchPastConversationsTool.TheName);
        orchestration.MustIncludeTools.Add(ListRecentConversationsTool.TheName);

        var instructions = await _templateService.RenderAsync(
            MemoryConstants.TemplateIds.PastConversationsAvailability,
            new Dictionary<string, object>
            {
                ["searchToolName"] = SearchPastConversationsTool.TheName,
                ["listToolName"] = ListRecentConversationsTool.TheName,
            },
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            orchestration.SystemMessageBuilder.AppendLine();
            orchestration.SystemMessageBuilder.AppendLine();
            orchestration.SystemMessageBuilder.Append(instructions);
        }
    }
}
