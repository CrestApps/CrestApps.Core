using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Resilience;
using CrestApps.Core.Extensions;
using CrestApps.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Chat.Privacy;

/// <summary>
/// Applies an AI profile's storage mode to its conversations once they have ended, and deletes conversations past
/// the profile's retention. Hosts call <see cref="ProcessAsync"/> on a schedule, once per profile, and keep the cursor
/// it returns.
/// </summary>
public sealed class AIConversationPrivacyService
{
    internal const string FindingPrompt =
        """
        You find personal details in a conversation between a customer and a business so they can be redacted.
        List every place the text names a private person (each full name, and each first or last name used on its own), a
        street address or part of one, a date of birth, or an account, member, policy, driver's license, passport or
        other identity number. Do not list the business, its products, its staff's first names, the assistant's own
        name, cities or countries on their own, order numbers, prices or dates that are not a date of birth.
        Copy each detail exactly as it appears in the text.
        Answer only with JSON: {"items": [{"text": "", "kind": "name|address|birthdate|id"}]}.
        """;

    private const int MaxTextForFinding = 24_000;

    private static readonly JsonSerializerOptions _jsonOptions = JsonSerializerOptions.Web;

    private readonly IAIChatSessionStore _sessionStore;
    private readonly IAIChatSessionPromptStore _promptStore;
    private readonly IEnumerable<IAIChatSessionHandler> _sessionHandlers;
    private readonly IEnumerable<IAIConversationPrivacyHandler> _privacyHandlers;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _clientFactory;
    private readonly AIConversationPrivacyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIConversationPrivacyService"/> class.
    /// </summary>
    /// <param name="sessionStore">The unscoped chat session store.</param>
    /// <param name="promptStore">The prompt store.</param>
    /// <param name="sessionHandlers">The chat session handlers told of a deletion.</param>
    /// <param name="privacyHandlers">The handlers that redact, clear or delete the other data kept about a conversation.</param>
    /// <param name="deploymentManager">The deployment manager the utility model is resolved from.</param>
    /// <param name="clientFactory">The AI client factory.</param>
    /// <param name="options">The options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public AIConversationPrivacyService(
        IAIChatSessionStore sessionStore,
        IAIChatSessionPromptStore promptStore,
        IEnumerable<IAIChatSessionHandler> sessionHandlers,
        IEnumerable<IAIConversationPrivacyHandler> privacyHandlers,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        IOptions<AIConversationPrivacyOptions> options,
        TimeProvider timeProvider,
        ILogger<AIConversationPrivacyService> logger)
    {
        _sessionStore = sessionStore;
        _promptStore = promptStore;
        _sessionHandlers = sessionHandlers;
        _privacyHandlers = privacyHandlers;
        _deploymentManager = deploymentManager;
        _clientFactory = clientFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Applies the profile's storage mode to its conversations that ended since <paramref name="cursor"/>, and deletes
    /// its conversations past its retention, at most <see cref="AIConversationPrivacyOptions.BatchSize"/> of each.
    /// Conversations are walked in the order of their last activity, stopping at the first one still going on or still
    /// being processed, so none is passed over. Nothing is committed; the host commits its unit of work afterwards.
    /// </summary>
    /// <param name="profile">The AI profile.</param>
    /// <param name="cursor">The cursor the previous call returned for this profile, or <see langword="null"/> on the
    /// first call, which starts from <see cref="AIProfileDataPrivacySettings.StorageModeSinceUtc"/> so conversations
    /// kept before the mode was chosen are left as they are.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was done, and the cursor to pass next time.</returns>
    public async Task<AIConversationPrivacyResult> ProcessAsync(AIProfile profile, DateTime? cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var settings = profile.GetSettings<AIProfileDataPrivacySettings>();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var result = new AIConversationPrivacyResult();

        if (settings.StorageMode != AIDataStorageMode.Everything)
        {
            var position = cursor ?? settings.StorageModeSinceUtc ?? utcNow;
            var conversations = await _sessionStore.ListByLastActivityAsync(profile.ItemId, position, utcNow - _options.SettleTime, _options.BatchSize, cancellationToken);

            foreach (var conversation in conversations)
            {
                if (cancellationToken.IsCancellationRequested || !IsFinished(conversation))
                {
                    break;
                }

                try
                {
                    if (await ApplyAsync(conversation, settings.StorageMode, cancellationToken))
                    {
                        result.Applied++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Most likely the utility model is unavailable. The cursor stays, so the conversation is redacted on
                    // a later run rather than kept as it is.
                    _logger.LogError(ex, "Applying the storage mode of AI profile '{ProfileId}' to session '{SessionId}' failed.", profile.ItemId, conversation.SessionId);

                    break;
                }

                position = conversation.LastActivityUtc;
            }

            result.Cursor = position;
        }

        if (settings.RetentionDays > 0)
        {
            var expired = await _sessionStore.ListByLastActivityAsync(profile.ItemId, null, utcNow.AddDays(-settings.RetentionDays.Value), _options.BatchSize, cancellationToken);

            foreach (var conversation in expired)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (await DeleteAsync(conversation.SessionId, cancellationToken))
                {
                    result.Deleted++;
                }
            }

            if (result.Deleted > 0 && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Deleted {Count} AI sessions of profile '{ProfileId}' past its retention.", result.Deleted, profile.ItemId);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies a storage mode to a conversation. A conversation it was applied to already is left alone.
    /// </summary>
    /// <param name="session">The conversation.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether anything was changed.</returns>
    /// <exception cref="Exception">The utility model could not be asked for the personal details; nothing is changed.</exception>
    public async Task<bool> ApplyAsync(AIChatSession session, AIDataStorageMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (mode == AIDataStorageMode.Everything || session.TryGet<AIConversationPrivacyMetadata>(out _))
        {
            return false;
        }

        var prompts = await _promptStore.GetPromptsAsync(session.SessionId);
        var context = new AIConversationPrivacyContext(session, mode);

        foreach (var handler in _privacyHandlers)
        {
            await handler.CollectingAsync(context, cancellationToken);
        }

        if (mode == AIDataStorageMode.MetadataOnly)
        {
            await _promptStore.DeleteAllPromptsAsync(session.SessionId);
            Strip(session);
        }
        else
        {
            // Everything is read first and the details found in all of it at once, so a name the customer gave in
            // the first turn is also taken out of the summary written from it.
            context.Terms = await FindPersonalDataAsync(CollectText(session, prompts, context.Texts), cancellationToken);

            foreach (var prompt in prompts)
            {
                var redacted = context.Redact(prompt.Content);

                if (!string.Equals(redacted, prompt.Content, StringComparison.Ordinal))
                {
                    prompt.Content = redacted;
                    await _promptStore.UpdateAsync(prompt, cancellationToken);
                }
            }

            Redact(session, context.Terms);
        }

        foreach (var handler in _privacyHandlers)
        {
            await handler.ApplyingAsync(context, cancellationToken);
        }

        session.RemoteAddress = null;
        session.Put(new AIConversationPrivacyMetadata
        {
            Mode = mode,
            AppliedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        });

        await _sessionStore.SaveAsync(session, cancellationToken);

        return true;
    }

    /// <summary>
    /// Deletes a conversation of any user, visitor or system channel, with its prompts and whatever the chat session
    /// handlers and privacy handlers delete with it, such as its documents.
    /// </summary>
    /// <param name="sessionId">The conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the conversation existed.</returns>
    public async Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var chatSession = await _sessionStore.FindByIdAsync(sessionId, cancellationToken);

        if (chatSession is null)
        {
            return false;
        }

        await _sessionHandlers.InvokeAsync((handler, context) => handler.DeletingAsync(context, cancellationToken), new DeletingContext<AIChatSession>(chatSession), _logger);

        foreach (var handler in _privacyHandlers)
        {
            await handler.DeletingAsync(chatSession, cancellationToken);
        }

        await _sessionStore.DeleteAsync(sessionId, cancellationToken);

        await _sessionHandlers.InvokeAsync((handler, context) => handler.DeletedAsync(context, cancellationToken), new DeletedContext<AIChatSession>(chatSession), _logger);

        return true;
    }

    /// <summary>
    /// Asks the utility model for the names, street addresses, dates of birth and identity numbers in a text, which
    /// patterns can't recognize. Without a utility deployment only the patterns are used.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The details found, with each part of a found full name as a name of its own.</returns>
    public async Task<IReadOnlyList<AIPersonalDataTerm>> FindPersonalDataAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var deployment = await _deploymentManager.ResolveUtilityOrDefaultAsync();

        if (deployment is null)
        {
            return [];
        }

        var client = await _clientFactory.CreateChatClientAsync(deployment, builder => builder
            .UseDefaultResilience()
            .UseUsageLabels(purpose: AIUsagePurposes.PersonalDataRedaction));

        var response = await client.GetResponseAsync<AIPersonalDataFindings>(
            [
                new ChatMessage(ChatRole.System, FindingPrompt),
                new ChatMessage(ChatRole.User, text.Length > MaxTextForFinding ? text[..MaxTextForFinding] : text),
            ],
            _jsonOptions,
            cancellationToken: cancellationToken);

        return WithNameParts(response.Result?.Items ?? []);
    }

    /// <summary>
    /// Gets whether a conversation has ended and its post-session processing is done, so its storage mode may be
    /// applied.
    /// </summary>
    /// <param name="conversation">The conversation.</param>
    /// <returns><see langword="true"/> when the conversation is finished.</returns>
    public static bool IsFinished(AIChatSession conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return conversation.Status != ChatSessionStatus.Active &&
            conversation.PostSessionProcessingStatus != PostSessionProcessingStatus.Pending;
    }

    /// <summary>
    /// Adds each part of a found full name as a name of its own, because a conversation that learns "Marcus Bell"
    /// goes on to say "Marcus".
    /// </summary>
    /// <param name="terms">The details found.</param>
    /// <returns>The details with the name parts.</returns>
    internal static IReadOnlyList<AIPersonalDataTerm> WithNameParts(IEnumerable<AIPersonalDataTerm> terms)
    {
        var found = terms.Where(term => !string.IsNullOrWhiteSpace(term?.Text)).ToList();
        var parts = found
            .Where(term => string.Equals(term.Kind?.Trim(), "name", StringComparison.OrdinalIgnoreCase))
            .SelectMany(term => term.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(part => part.Trim(',', '.', '\'', '"'))
            .Where(part => part.Length >= 3 && char.IsUpper(part[0]))
            .Where(part => !found.Any(term => string.Equals(term.Text.Trim(), part, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(part => new AIPersonalDataTerm { Text = part, Kind = "name" });

        return [.. found, .. parts];
    }

    internal static string CollectText(AIChatSession session, IEnumerable<AIChatSessionPrompt> prompts, IEnumerable<string> texts)
    {
        var builder = new StringBuilder();
        Append(builder, session.Title);

        foreach (var prompt in prompts ?? [])
        {
            Append(builder, prompt.Content);
        }

        foreach (var result in session.PostSessionResults.Values)
        {
            Append(builder, result?.Value);
        }

        foreach (var field in session.ExtractedData.Values)
        {
            foreach (var value in field?.Values ?? [])
            {
                Append(builder, value);
            }
        }

        if (session.TryGet<AIConversationMemory>(out var memory))
        {
            Append(builder, memory.Summary);
        }

        foreach (var text in texts ?? [])
        {
            Append(builder, text);
        }

        return builder.ToString();
    }

    internal static void Redact(AIChatSession session, IReadOnlyList<AIPersonalDataTerm> terms)
    {
        session.Title = AIPersonalDataRedactor.Redact(session.Title, terms);

        foreach (var result in session.PostSessionResults.Values.Where(result => result is not null))
        {
            result.Value = AIPersonalDataRedactor.Redact(result.Value, terms);
        }

        foreach (var field in session.ExtractedData.Values.Where(field => field?.Values is not null))
        {
            field.Values = field.Values.Select(value => AIPersonalDataRedactor.Redact(value, terms)).ToList();
        }

        // The running summary of a long conversation repeats what the transcript says, so it is redacted with it.
        if (session.TryGet<AIConversationMemory>(out var memory) && !string.IsNullOrEmpty(memory.Summary))
        {
            memory.Summary = AIPersonalDataRedactor.Redact(memory.Summary, terms);
            session.Put(memory);
        }
    }

    internal static void Strip(AIChatSession session)
    {
        session.Title = null;

        foreach (var result in session.PostSessionResults.Values.Where(result => result is not null))
        {
            result.Value = null;
        }

        foreach (var field in session.ExtractedData.Values.Where(field => field is not null))
        {
            field.Values = [];
        }

        // With the transcript gone, the summary of it must go too.
        session.Remove<AIConversationMemory>();
    }

    private static void Append(StringBuilder builder, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.AppendLine(text);
        }
    }

    private sealed class AIPersonalDataFindings
    {
        public List<AIPersonalDataTerm> Items { get; set; } = [];
    }
}
