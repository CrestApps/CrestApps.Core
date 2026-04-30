using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Channels;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Exceptions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Extensions;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Rendering;
using CrestApps.Core.Templates.Services;
using Cysharp.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
namespace CrestApps.Core.AI.Chat.Hubs;

/// <summary>
/// Core SignalR hub for AI chat sessions. Provides streaming message delivery,
/// session management, message rating, handler transfer, conversation mode support,
/// and notification action dispatch.
/// <para>
/// All public hub methods are <c>virtual</c> so that framework-specific subclasses
/// (e.g., OrchardCore) can wrap each call with their own scoping or authorization
/// logic and then call the base implementation.
/// </para>
/// </summary>
public class AIChatHubCore<TClient> : Hub<TClient>
    where TClient : class, IAIChatHubClient
{
    private const string _conversationCtsKey = "ConversationCts";

    private readonly IServiceProvider _services;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatHubCore"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    protected AIChatHubCore(
        IServiceProvider services,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _services = services;
        _timeProvider = timeProvider;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    /// <summary>
    /// Executes an action within a service scope. Override in OrchardCore to use
    /// <c>ShellScope.UsingChildScopeAsync</c> so that each hub invocation gets
    /// its own <c>ISession</c> / <c>IDocumentStore</c> lifecycle.
    /// </summary>
    /// <param name="action">The action.</param>
    protected virtual Task ExecuteInScopeAsync(Func<IServiceProvider, Task> action)
    {
        return action(_services);
    }

    /// <summary>
    /// Gets the chat context type for this hub. Override when using a different
    /// chat context type (e.g., <see cref="ChatContextType.ChatInteraction"/>).
    /// </summary>
    protected virtual ChatContextType GetChatContextType()
    {
        return ChatContextType.AIChatSession;
    }

    /// <summary>
    /// Gets the current UTC time. Override to use a framework-specific time
    /// abstraction (e.g., <c>IClock</c>).
    /// </summary>
    protected virtual DateTime GetUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Generates a unique identifier. Override to use a framework-specific
    /// ID generator (e.g., OrchardCore's <c>IdGenerator</c>).
    /// </summary>
    protected virtual string GenerateId()
    {
        return UniqueId.GenerateId();
    }

    /// <summary>
    /// Returns the default blank session title used when no title can be determined.
    /// </summary>
    protected virtual string DefaultBlankSessionTitle => "Untitled";

    /// <summary>
    /// Returns the error message used when a required field is missing.
    /// </summary>
    /// <param name="fieldName">The field name.</param>
    protected virtual string GetRequiredFieldMessage(string fieldName)
    {
        return $"{fieldName} is required.";
    }

    /// <summary>
    /// Gets profile not found message.
    /// </summary>
    protected virtual string GetProfileNotFoundMessage()
    {
        return "Profile not found.";
    }

    /// <summary>
    /// Gets session not found message.
    /// </summary>
    protected virtual string GetSessionNotFoundMessage()
    {
        return "Session not found.";
    }

    /// <summary>
    /// Gets not authorized message.
    /// </summary>
    protected virtual string GetNotAuthorizedMessage()
    {
        return "You are not authorized to interact with the given profile.";
    }

    /// <summary>
    /// Gets friendly error message.
    /// </summary>
    /// <param name="ex">The ex.</param>
    protected virtual string GetFriendlyErrorMessage(Exception ex)
    {
        if (AIHubErrorMessageHelper.IsInvalidChatModelSettingsFailure(ex))
        {
            return GetInvalidChatModelSettingsMessage();
        }

        return "An error occurred processing your message.";
    }

    /// <summary>
    /// Gets invalid chat model settings message.
    /// </summary>
    protected virtual string GetInvalidChatModelSettingsMessage()
    {
        return "The chat model settings are missing or invalid. Update the Chat model in the AI Profile or the global AI settings.";
    }

    /// <summary>
    /// Gets only chat profiles message.
    /// </summary>
    protected virtual string GetOnlyChatProfilesMessage()
    {
        return "Only chat profiles can start chat sessions.";
    }

    /// <summary>
    /// Gets conversation not enabled message.
    /// </summary>
    protected virtual string GetConversationNotEnabledMessage()
    {
        return "Conversation mode is not enabled for this profile.";
    }

    /// <summary>
    /// Gets no stt deployment message.
    /// </summary>
    protected virtual string GetNoSttDeploymentMessage()
    {
        return "No speech-to-text deployment is configured.";
    }

    /// <summary>
    /// Gets no tts deployment message.
    /// </summary>
    protected virtual string GetNoTtsDeploymentMessage()
    {
        return "No text-to-speech deployment is configured.";
    }

    /// <summary>
    /// Gets stt deployment not found message.
    /// </summary>
    protected virtual string GetSttDeploymentNotFoundMessage()
    {
        return "The configured speech-to-text deployment was not found.";
    }

    /// <summary>
    /// Gets tts deployment not found message.
    /// </summary>
    protected virtual string GetTtsDeploymentNotFoundMessage()
    {
        return "The configured text-to-speech deployment was not found.";
    }

    /// <summary>
    /// Gets tts not enabled message.
    /// </summary>
    protected virtual string GetTtsNotEnabledMessage()
    {
        return "Text-to-speech is not enabled for this profile.";
    }

    /// <summary>
    /// Gets conversation error message.
    /// </summary>
    protected virtual string GetConversationErrorMessage()
    {
        return "An error occurred during the conversation. Please try again.";
    }

    /// <summary>
    /// Gets notification action error message.
    /// </summary>
    protected virtual string GetNotificationActionErrorMessage()
    {
        return "An error occurred while processing your action. Please try again.";
    }

    /// <summary>
    /// Gets transcription error message.
    /// </summary>
    /// <param name="ex">The ex.</param>
    protected virtual string GetTranscriptionErrorMessage(Exception ex = null)
    {
        return IsSpeechAuthenticationFailure(ex) ? "Speech-to-text authentication failed. Check the configured speech deployment credentials and region." : "An error occurred while transcribing the audio. Please try again.";
    }

    /// <summary>
    /// Gets speech synthesis error message.
    /// </summary>
    /// <param name="ex">The ex.</param>
    protected virtual string GetSpeechSynthesisErrorMessage(Exception ex = null)
    {
        return IsSpeechAuthenticationFailure(ex) ? "Text-to-speech authentication failed. Check the configured speech deployment credentials and region." : "An error occurred while synthesizing speech. Please try again.";
    }

    /// <summary>
    /// Checks whether the current caller is authorized to use the given profile.
    /// Override to perform framework-specific authorization checks.
    /// The default implementation always returns <c>true</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="profile">The profile.</param>
    protected virtual Task<bool> AuthorizeProfileAsync(IServiceProvider services, AIProfile profile)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Called after a streaming response has been fully collected and saved.
    /// Override to perform analytics, citation collection, or workflow triggers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="context">The context.</param>
    protected virtual Task OnMessageCompletedAsync(IServiceProvider services, ChatMessageCompletedContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Collects references (citations) during streaming. Called after each chunk
    /// and once after the stream ends. Override to integrate citation collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="handlerContext">The handler context.</param>
    /// <param name="references">The references.</param>
    /// <param name="contentItemIds">The content item ids.</param>
    protected virtual void CollectStreamingReferences(IServiceProvider services, ChatResponseHandlerContext handlerContext, Dictionary<string, AICompletionReference> references, HashSet<string> contentItemIds)
    {
        // No-op. OC overrides to use CitationReferenceCollector.
    }

    /// <summary>
    /// Generates a title for a new session. The default implementation uses AI
    /// title generation when configured on the profile, falling back to a
    /// truncated user prompt.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="userPrompt">The user prompt.</param>
    protected virtual async Task<string> GenerateSessionTitleAsync(IServiceProvider services, AIProfile profile, string userPrompt)
    {
        var titleUserPrompt = BuildTitleUserPrompt(profile, userPrompt);
        if (profile.TitleType == AISessionTitleType.Generated)
        {
            var generated = await GetAIGeneratedTitleAsync(services, profile, titleUserPrompt);
            if (!string.IsNullOrEmpty(generated))
            {
                return generated;
            }
        }

        return Truncate(titleUserPrompt, 255);
    }

    private static string Truncate(string value, int maxLength)
    {
        return string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool IsSpeechAuthenticationFailure(Exception ex)
    {
        var message = ex?.ToString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("AuthenticationFailure", StringComparison.OrdinalIgnoreCase) || message.Contains("Authentication error (401)", StringComparison.OrdinalIgnoreCase) || message.Contains("check subscription information and region name", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetAIGeneratedTitleAsync(IServiceProvider services, AIProfile profile, string userPrompt)
    {
        var aiTemplateService = services.GetService<ITemplateService>();
        var completionContextBuilder = services.GetService<IAICompletionContextBuilder>();
        var completionService = services.GetService<IAICompletionService>();
        if (aiTemplateService is null || completionContextBuilder is null || completionService is null)
        {
            return null;
        }

        var titleSystemMessage = await aiTemplateService.RenderAsync(AITemplateIds.TitleGeneration);
        var context = await completionContextBuilder.BuildAsync(profile, c =>
        {
            c.SystemMessage = titleSystemMessage;
            c.FrequencyPenalty = 0;
            c.PresencePenalty = 0;
            c.TopP = 1;
            c.Temperature = 0;
            c.MaxTokens = 64;
            c.DataSourceId = null;
            c.DisableTools = true;
        });
        var deploymentManager = services.GetService<IAIDeploymentManager>();
        if (deploymentManager is null)
        {
            return null;
        }

        var chatDeployment = await deploymentManager.ResolveUtilityOrDefaultAsync(utilityDeploymentName: context.UtilityDeploymentName, chatDeploymentName: context.ChatDeploymentName);
        if (chatDeployment is null)
        {
            return null;
        }

        var titleResponse = await completionService.CompleteAsync(chatDeployment, [new(ChatRole.User, userPrompt),], context);

        return titleResponse.Messages.Count > 0 ? Truncate(titleResponse.Messages.First().Text, 255) : null;
    }

    /// <summary>
    /// Builds title user prompt.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="userPrompt">The user prompt.</param>
    protected static string BuildTitleUserPrompt(AIProfile profile, string userPrompt)
    {
        var trimmedUserPrompt = userPrompt?.Trim();

        if (profile.TryGet<AIProfileMetadata>(out var profileMetadata))
        {
            var initialPrompt = profileMetadata.InitialPrompt?.Trim();
            if (!string.IsNullOrWhiteSpace(initialPrompt))
            {
                return string.IsNullOrWhiteSpace(trimmedUserPrompt) ? initialPrompt : $"{initialPrompt}\n\n{trimmedUserPrompt}";
            }
        }

        return trimmedUserPrompt;
    }

    /// <summary>
    /// Gets the SignalR group name for a chat session. Clients in this group
    /// receive deferred responses delivered via webhook or external callback.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public static string GetSessionGroupName(string sessionId)
    {
        return $"aichat-session-{sessionId}";
    }

    /// <summary>
    /// Resolves the deployment settings for speech services. Override in
    /// OrchardCore to read from ISiteService instead of IOptionsMonitor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    protected virtual Task<DefaultAIDeploymentSettings> GetDeploymentSettingsAsync(IServiceProvider services)
    {
        var options = services.GetService<IOptionsMonitor<DefaultAIDeploymentSettings>>();

        return Task.FromResult(options?.CurrentValue ?? new DefaultAIDeploymentSettings());
    }

    /// <summary>
    /// Streams a chat response for the given prompt. Creates a new session on the
    /// fly when <paramref name = "sessionId"/> is empty.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="sessionProfileId">The session profile id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual ChannelReader<CompletionPartialMessage> SendMessage(string profileId, string prompt, string sessionId, string sessionProfileId, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<CompletionPartialMessage>();
        _ = ExecuteInScopeAsync(services => HandleSendMessageAsync(channel.Writer, services, profileId, prompt, sessionId, sessionProfileId, cancellationToken));

        return channel.Reader;
    }

    /// <summary>
    /// Loads an existing session and sends its messages to the caller.
    /// Also joins the caller to the session's SignalR group for deferred responses.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public virtual async Task LoadSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(sessionId)));

            return;
        }

        await ExecuteInScopeAsync(async services =>
        {
            var sessionManager = services.GetRequiredService<IAIChatSessionManager>();
            var profileManager = services.GetRequiredService<IAIProfileManager>();
            var promptStore = services.GetRequiredService<IAIChatSessionPromptStore>();
            var chatSession = await sessionManager.FindAsync(sessionId);
            if (chatSession == null)
            {
                await Clients.Caller.ReceiveError(GetSessionNotFoundMessage());

                return;
            }

            var profile = await profileManager.FindByIdAsync(chatSession.ProfileId);
            if (profile is null)
            {
                await Clients.Caller.ReceiveError(GetProfileNotFoundMessage());

                return;
            }

            if (!await AuthorizeProfileAsync(services, profile))
            {
                await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());

                return;
            }

            var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, GetSessionGroupName(chatSession.SessionId));
            await Clients.Caller.LoadSession(CreateSessionPayload(chatSession, profile, prompts));
        });
    }

    /// <summary>
    /// Creates a new chat session for the given profile and returns it to the caller.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <param name="initialResponseHandlerName">The initial response handler name.</param>
    public virtual async Task StartSession(string profileId, string initialResponseHandlerName = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(profileId)));

            return;
        }

        await ExecuteInScopeAsync(async services =>
        {
            var sessionManager = services.GetRequiredService<IAIChatSessionManager>();
            var profileManager = services.GetRequiredService<IAIProfileManager>();
            var promptStore = services.GetRequiredService<IAIChatSessionPromptStore>();
            var profile = await profileManager.FindByIdAsync(profileId);
            if (profile is null)
            {
                await Clients.Caller.ReceiveError(GetProfileNotFoundMessage());

                return;
            }

            if (!await AuthorizeProfileAsync(services, profile))
            {
                await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());

                return;
            }

            if (profile.Type != AIProfileType.Chat)
            {
                await Clients.Caller.ReceiveError(GetOnlyChatProfilesMessage());

                return;
            }

            var chatSession = await sessionManager.NewAsync(profile, new NewAIChatSessionContext());
            if (!string.IsNullOrWhiteSpace(initialResponseHandlerName))
            {
                chatSession.ResponseHandlerName = initialResponseHandlerName.Trim();
            }

            await SaveChatSessionAsync(sessionManager, chatSession);
            var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, GetSessionGroupName(chatSession.SessionId));
            await Clients.Caller.LoadSession(CreateSessionPayload(chatSession, profile, prompts));
        });
    }

    /// <summary>
    /// Rates a message as positive or negative. Toggling the same rating clears it.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="messageId">The message id.</param>
    /// <param name="isPositive">The is positive.</param>
    public virtual async Task RateMessage(string sessionId, string messageId, bool isPositive)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        await ExecuteInScopeAsync(async services =>
        {
            var sessionManager = services.GetRequiredService<IAIChatSessionManager>();
            var profileManager = services.GetRequiredService<IAIProfileManager>();
            var promptStore = services.GetRequiredService<IAIChatSessionPromptStore>();
            var chatSession = await sessionManager.FindAsync(sessionId);
            if (chatSession is null)
            {
                return;
            }

            var profile = await profileManager.FindByIdAsync(chatSession.ProfileId);
            if (profile is null)
            {
                return;
            }

            if (!await AuthorizeProfileAsync(services, profile))
            {
                return;
            }

            var prompt = (await promptStore.GetPromptsAsync(chatSession.SessionId)).FirstOrDefault(p => p.ItemId == messageId);
            if (prompt is null)
            {
                return;
            }

            prompt.UserRating = prompt.UserRating == isPositive ? null : isPositive;
            await promptStore.UpdateAsync(prompt);
            await OnMessageRatedAsync(services, chatSession, promptStore);
            await Clients.Caller.MessageRated(messageId, prompt.UserRating);
        });
    }

    /// <summary>
    /// Called after a message has been rated. Override to record analytics.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="chatSession">The chat session.</param>
    /// <param name="promptStore">The prompt store.</param>
    protected virtual Task OnMessageRatedAsync(IServiceProvider services, AIChatSession chatSession, IAIChatSessionPromptStore promptStore)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a user-initiated action on a chat notification system message.
    /// Dispatches to registered <see cref="IChatNotificationActionHandler"/> implementations.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="notificationType">The notification type.</param>
    /// <param name="actionName">The action name.</param>
    public virtual async Task HandleNotificationAction(string sessionId, string notificationType, string actionName)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(actionName))
        {
            return;
        }

        await ExecuteInScopeAsync(async services =>
        {
            try
            {
                var handler = services.GetKeyedService<IChatNotificationActionHandler>(actionName);
                if (handler is null)
                {
                    Logger.LogWarning("No notification action handler found for action '{ActionName}'.", actionName);

                    return;
                }

                var context = new ChatNotificationActionContext
                {
                    SessionId = sessionId,
                    NotificationType = notificationType,
                    ActionName = actionName,
                    ChatType = GetChatContextType(),
                    ConnectionId = Context.ConnectionId,
                    Services = services,
                };
                await handler.HandleAsync(context);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while handling notification action '{ActionName}'.", actionName);
                try
                {
                    await Clients.Caller.ReceiveError(GetNotificationActionErrorMessage());
                }
                catch
                {
                    // Best-effort error reporting.
                }
            }
        });
    }

    /// <summary>
    /// Stops the current conversation by cancelling the conversation CTS.
    /// </summary>
    public virtual Task StopConversation()
    {
        if (Context.Items.TryGetValue(_conversationCtsKey, out var value) && value is CancellationTokenSource cts)
        {
            cts.Cancel();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts a real-time conversation with speech-to-text transcription and
    /// text-to-speech synthesis. The caller streams audio chunks and receives
    /// AI responses as both text tokens and synthesized audio.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="audioChunks">The audio chunks.</param>
    /// <param name="audioFormat">The audio format.</param>
    /// <param name="language">The language.</param>
    public virtual async Task StartConversation(string profileId, string sessionId, IAsyncEnumerable<string> audioChunks, string audioFormat = null, string language = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(profileId)));

            return;
        }

        var cancellationToken = Context.ConnectionAborted;
        try
        {
            await ExecuteInScopeAsync(async services =>
            {
                var profileManager = services.GetRequiredService<IAIProfileManager>();
                var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();
                var clientFactory = services.GetRequiredService<IAIClientFactory>();
                var profile = await profileManager.FindByIdAsync(profileId);
                if (profile is null)
                {
                    await Clients.Caller.ReceiveError(GetProfileNotFoundMessage());

                    return;
                }

                if (!await AuthorizeProfileAsync(services, profile))
                {
                    await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());

                    return;
                }

                if (!profile.TryGetSettings<ChatModeProfileSettings>(out var chatModeSettings) || chatModeSettings.ChatMode != ChatMode.Conversation)
                {
                    await Clients.Caller.ReceiveError(GetConversationNotEnabledMessage());

                    return;
                }

                var deploymentSettings = await GetDeploymentSettingsAsync(services);
                if (string.IsNullOrEmpty(deploymentSettings.DefaultSpeechToTextDeploymentName))
                {
                    await Clients.Caller.ReceiveError(GetNoSttDeploymentMessage());

                    return;
                }

                if (string.IsNullOrEmpty(deploymentSettings.DefaultTextToSpeechDeploymentName))
                {
                    await Clients.Caller.ReceiveError(GetNoTtsDeploymentMessage());

                    return;
                }

                var sttDeployment = await deploymentManager.FindByNameAsync(deploymentSettings.DefaultSpeechToTextDeploymentName);
                if (sttDeployment is null)
                {
                    await Clients.Caller.ReceiveError(GetSttDeploymentNotFoundMessage());

                    return;
                }

                var ttsDeployment = await deploymentManager.FindByNameAsync(deploymentSettings.DefaultTextToSpeechDeploymentName);
                if (ttsDeployment is null)
                {
                    await Clients.Caller.ReceiveError(GetTtsDeploymentNotFoundMessage());

                    return;
                }

                using var speechToTextClient = await clientFactory.CreateSpeechToTextClientAsync(sttDeployment);
                using var textToSpeechClient = await clientFactory.CreateTextToSpeechClientAsync(ttsDeployment);
                var effectiveVoiceName = !string.IsNullOrWhiteSpace(chatModeSettings.VoiceName) ? chatModeSettings.VoiceName : deploymentSettings.DefaultTextToSpeechVoiceId;
                var speechLanguage = !string.IsNullOrWhiteSpace(language) ? language : "en-US";
                using var conversationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Context.Items[_conversationCtsKey] = conversationCts;
                try
                {
                    await RunConversationLoopAsync(profile, sessionId, audioChunks, audioFormat, speechLanguage, speechToTextClient, textToSpeechClient, effectiveVoiceName, services, conversationCts.Token);
                }
                finally
                {
                    Context.Items.Remove(_conversationCtsKey);
                }
            });
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                Logger.LogDebug("Conversation was cancelled.");

                return;
            }

            Logger.LogError(ex, "An error occurred during conversation mode.");
            try
            {
                await Clients.Caller.ReceiveError(GetConversationErrorMessage());
            }
            catch (Exception writeEx)
            {
                Logger.LogWarning(writeEx, "Failed to write conversation error message.");
            }
        }
    }

    /// <summary>
    /// Streams audio chunks for speech-to-text transcription. Returns partial
    /// and final transcripts to the caller as they are produced.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="audioChunks">The audio chunks.</param>
    /// <param name="audioFormat">The audio format.</param>
    /// <param name="language">The language.</param>
    public virtual async Task SendAudioStream(string profileId, string sessionId, IAsyncEnumerable<string> audioChunks, string audioFormat = null, string language = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(profileId)));

            return;
        }

        var cancellationToken = Context.ConnectionAborted;
        try
        {
            await ExecuteInScopeAsync(async services =>
            {
                var profileManager = services.GetRequiredService<IAIProfileManager>();
                var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();
                var clientFactory = services.GetRequiredService<IAIClientFactory>();
                var profile = await profileManager.FindByIdAsync(profileId);
                if (profile is null)
                {
                    await Clients.Caller.ReceiveError(GetProfileNotFoundMessage());

                    return;
                }

                if (!await AuthorizeProfileAsync(services, profile))
                {
                    await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());

                    return;
                }

                var deploymentSettings = await GetDeploymentSettingsAsync(services);
                if (string.IsNullOrEmpty(deploymentSettings.DefaultSpeechToTextDeploymentName))
                {
                    await Clients.Caller.ReceiveError(GetNoSttDeploymentMessage());

                    return;
                }

                var deployment = await deploymentManager.FindByNameAsync(deploymentSettings.DefaultSpeechToTextDeploymentName);
                if (deployment is null)
                {
                    await Clients.Caller.ReceiveError(GetSttDeploymentNotFoundMessage());

                    return;
                }

#pragma warning disable MEAI001
                using var speechToTextClient = await clientFactory.CreateSpeechToTextClientAsync(deployment);
#pragma warning restore MEAI001
                var speechLanguage = !string.IsNullOrWhiteSpace(language) ? language : "en-US";
                await StreamTranscriptionAsync(speechToTextClient, sessionId ?? string.Empty, audioChunks, audioFormat, speechLanguage, cancellationToken);
            });
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                Logger.LogDebug("Audio transcription was cancelled.");

                return;
            }

            Logger.LogError(ex, "An error occurred while transcribing audio.");
            try
            {
                await Clients.Caller.ReceiveError(GetTranscriptionErrorMessage(ex));
            }
            catch (Exception writeEx)
            {
                Logger.LogWarning(writeEx, "Failed to write transcription error message.");
            }
        }
    }

    /// <summary>
    /// Synthesizes the given text as speech and streams audio chunks to the caller.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="text">The text.</param>
    /// <param name="voiceName">The voice name.</param>
    public virtual async Task SynthesizeSpeech(string profileId, string sessionId, string text, string voiceName = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(profileId)));

            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(text)));

            return;
        }

        var cancellationToken = Context.ConnectionAborted;
        try
        {
            await ExecuteInScopeAsync(async services =>
            {
                var profileManager = services.GetRequiredService<IAIProfileManager>();
                var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();
                var clientFactory = services.GetRequiredService<IAIClientFactory>();
                var profile = await profileManager.FindByIdAsync(profileId);
                if (profile is null)
                {
                    await Clients.Caller.ReceiveError(GetProfileNotFoundMessage());

                    return;
                }

                if (!await AuthorizeProfileAsync(services, profile))
                {
                    await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());

                    return;
                }

                profile.TryGetSettings<ChatModeProfileSettings>(out var chatModeSettings);

                var deploymentSettings = await GetDeploymentSettingsAsync(services);
                if (string.IsNullOrEmpty(deploymentSettings.DefaultTextToSpeechDeploymentName))
                {
                    await Clients.Caller.ReceiveError(GetNoTtsDeploymentMessage());

                    return;
                }

                var deployment = await deploymentManager.FindByNameAsync(deploymentSettings.DefaultTextToSpeechDeploymentName);
                if (deployment is null)
                {
                    await Clients.Caller.ReceiveError(GetTtsDeploymentNotFoundMessage());

                    return;
                }

                using var textToSpeechClient = await clientFactory.CreateTextToSpeechClientAsync(deployment);
                var effectiveVoiceName = !string.IsNullOrWhiteSpace(voiceName) ? voiceName : !string.IsNullOrWhiteSpace(chatModeSettings?.VoiceName) ? chatModeSettings.VoiceName : deploymentSettings.DefaultTextToSpeechVoiceId;
                await StreamSpeechAsync(textToSpeechClient, sessionId ?? string.Empty, text, effectiveVoiceName, cancellationToken);
            });
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                Logger.LogDebug("Speech synthesis was cancelled.");

                return;
            }

            Logger.LogError(ex, "An error occurred while synthesizing speech.");
            try
            {
                await Clients.Caller.ReceiveError(GetSpeechSynthesisErrorMessage(ex));
            }
            catch (Exception writeEx)
            {
                Logger.LogWarning(writeEx, "Failed to write speech synthesis error message.");
            }
        }
    }

    /// <summary>
    /// Top-level handler for <see cref="SendMessage"/>. Validates input, resolves
    /// the profile, and dispatches to the appropriate processor.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="services">The service collection.</param>
    /// <param name="profileId">The profile id.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="sessionProfileId">The session profile id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected virtual async Task HandleSendMessageAsync(ChannelWriter<CompletionPartialMessage> writer, IServiceProvider services, string profileId, string prompt, string sessionId, string sessionProfileId, CancellationToken cancellationToken)
    {
        try
        {
            using var invocationScope = AIInvocationScope.Begin();
            if (string.IsNullOrWhiteSpace(profileId))
            {
                await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(profileId)));

                return;
            }

            var profileManager = services.GetRequiredService<IAIProfileManager>();
            var profile = await profileManager.FindByIdAsync(profileId, cancellationToken);
            if (profile is null)
            {
                await Clients.Caller.ReceiveError(GetProfileNotFoundMessage());

                return;
            }

            if (!await AuthorizeProfileAsync(services, profile))
            {
                await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());

                return;
            }

            if (profile.Type == AIProfileType.Utility)
            {
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(prompt)));

                    return;
                }

                await ProcessUtilityAsync(writer, services, profile, prompt.Trim(), cancellationToken);

                return;
            }

            await ProcessChatPromptAsync(writer, services, profile, sessionId, prompt?.Trim(), cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || (ex is TaskCanceledException && cancellationToken.IsCancellationRequested))
            {
                Logger.LogDebug("Chat prompt processing was cancelled.");

                return;
            }

            Logger.LogError(ex, "An error occurred while processing the chat prompt.");
            try
            {
                var errorMessage = new CompletionPartialMessage
                {
                    SessionId = sessionId,
                    MessageId = GenerateId(),
                    Content = GetFriendlyErrorMessage(ex),
                };
                await writer.WriteAsync(errorMessage, CancellationToken.None);
            }
            catch (Exception writeEx)
            {
                Logger.LogWarning(writeEx, "Failed to write error message to the channel.");
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Processes a chat prompt: resolves or creates a session, dispatches to the
    /// handler, streams the response, and persists results.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="services">The service collection.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected virtual async Task ProcessChatPromptAsync(ChannelWriter<CompletionPartialMessage> writer, IServiceProvider services, AIProfile profile, string sessionId, string prompt, CancellationToken cancellationToken)
    {
        var sessionManager = services.GetRequiredService<IAIChatSessionManager>();
        var promptStore = services.GetRequiredService<IAIChatSessionPromptStore>();
        var handlerResolver = services.GetRequiredService<IChatResponseHandlerResolver>();
        var sessionHandlers = services.GetRequiredService<IEnumerable<IAIChatSessionHandler>>();
        var (chatSession, isNew) = await GetOrCreateSessionAsync(services, sessionId, profile, prompt);
        await Groups.AddToGroupAsync(Context.ConnectionId, GetSessionGroupName(chatSession.SessionId), cancellationToken);
        var utcNow = GetUtcNow();
        if (IsEndedStatus(chatSession.Status))
        {
            chatSession.Status = ChatSessionStatus.Active;
            chatSession.ClosedAtUtc = null;
        }

        chatSession.LastActivityUtc = utcNow;
        // Generate a title when the session was created without one (e.g., via document upload).
        if (!isNew && !string.IsNullOrWhiteSpace(prompt) && (string.IsNullOrWhiteSpace(chatSession.Title) || chatSession.Title == DefaultBlankSessionTitle))
        {
            chatSession.Title = await GenerateSessionTitleAsync(services, profile, prompt);
        }

        var userPromptRecord = new AIChatSessionPrompt
        {
            ItemId = GenerateId(),
            SessionId = chatSession.SessionId,
            Role = ChatRole.User,
            Content = prompt,
            CreatedUtc = utcNow,
        };
        await promptStore.CreateAsync(userPromptRecord, cancellationToken);
        var existingPrompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
        var conversationHistorySource = existingPrompts.ToList();

        if (!conversationHistorySource.Any(x => x.ItemId == userPromptRecord.ItemId))
        {
            conversationHistorySource.Add(userPromptRecord);
        }

        var conversationHistory = conversationHistorySource
            .OrderBy(x => x.CreatedUtc)
            .Where(x => !x.IsGeneratedPrompt)
            .Select(p => new ChatMessage(p.Role, p.Content))
            .ToList();
        // Resolve the chat response handler for this session.
        var chatMode = profile.TryGetSettings<ChatModeProfileSettings>(out var chatModeSettings) ? chatModeSettings.ChatMode : ChatMode.TextInput;
        var handler = handlerResolver.Resolve(chatSession.ResponseHandlerName, chatMode);
        var handlerContext = new ChatResponseHandlerContext
        {
            Prompt = prompt,
            ConnectionId = Context.ConnectionId,
            SessionId = chatSession.SessionId,
            ChatType = GetChatContextType(),
            ConversationHistory = conversationHistory,
            Services = services,
            Profile = profile,
            ChatSession = chatSession,
        };
        var handlerResult = await handler.HandleAsync(handlerContext, cancellationToken);
        if (handlerResult.IsDeferred)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GetSessionGroupName(chatSession.SessionId), cancellationToken);
            await SaveChatSessionAsync(sessionManager, chatSession);

            return;
        }

        // Streaming response with reference collection.
        var assistantMessage = new AIChatSessionPrompt
        {
            ItemId = GenerateId(),
            SessionId = chatSession.SessionId,
            Role = ChatRole.Assistant,
            Title = profile.PromptSubject,
            CreatedUtc = utcNow,
        };
        if (handlerContext.AssistantAppearance is not null)
        {
            assistantMessage.Put(handlerContext.AssistantAppearance);
        }

        using var builder = ZString.CreateStringBuilder();
        var contentItemIds = new HashSet<string>();
        var references = new Dictionary<string, AICompletionReference>();
        var stopwatch = Stopwatch.StartNew();
        // Collect preemptive RAG references if available.
        CollectStreamingReferences(services, handlerContext, references, contentItemIds);
        await foreach (var chunk in handlerResult.ResponseStream.WithCancellation(cancellationToken))
        {
            if (string.IsNullOrEmpty(chunk.Text))
            {
                continue;
            }

            builder.Append(chunk.Text);
            CollectStreamingReferences(services, handlerContext, references, contentItemIds);
            var partialMessage = new CompletionPartialMessage
            {
                SessionId = chatSession.SessionId,
                MessageId = assistantMessage.ItemId,
                ResponseId = chunk.ResponseId,
                Content = chunk.Text,
                References = references,
                Appearance = handlerContext.AssistantAppearance,
            };
            await writer.WriteAsync(partialMessage, cancellationToken);
        }

        // Final pass for any references added by the last tool call.
        CollectStreamingReferences(services, handlerContext, references, contentItemIds);
        stopwatch.Stop();
        if (builder.Length > 0)
        {
            assistantMessage.Content = builder.ToString();
            assistantMessage.ContentItemIds = contentItemIds.ToList();
            assistantMessage.References = references;
            await promptStore.CreateAsync(assistantMessage, cancellationToken);
        }

        var prompts = await promptStore.GetPromptsAsync(chatSession.SessionId);
        var context = new ChatMessageCompletedContext
        {
            Profile = profile,
            ChatSession = chatSession,
            Prompts = prompts,
            ResponseLatencyMs = stopwatch.Elapsed.TotalMilliseconds,
        };
        await sessionHandlers.InvokeAsync((h, ctx) => h.MessageCompletedAsync(ctx), context, Logger);
        await OnMessageCompletedAsync(services, context);
        await SaveChatSessionAsync(sessionManager, chatSession);
    }

    /// <summary>
    /// Processes a generated prompt for a profile that uses a prompt template.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="services">The service collection.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="parentProfile">The parent profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected virtual async Task ProcessGeneratedPromptAsync(ChannelWriter<CompletionPartialMessage> writer, IServiceProvider services, AIProfile profile, string sessionId, AIProfile parentProfile, CancellationToken cancellationToken)
    {
        var sessionManager = services.GetRequiredService<IAIChatSessionManager>();
        var promptStore = services.GetRequiredService<IAIChatSessionPromptStore>();
        var aiTemplateEngine = services.GetRequiredService<ITemplateEngine>();
        var completionContextBuilder = services.GetRequiredService<IAICompletionContextBuilder>();
        var completionService = services.GetRequiredService<IAICompletionService>();
        (var chatSession, _) = await GetOrCreateSessionAsync(services, sessionId, parentProfile, userPrompt: profile.Name);
        var generatedPrompt = await aiTemplateEngine.RenderAsync(profile.PromptTemplate, new Dictionary<string, object>() { ["Profile"] = profile, ["Session"] = chatSession, }, cancellationToken);
        var assistantMessage = new AIChatSessionPrompt
        {
            ItemId = GenerateId(),
            SessionId = chatSession.SessionId,
            Role = ChatRole.Assistant,
            IsGeneratedPrompt = true,
            Title = profile.PromptSubject,
            CreatedUtc = GetUtcNow(),
        };

        var completionContext = await completionContextBuilder.BuildAsync(profile, cancellationToken: cancellationToken);
        var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();
        var chatDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Chat, deploymentName: completionContext.ChatDeploymentName, cancellationToken: cancellationToken)
            ?? throw new AIDeploymentNotFoundException("Unable to resolve a chat deployment for the profile.");

        using var builder = ZString.CreateStringBuilder();
        var contentItemIds = new HashSet<string>();
        var references = new Dictionary<string, AICompletionReference>();

        await foreach (var chunk in completionService.CompleteStreamingAsync(chatDeployment, [new ChatMessage(ChatRole.User, generatedPrompt)], completionContext, cancellationToken))
        {
            if (string.IsNullOrEmpty(chunk.Text))
            {
                continue;
            }

            builder.Append(chunk.Text);
            var partialMessage = new CompletionPartialMessage
            {
                SessionId = sessionId,
                MessageId = assistantMessage.ItemId,
                Content = chunk.Text,
                References = references,
            };
            await writer.WriteAsync(partialMessage, cancellationToken);
        }

        assistantMessage.Content = builder.ToString();
        assistantMessage.ContentItemIds = contentItemIds.ToList();
        assistantMessage.References = references;
        await promptStore.CreateAsync(assistantMessage, cancellationToken);
        await SaveChatSessionAsync(sessionManager, chatSession);
    }

    /// <summary>
    /// Processes a utility (one-shot) profile - no session or history needed.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="services">The service collection.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected virtual async Task ProcessUtilityAsync(ChannelWriter<CompletionPartialMessage> writer, IServiceProvider services, AIProfile profile, string prompt, CancellationToken cancellationToken)
    {
        var completionContextBuilder = services.GetRequiredService<IAICompletionContextBuilder>();
        var completionService = services.GetRequiredService<IAICompletionService>();
        var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();
        var messageId = GenerateId();
        var completionContext = await completionContextBuilder.BuildAsync(profile, cancellationToken: cancellationToken);
        var chatDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Chat, deploymentName: completionContext.ChatDeploymentName, cancellationToken: cancellationToken)
            ?? throw new AIDeploymentNotFoundException("Unable to resolve a chat deployment for the profile.");
        var references = new Dictionary<string, AICompletionReference>();
        await foreach (var chunk in completionService.CompleteStreamingAsync(chatDeployment, [new ChatMessage(ChatRole.User, prompt)], completionContext, cancellationToken))
        {
            if (string.IsNullOrEmpty(chunk.Text))
            {
                continue;
            }

            var partialMessage = new CompletionPartialMessage
            {
                MessageId = messageId,
                Content = chunk.Text,
                References = references,
            };
            await writer.WriteAsync(partialMessage, cancellationToken);
        }
    }

    /// <summary>
    /// Finds an existing session by ID or creates a new one for the given profile.
    /// </summary>
    protected virtual async Task<(AIChatSession ChatSession, bool IsNewSession)> GetOrCreateSessionAsync(IServiceProvider services, string sessionId, AIProfile profile, string userPrompt)
    {
        var sessionManager = services.GetRequiredService<IAIChatSessionManager>();
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var existingChatSession = await sessionManager.FindAsync(sessionId);
            if (existingChatSession != null && existingChatSession.ProfileId == profile.ItemId)
            {
                return (existingChatSession, false);
            }
        }

        var chatSession = await sessionManager.NewAsync(profile, new NewAIChatSessionContext());
        chatSession.Title = await GenerateSessionTitleAsync(services, profile, userPrompt);
        if (string.IsNullOrEmpty(chatSession.Title))
        {
            chatSession.Title = Truncate(userPrompt, 255);
        }

        return (chatSession, true);
    }

    private static bool IsEndedStatus(ChatSessionStatus status)
    {
        return status is ChatSessionStatus.Closed or ChatSessionStatus.Abandoned;
    }

    /// <summary>
    /// Creates the payload object sent to clients when a session is loaded.
    /// </summary>
    /// <param name="chatSession">The chat session.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="prompts">The prompts.</param>
    protected virtual object CreateSessionPayload(AIChatSession chatSession, AIProfile profile, IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        return new
        {
            chatSession.SessionId,
            Profile = new
            {
                Id = chatSession.ProfileId,
                Type = profile.Type.ToString(),
            },
            chatSession.Documents,
            Messages = prompts.Select(message => new AIChatResponseMessageDetailed
            {
                Id = message.ItemId,
                Role = message.Role.Value,
                IsGeneratedPrompt = message.IsGeneratedPrompt,
                Title = message.Title,
                Content = message.Content,
                UserRating = message.UserRating,
                References = message.References,
                Appearance = message.TryGet<AssistantMessageAppearance>(out var appearance) ? appearance : null,
            })
        };
    }

    /// <summary>
    /// Saves chat session.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="chatSession">The chat session.</param>
    protected virtual async Task SaveChatSessionAsync(IAIChatSessionManager sessionManager, AIChatSession chatSession)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(chatSession);

        await sessionManager.SaveAsync(chatSession);

        var committer = _services.GetService<IStoreCommitter>();

        if (committer != null)
        {
            await committer.CommitAsync();
        }
    }

#pragma warning disable MEAI001
    /// <summary>
    /// Synthesizes the given text as speech and streams audio chunks to the caller.
    /// </summary>
    /// <param name="textToSpeechClient">The text to speech client.</param>
    /// <param name="identifier">The identifier.</param>
    /// <param name="text">The text.</param>
    /// <param name="voiceName">The voice name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected async Task StreamSpeechAsync(ITextToSpeechClient textToSpeechClient, string identifier, string text, string voiceName, CancellationToken cancellationToken)
    {
        var options = new TextToSpeechOptions();
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            options.VoiceId = voiceName;
        }

        var speechText = SpeechTextSanitizer.Sanitize(text);
        if (string.IsNullOrWhiteSpace(speechText))
        {
            await Clients.Caller.ReceiveAudioComplete(identifier);

            return;
        }

        await foreach (var update in textToSpeechClient.GetStreamingAudioAsync(speechText, options, cancellationToken))
        {
            var audioContent = update.Contents.OfType<DataContent>().FirstOrDefault();

            if (audioContent?.Data is not { Length: > 0 } audioData)
            {
                continue;
            }

            var base64Audio = Convert.ToBase64String(audioData.ToArray());

            await Clients.Caller.ReceiveAudioChunk(identifier, base64Audio, audioContent.MediaType ?? "audio/mp3");
        }

        await Clients.Caller.ReceiveAudioComplete(identifier);
    }

    /// <summary>
    /// Reads sentences from a channel and synthesizes each as speech.
    /// </summary>
    /// <param name="textToSpeechClient">The text to speech client.</param>
    /// <param name="getIdentifier">The get identifier.</param>
    /// <param name="sentenceReader">The sentence reader.</param>
    /// <param name="voiceName">The voice name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected async Task StreamSentencesAsSpeechAsync(ITextToSpeechClient textToSpeechClient, Func<string> getIdentifier, ChannelReader<string> sentenceReader, string voiceName, CancellationToken cancellationToken)
    {
        var options = new TextToSpeechOptions();

        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            options.VoiceId = voiceName;
        }

        await foreach (var sentence in sentenceReader.ReadAllAsync(cancellationToken))
        {
            var identifier = getIdentifier();
            var speechText = SpeechTextSanitizer.Sanitize(sentence);

            if (string.IsNullOrWhiteSpace(speechText))
            {
                continue;
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("StreamSentencesAsSpeechAsync: Synthesizing sentence ({Length} chars).", speechText.Length);
            }

            await foreach (var update in textToSpeechClient.GetStreamingAudioAsync(speechText, options, cancellationToken))
            {
                var audioContent = update.Contents.OfType<DataContent>().FirstOrDefault();
                if (audioContent?.Data is not { Length: > 0 } audioData)
                {
                    continue;
                }

                var base64Audio = Convert.ToBase64String(audioData.ToArray());
                await Clients.Caller.ReceiveAudioChunk(identifier, base64Audio, audioContent.MediaType ?? "audio/mp3");
            }

            await Clients.Caller.ReceiveAudioComplete(identifier);
        }
    }
#pragma warning restore MEAI001

#pragma warning disable MEAI001
    /// <summary>
    /// Runs the full conversation loop: transcribes speech input, sends it through
    /// the AI pipeline, and streams the synthesized speech response.
    /// </summary>
    private async Task RunConversationLoopAsync(AIProfile profile, string sessionId, IAsyncEnumerable<string> audioChunks, string audioFormat, string speechLanguage, ISpeechToTextClient speechToTextClient, ITextToSpeechClient textToSpeechClient, string voiceName, IServiceProvider services, CancellationToken cancellationToken)
    {
        var pipe = new Pipe();
        using var errorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var transcriptionTask = TranscribeConversationAsync(pipe.Reader, profile, sessionId, audioFormat, speechLanguage, speechToTextClient, textToSpeechClient, voiceName, services, errorCts, cancellationToken);
        try
        {
            await foreach (var base64Chunk in audioChunks.WithCancellation(errorCts.Token))
            {
                try
                {
                    var bytes = Convert.FromBase64String(base64Chunk);
                    await pipe.Writer.WriteAsync(bytes, errorCts.Token);
                }
                catch (FormatException)
                {
                    continue;
                }
            }
        }
        catch (OperationCanceledException) when (errorCts.IsCancellationRequested)
        {
            // Transcription error or connection aborted.
        }

        await pipe.Writer.CompleteAsync();
        await transcriptionTask;
    }

    private async Task TranscribeConversationAsync(PipeReader pipeReader, AIProfile profile, string sessionId, string audioFormat, string speechLanguage, ISpeechToTextClient speechToTextClient, ITextToSpeechClient textToSpeechClient, string voiceName, IServiceProvider services, CancellationTokenSource errorCts, CancellationToken cancellationToken)
    {
        CancellationTokenSource currentResponseCts = null;
        Task<string> currentResponseTask = null;
        try
        {
            await using var readerStream = pipeReader.AsStream();
            using var committedText = ZString.CreateStringBuilder();
            var sttOptions = new SpeechToTextOptions
            {
                SpeechLanguage = speechLanguage,
            };
            if (!string.IsNullOrWhiteSpace(audioFormat))
            {
                sttOptions.AdditionalProperties ??= [];
                sttOptions.AdditionalProperties["audioFormat"] = audioFormat;
            }

            var effectiveSessionId = sessionId ?? string.Empty;
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("TranscribeConversationAsync: Starting STT stream. Language={Language}, Format={Format}.", speechLanguage, audioFormat);
            }

            await foreach (var update in speechToTextClient.GetStreamingTextAsync(readerStream, sttOptions, cancellationToken))
            {
                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                var isPartial = update.AdditionalProperties?.TryGetValue("isPartial", out var p) == true && p is true;
                if (isPartial)
                {
                    var display = committedText.Length > 0 ? committedText.ToString() + update.Text : update.Text;
                    await Clients.Caller.ReceiveTranscript(effectiveSessionId, display, false);
                }
                else
                {
                    if (currentResponseCts != null)
                    {
                        Logger.LogDebug("TranscribeConversationAsync: New utterance received, cancelling previous AI response.");
                        await currentResponseCts.CancelAsync();
                        if (currentResponseTask != null)
                        {
                            try
                            {
                                effectiveSessionId = await currentResponseTask;
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                Logger.LogDebug("AI response was interrupted by new user speech.");
                            }
                        }

                        currentResponseCts.Dispose();
                        currentResponseCts = null;
                        currentResponseTask = null;
                    }

                    if (committedText.Length > 0)
                    {
                        committedText.Append(' ');
                    }

                    committedText.Append(update.Text);
                    var fullText = committedText.ToString().TrimEnd();
                    await Clients.Caller.ReceiveTranscript(effectiveSessionId, fullText, true);
                    await Clients.Caller.ReceiveConversationUserMessage(effectiveSessionId, fullText);
                    committedText.Clear();
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.LogDebug("TranscribeConversationAsync: Final utterance received: '{Text}'. Dispatching AI response.", fullText);
                    }

                    currentResponseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    currentResponseTask = ProcessConversationPromptAsync(profile, effectiveSessionId, fullText, textToSpeechClient, voiceName, services, currentResponseCts.Token);
                }
            }

            Logger.LogDebug("TranscribeConversationAsync: STT stream ended.");
            if (currentResponseTask != null)
            {
                try
                {
                    effectiveSessionId = await currentResponseTask;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Interrupted.
                }

                currentResponseCts?.Dispose();
                currentResponseCts = null;
                currentResponseTask = null;
            }

            var remainingText = committedText.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(remainingText))
            {
                await Clients.Caller.ReceiveConversationUserMessage(effectiveSessionId, remainingText);
                try
                {
                    await ProcessConversationPromptAsync(profile, effectiveSessionId, remainingText, textToSpeechClient, voiceName, services, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Interrupted.
                }
            }
        }
        catch (Exception)
        {
            await errorCts.CancelAsync();
            throw;
        }
    }

    private async Task<string> ProcessConversationPromptAsync(AIProfile profile, string sessionId, string prompt, ITextToSpeechClient textToSpeechClient, string voiceName, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("ProcessConversationPromptAsync: Starting for prompt length={PromptLength}.", prompt.Length);
        }

        var channel = Channel.CreateUnbounded<CompletionPartialMessage>();
        var handleTask = HandleSendMessageAsync(channel.Writer, services, profile.ItemId, prompt, sessionId, null, cancellationToken);
        var sentenceChannel = Channel.CreateUnbounded<string>();
        var effectiveSessionId = sessionId;
        string messageId = null;
        string responseId = null;
        var ttsTask = StreamSentencesAsSpeechAsync(textToSpeechClient, () => effectiveSessionId, sentenceChannel.Reader, voiceName, cancellationToken);
        using var sentenceBuffer = ZString.CreateStringBuilder();
        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.SessionId) && string.IsNullOrEmpty(effectiveSessionId))
                {
                    effectiveSessionId = chunk.SessionId;
                }

                messageId ??= chunk.MessageId;
                responseId ??= chunk.ResponseId;
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    await Clients.Caller.ReceiveConversationAssistantToken(
                        effectiveSessionId,
                        messageId ?? string.Empty,
                        chunk.Content,
                        responseId ?? string.Empty,
                        chunk.References);
                    sentenceBuffer.Append(chunk.Content);
                    if (SentenceBoundaryDetector.EndsWithSentenceBoundary(chunk.Content))
                    {
                        var sentence = sentenceBuffer.ToString().Trim();
                        if (!string.IsNullOrEmpty(sentence))
                        {
                            if (Logger.IsEnabled(LogLevel.Debug))
                            {
                                Logger.LogDebug("ProcessConversationPromptAsync: Queuing sentence for TTS ({Length} chars).", sentence.Length);
                            }

                            await sentenceChannel.Writer.WriteAsync(sentence, cancellationToken);
                            sentenceBuffer.Clear();
                        }
                    }
                }
            }

            await handleTask;
            var remaining = sentenceBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(remaining))
            {
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogDebug("ProcessConversationPromptAsync: Queuing final partial sentence for TTS ({Length} chars).", remaining.Length);
                }

                await sentenceChannel.Writer.WriteAsync(remaining, cancellationToken);
            }

            sentenceChannel.Writer.Complete();
            await ttsTask;
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("ProcessConversationPromptAsync: Completed. SessionId={SessionId}.", effectiveSessionId);
            }
        }
        finally
        {
            sentenceChannel.Writer.TryComplete();

            if (!string.IsNullOrEmpty(messageId))
            {
                try
                {
                    await Clients.Caller.ReceiveConversationAssistantComplete(
                        effectiveSessionId,
                        messageId,
                        await GetPromptReferencesAsync(services, effectiveSessionId, messageId));
                }
                catch
                {
                    // Best-effort - the client may have disconnected.
                }
            }
        }

        return effectiveSessionId;
    }

    private static async Task<Dictionary<string, AICompletionReference>> GetPromptReferencesAsync(
        IServiceProvider services,
        string sessionId,
        string messageId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var promptStore = services.GetRequiredService<IAIChatSessionPromptStore>();
        var prompts = await promptStore.GetPromptsAsync(sessionId);
        var prompt = prompts.FirstOrDefault(entry => string.Equals(entry.ItemId, messageId, StringComparison.Ordinal));

        return prompt?.References;
    }
#pragma warning restore MEAI001

#pragma warning disable MEAI001
    /// <summary>
    /// Streams real-time speech-to-text transcription of audio input to the caller.
    /// </summary>
    private async Task StreamTranscriptionAsync(ISpeechToTextClient speechToTextClient, string sessionId, IAsyncEnumerable<string> audioChunks, string audioFormat, string speechLanguage, CancellationToken cancellationToken)
    {
        var pipe = new Pipe();
        using var errorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var transcriptionTask = TranscribeAudioInputAsync(sessionId, pipe, audioFormat, speechLanguage, speechToTextClient, errorCts, cancellationToken);
        try
        {
            await foreach (var base64Chunk in audioChunks.WithCancellation(errorCts.Token))
            {
                try
                {
                    var bytes = Convert.FromBase64String(base64Chunk);
                    await pipe.Writer.WriteAsync(bytes, errorCts.Token);
                }
                catch (FormatException)
                {
                    continue;
                }
            }
        }
        catch (OperationCanceledException) when (errorCts.IsCancellationRequested)
        {
            // Transcription failed or connection aborted.
        }

        await pipe.Writer.CompleteAsync();
        await transcriptionTask;
    }

    private async Task TranscribeAudioInputAsync(string sessionId, Pipe pipe, string audioFormat, string speechLanguage, ISpeechToTextClient speechToTextClient, CancellationTokenSource errorCts, CancellationToken cancellationToken)
    {
        try
        {
            await using var readerStream = pipe.Reader.AsStream();
            using var committedText = ZString.CreateStringBuilder();
            var sttOptions = new SpeechToTextOptions
            {
                SpeechLanguage = speechLanguage,
            };
            if (!string.IsNullOrWhiteSpace(audioFormat))
            {
                sttOptions.AdditionalProperties ??= [];
                sttOptions.AdditionalProperties["audioFormat"] = audioFormat;
            }

            await foreach (var update in speechToTextClient.GetStreamingTextAsync(readerStream, sttOptions, cancellationToken))
            {
                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                var isPartial = update.AdditionalProperties?.TryGetValue("isPartial", out var p) == true && p is true;
                if (isPartial)
                {
                    var display = committedText.Length > 0 ? committedText.ToString() + update.Text : update.Text;
                    await Clients.Caller.ReceiveTranscript(sessionId, display, false);
                }
                else
                {
                    if (committedText.Length > 0)
                    {
                        committedText.Append(' ');
                    }

                    committedText.Append(update.Text);
                    await Clients.Caller.ReceiveTranscript(sessionId, committedText.ToString(), false);
                }
            }

            var finalText = committedText.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(finalText))
            {
                await Clients.Caller.ReceiveTranscript(sessionId, finalText, true);
            }
        }
        catch (Exception)
        {
            await errorCts.CancelAsync();
            throw;
        }
    }
#pragma warning restore MEAI001
}
