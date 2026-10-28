using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Resilience;
using CrestApps.Core.AI.Security;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Guardrails;

/// <summary>
/// Checks what the customer says, and the AI's replies, against the topics an AI profile refuses, with the utility
/// model. The chat hub runs it on every typed message and reply; hosts call it from their other channels, such as
/// text messages and turn-based calls.
/// </summary>
public sealed class AIGuardrailService
{
    /// <summary>
    /// What the AI says when a guardrail triggers and the profile does not say otherwise.
    /// </summary>
    public const string DefaultPlaceholderReply = "I'm sorry, I can't help with that. Is there something else I can help you with?";

    /// <summary>
    /// The prefix of the <see cref="PromptSecurityResult.DetectionRule"/> of a result a guardrail blocked; the topic's
    /// template identifier follows it.
    /// </summary>
    public const string DetectionRulePrefix = "guardrail:";

    internal const string SystemPrompt =
        """
        You are a content moderator for a customer service assistant. You are given a message and the topics the
        business does not allow. Decide whether the message falls under one of those topics. Judge what the message
        actually asks for or says, not words taken out of context: a customer asking about a refund for a damaged
        knife is not violence, and "this is killing me" about a late order is not self-harm. Ordinary questions about
        the business's products, orders, accounts and policies are always allowed.
        Answer only with JSON: {"violates": false, "category": "", "reason": ""}, where category is the identifier of
        the topic shown in brackets and reason is one short sentence.
        """;

    private const int MaxCheckedLength = 4000;

    private static readonly JsonSerializerOptions _jsonOptions = JsonSerializerOptions.Web;

    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _clientFactory;
    private readonly ITemplateService _templateService;
    private readonly IEnumerable<IAIGuardrailHandler> _handlers;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIGuardrailService"/> class.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager the utility model is resolved from.</param>
    /// <param name="clientFactory">The AI client factory.</param>
    /// <param name="templateService">The template service the topics are read from.</param>
    /// <param name="handlers">The handlers told when a guardrail triggers.</param>
    /// <param name="logger">The logger.</param>
    public AIGuardrailService(
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ITemplateService templateService,
        IEnumerable<IAIGuardrailHandler> handlers,
        ILogger<AIGuardrailService> logger)
    {
        _deploymentManager = deploymentManager;
        _clientFactory = clientFactory;
        _templateService = templateService;
        _handlers = handlers;
        _logger = logger;
    }

    /// <summary>
    /// Gets whether a profile checks this side of the conversation.
    /// </summary>
    /// <param name="profile">The AI profile.</param>
    /// <param name="direction">The side.</param>
    /// <returns><see langword="true"/> when the profile refuses topics and checks this side.</returns>
    public static bool IsOn(AIProfile profile, AIGuardrailDirection direction)
    {
        if (profile is null || !profile.TryGetSettings<AIProfileGuardrailSettings>(out var settings) || !settings.HasTopics)
        {
            return false;
        }

        return direction == AIGuardrailDirection.Customer
            ? settings.CheckCustomer
            : settings.CheckReplies;
    }

    /// <summary>
    /// Gets what the AI says when a guardrail of the profile triggers.
    /// </summary>
    /// <param name="profile">The AI profile.</param>
    /// <returns>The profile's placeholder reply, or <see cref="DefaultPlaceholderReply"/>.</returns>
    public static string GetPlaceholderReply(AIProfile profile)
    {
        var reply = profile?.GetSettings<AIProfileGuardrailSettings>()?.PlaceholderReply;

        return string.IsNullOrWhiteSpace(reply)
            ? DefaultPlaceholderReply
            : reply.Trim();
    }

    /// <summary>
    /// Turns a verdict into a blocked prompt security result, so a guardrail blocks a message the way prompt security
    /// does.
    /// </summary>
    /// <param name="verdict">The guardrail that triggered.</param>
    /// <returns>The blocked result.</returns>
    public static PromptSecurityResult ToSecurityResult(AIGuardrailVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return PromptSecurityResult.Blocked(verdict.Reason, PromptRiskLevel.High, DetectionRulePrefix + verdict.Category);
    }

    /// <summary>
    /// Gets whether a prompt security result was blocked by a guardrail.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns><see langword="true"/> when a guardrail blocked it.</returns>
    public static bool IsGuardrailResult(PromptSecurityResult result)
    {
        return result?.DetectionRule is not null &&
            result.DetectionRule.StartsWith(DetectionRulePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks a text against the profile's guardrails.
    /// </summary>
    /// <param name="profile">The AI profile.</param>
    /// <param name="text">What the customer said, or the AI's reply.</param>
    /// <param name="direction">Which of the two it is.</param>
    /// <param name="sessionId">The chat session, when the conversation has one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The guardrail that triggered, or <see langword="null"/> when the text may go on.</returns>
    public async Task<AIGuardrailVerdict> CheckAsync(AIProfile profile, string text, AIGuardrailDirection direction, string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsOn(profile, direction))
        {
            return null;
        }

        var settings = profile.GetSettings<AIProfileGuardrailSettings>();
        IReadOnlyList<AIGuardrailTopic> topics;
        AIGuardrailJudgement judgement;

        try
        {
            topics = await LoadTopicsAsync(settings.Topics, cancellationToken);

            if (topics.Count == 0 && string.IsNullOrWhiteSpace(settings.OtherTopics))
            {
                return null;
            }

            var deployment = await _deploymentManager.ResolveUtilityOrDefaultAsync(
                utilityDeploymentName: profile.UtilityDeploymentName,
                chatDeploymentName: profile.ChatDeploymentName);

            if (deployment is null)
            {
                return null;
            }

            var client = await _clientFactory.CreateChatClientAsync(deployment, builder => builder
                .UseDefaultResilience()
                .UseUsageLabels(purpose: AIUsagePurposes.Guardrail));

            var response = await client.GetResponseAsync<AIGuardrailJudgement>(
                [
                    new ChatMessage(ChatRole.System, SystemPrompt),
                    new ChatMessage(ChatRole.User, BuildRequest(topics, settings.OtherTopics, text, direction)),
                ],
                _jsonOptions,
                cancellationToken: cancellationToken);

            judgement = response.Result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The conversation goes on unchecked rather than failing: a guardrail is a second line behind the
            // instructions and prompt security, and a customer left without an answer is worse than one unchecked turn.
            _logger.LogWarning(ex, "The guardrails of AI profile '{ProfileId}' could not be checked.", profile.ItemId);

            return null;
        }

        var verdict = ToVerdict(judgement, topics, settings.OtherTopics, profile, text, direction, sessionId);

        if (verdict is null)
        {
            return null;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "A guardrail of AI profile '{ProfileId}' triggered on the {Direction} in session '{SessionId}': {Category}.",
                profile.ItemId,
                direction,
                sessionId,
                verdict.Category);
        }

        foreach (var handler in _handlers)
        {
            try
            {
                await handler.TriggeredAsync(verdict, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A guardrail handler failed for AI profile '{ProfileId}'.", profile.ItemId);
            }
        }

        return verdict;
    }

    /// <summary>
    /// Reads the definitions of the chosen topics from their templates. A topic whose template was deleted, or moved
    /// out of the guardrail topic category, is left out.
    /// </summary>
    /// <param name="ids">The chosen topics.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The topics, in the order chosen.</returns>
    public async Task<IReadOnlyList<AIGuardrailTopic>> LoadTopicsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var chosen = AIGuardrailTopics.Normalize(ids);

        if (chosen.Length == 0)
        {
            return [];
        }

        var templates = (await _templateService.ListByCategoryAsync(AIGuardrailTopics.Category, cancellationToken))
            .ToDictionary(template => template.Id, StringComparer.OrdinalIgnoreCase);
        var topics = new List<AIGuardrailTopic>();

        foreach (var id in chosen)
        {
            if (!templates.TryGetValue(id, out var template))
            {
                _logger.LogWarning("The guardrail topic '{Topic}' has no template in the '{Category}' category and is skipped.", id, AIGuardrailTopics.Category);

                continue;
            }

            var definition = await _templateService.RenderAsync(template.Id, cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(definition))
            {
                topics.Add(new AIGuardrailTopic(template.Id, AITemplateCategoryExtensions.GetTitle(template), definition.Trim()));
            }
        }

        return topics;
    }

    internal static string BuildRequest(IReadOnlyList<AIGuardrailTopic> topics, string otherTopics, string text, AIGuardrailDirection direction)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Topics not allowed:");

        foreach (var topic in topics ?? [])
        {
            builder.Append("- [").Append(topic.Id).Append("] ").Append(topic.Title).Append(": ").AppendLine(topic.Definition.ReplaceLineEndings(" "));
        }

        if (!string.IsNullOrWhiteSpace(otherTopics))
        {
            builder.Append("- [").Append(AIGuardrailTopics.Other).Append("] ").AppendLine(otherTopics.Trim().ReplaceLineEndings(" "));
        }

        builder.AppendLine();
        builder.AppendLine(direction == AIGuardrailDirection.Customer
            ? "The customer's message:"
            : "The assistant's reply, which must not provide or engage with a topic that is not allowed:");

        builder.AppendLine(text.Length > MaxCheckedLength ? text[..MaxCheckedLength] : text);

        return builder.ToString();
    }

    internal static AIGuardrailVerdict ToVerdict(AIGuardrailJudgement judgement, IReadOnlyList<AIGuardrailTopic> topics, string otherTopics, AIProfile profile, string text, AIGuardrailDirection direction, string sessionId)
    {
        if (judgement is null || !judgement.Violates)
        {
            return null;
        }

        var named = judgement.Category?.Trim().Trim('[', ']');
        var topic = (topics ?? []).FirstOrDefault(topic => string.Equals(topic.Id, named, StringComparison.OrdinalIgnoreCase));
        string category;
        string title;

        if (topic is not null)
        {
            category = topic.Id;
            title = topic.Title;
        }
        else
        {
            // The model named a topic the profile does not refuse. Only the profile's own description of other
            // topics can account for that; without one the message is allowed, so a guardrail never refuses more
            // than it was set to.
            if (string.IsNullOrWhiteSpace(otherTopics))
            {
                return null;
            }

            category = AIGuardrailTopics.Other;
            title = "Other topics";
        }

        return new AIGuardrailVerdict
        {
            ProfileId = profile.ItemId,
            SessionId = sessionId,
            Direction = direction,
            Category = category,
            Topic = title,
            Reason = judgement.Reason?.Trim(),
            Text = text,
            Reply = GetPlaceholderReply(profile),
        };
    }
}
