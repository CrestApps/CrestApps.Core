using System.IO.Pipelines;
using System.Text.Json;
using System.Threading.Channels;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Services;
using Cysharp.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.

namespace CrestApps.Core.AI.Chat.Hubs;

/// <summary>
/// Base SignalR hub for AI chat interactions. Provides streaming message delivery,
/// interaction loading, settings persistence, history clearing, conversation mode,
/// audio transcription, and text-to-speech synthesis.
/// <para>
/// All public hub methods are <c>virtual</c> so that framework-specific subclasses
/// (e.g., OrchardCore) can wrap each call with their own scoping or authorization
/// logic and then call the base implementation.
/// </para>
/// </summary>
public class ChatInteractionHubBase : Hub<IChatInteractionHubClient>
{
    private const string _conversationCtsKey = "ConversationCts";

    private readonly IServiceProvider _services;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatInteractionHubBase"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    protected ChatInteractionHubBase(
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
    // ------------------------- Scoping hook -------------------------

    protected virtual Task ExecuteInScopeAsync(Func<IServiceProvider, Task> action)
    {
        return action(_services);
    }

    /// <summary>
    /// Gets the chat context type for this hub.
    /// </summary>
    // ------------------------- Identity hooks -------------------------

    protected virtual ChatContextType GetChatContextType()
    {
        return ChatContextType.ChatInteraction;
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
    /// Checks whether the current caller is authorized to use the given interaction.
    /// Override to perform framework-specific authorization checks.
    /// The default implementation always returns <c>true</c>.
    /// </summary>
    // ------------------------- Authorization hooks -------------------------

    protected virtual Task<bool> AuthorizeAsync(IServiceProvider services, ChatInteraction interaction)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Commits all staged writes to the underlying data store. The default implementation
    /// resolves <see cref="IStoreCommitter"/> and calls <see cref="IStoreCommitter.CommitAsync"/>.
    /// </summary>
    // ------------------------- Commit hook -------------------------

    protected virtual async Task CommitChangesAsync(IServiceProvider services)
    {
        var committer = services.GetService<IStoreCommitter>();
        if (committer is not null)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("ChatInteractionHubBase committing staged store changes for connection '{ConnectionId}'.", Context.ConnectionId);
            }

            await committer.CommitAsync(Context.ConnectionAborted);
        }
    }

    /// <summary>
    /// Resolves the deployment settings for speech services. Override in
    /// OrchardCore to read from ISiteService instead of IOptionsMonitor.
    /// </summary>
    // ------------------------- Deployment resolution -------------------------

    protected virtual Task<DefaultAIDeploymentSettings> GetDeploymentSettingsAsync(IServiceProvider services)
    {
        var options = services.GetService<IOptionsMonitor<DefaultAIDeploymentSettings>>();
        return Task.FromResult(options?.CurrentValue ?? new DefaultAIDeploymentSettings());
    }

    /// <summary>
    /// Returns the <see cref="ChatMode"/> for the current chat interaction context.
    /// Override to read from site-level or module-specific settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    protected virtual Task<ChatMode> GetChatModeAsync(IServiceProvider services)
    {
        return Task.FromResult(ChatMode.TextInput);
    }

    /// <summary>
    /// Returns whether text-to-speech playback is enabled for the current context.
    /// Override to read from site-level or module-specific settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    protected virtual Task<bool> IsTextToSpeechPlaybackEnabledAsync(IServiceProvider services)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets required field message.
    /// </summary>
    // ------------------------- Error message hooks -------------------------

    protected virtual string GetRequiredFieldMessage(string fieldName)
    {
        return $"{fieldName} is required.";
    }

    /// <summary>
    /// Gets interaction not found message.
    /// </summary>
    protected virtual string GetInteractionNotFoundMessage()
    {
        return "Interaction not found.";
    }

    /// <summary>
    /// Gets not authorized message.
    /// </summary>
    protected virtual string GetNotAuthorizedMessage()
    {
        return "You are not authorized to access chat interactions.";
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

        return "An error occurred while processing your message.";
    }

    /// <summary>
    /// Gets invalid chat model settings message.
    /// </summary>
    protected virtual string GetInvalidChatModelSettingsMessage()
    {
        return "The chat model settings are missing or invalid. Update the Chat model in this chat interaction, the linked AI Profile, or the global AI settings.";
    }

    /// <summary>
    /// Gets conversation not enabled message.
    /// </summary>
    protected virtual string GetConversationNotEnabledMessage()
    {
        return "Conversation mode is not enabled for chat interactions.";
    }

    /// <summary>
    /// Gets no stt deployment message.
    /// </summary>
    protected virtual string GetNoSttDeploymentMessage()
    {
        return "No speech-to-text deployment is configured or available.";
    }

    /// <summary>
    /// Gets no tts deployment message.
    /// </summary>
    protected virtual string GetNoTtsDeploymentMessage()
    {
        return "No text-to-speech deployment is configured or available.";
    }

    /// <summary>
    /// Gets tts not enabled message.
    /// </summary>
    protected virtual string GetTtsNotEnabledMessage()
    {
        return "Text-to-speech is not enabled for chat interactions.";
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
        return IsSpeechAuthenticationFailure(ex)
            ? "Speech-to-text authentication failed. Check the configured speech deployment credentials and region."
            : "An error occurred while transcribing the audio. Please try again.";
    }

    /// <summary>
    /// Gets speech synthesis error message.
    /// </summary>
    /// <param name="ex">The ex.</param>
    protected virtual string GetSpeechSynthesisErrorMessage(Exception ex = null)
    {
        return IsSpeechAuthenticationFailure(ex)
            ? "Text-to-speech authentication failed. Check the configured speech deployment credentials and region."
            : "An error occurred while synthesizing speech. Please try again.";
    }

    /// <summary>
    /// Gets settings validation message.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    protected virtual string GetSettingsValidationMessage(string propertyName)
    {
        return "One or more settings are invalid.";
    }

    /// <summary>
    /// Collects references (citations) during streaming. Called after each chunk
    /// and once after the stream ends. Override to integrate citation collection.
    /// </summary>
    // ------------------------- Post-completion hooks -------------------------

    protected virtual void CollectStreamingReferences(
    IServiceProvider services,
    ChatResponseHandlerContext handlerContext,
    Dictionary<string, AICompletionReference> references,
    HashSet<string> contentItemIds)
    {
    }

    /// <summary>
    /// Called after an assistant prompt is created and before it is saved.
    /// Override to add content metadata or perform analytics.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="contentItemIds">The content item ids.</param>
    protected virtual Task OnAssistantPromptCreatedAsync(
        IServiceProvider services,
        ChatInteractionPrompt prompt,
        HashSet<string> contentItemIds)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates the interaction payload sent to the client when loading an interaction.
    /// Override to include additional properties (e.g., Appearance).
    /// </summary>
    /// <param name="interaction">The interaction.</param>
    /// <param name="prompts">The prompts.</param>
    protected virtual object CreateInteractionPayload(
        ChatInteraction interaction,
        IReadOnlyCollection<ChatInteractionPrompt> prompts)
    {
        return new
        {
            interaction.ItemId,
            interaction.Title,
            interaction.ConnectionName,
            DeploymentId = interaction.ChatDeploymentName,
            Messages = prompts.Select(message => new AIChatResponseMessageDetailed
            {
                Id = message.ItemId,
                Role = message.Role.Value,
                IsGeneratedPrompt = message.IsGeneratedPrompt,
                Title = message.Title,
                Content = message.Text,
                References = message.References,
            }),
        };
    }

    /// <summary>
    /// Applies core settings from a JSON payload to a <see cref="ChatInteraction"/>.
    /// Override to apply additional module-specific settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="interaction">The interaction.</param>
    /// <param name="settings">The settings.</param>
    protected virtual Task ApplyCoreSettingsAsync(
        IServiceProvider services,
        ChatInteraction interaction,
        JsonElement settings)
    {
        interaction.Title = JsonHelper.GetString(settings, "title") ?? "Untitled";
        interaction.OrchestratorName = JsonHelper.GetString(settings, "orchestratorName");
        interaction.ConnectionName = JsonHelper.GetString(settings, "connectionName");
        interaction.ChatDeploymentName = JsonHelper.GetString(settings, "deploymentName")
            ?? JsonHelper.GetString(settings, "deploymentId");
        interaction.UtilityDeploymentName = JsonHelper.GetString(settings, "utilityDeploymentName");
        interaction.SystemMessage = JsonHelper.GetString(settings, "systemMessage");
        interaction.Temperature = JsonHelper.GetFloat(settings, "temperature");
        interaction.TopP = JsonHelper.GetFloat(settings, "topP");
        interaction.FrequencyPenalty = JsonHelper.GetFloat(settings, "frequencyPenalty");
        interaction.PresencePenalty = JsonHelper.GetFloat(settings, "presencePenalty");
        interaction.MaxTokens = JsonHelper.GetInt(settings, "maxTokens");
        interaction.PastMessagesCount = JsonHelper.GetInt(settings, "pastMessagesCount");
        interaction.ToolNames = JsonHelper.GetStringArray(settings, "toolNames");
        interaction.AgentNames = JsonHelper.GetStringArray(settings, "agentNames");
        interaction.McpConnectionIds = JsonHelper.GetStringArray(settings, "mcpConnectionIds");
        interaction.A2AConnectionIds = JsonHelper.GetStringArray(settings, "a2aConnectionIds");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates the settings payload before applying it. Return <c>null</c> if valid,
    /// or the invalid property name to trigger a validation error message.
    /// </summary>
    /// <param name="settings">The settings.</param>
    protected virtual string ValidateSettings(JsonElement settings)
    {
        return null;
    }

    /// <summary>
    /// Stops the current conversation by cancelling the conversation CTS.
    /// </summary>
    // -------------------------------------------------------------------
    //  PUBLIC HUB METHODS - all virtual for framework-specific overrides
    // -------------------------------------------------------------------

    public virtual Task StopConversation()
    {
        if (Context.Items.TryGetValue(_conversationCtsKey, out var value) && value is CancellationTokenSource cts)
        {
            cts.Cancel();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Streams a chat response for the given prompt.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual ChannelReader<CompletionPartialMessage> SendMessage(string itemId, string prompt, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<CompletionPartialMessage>();
        _ = ExecuteInScopeAsync(services => HandlePromptAsync(channel.Writer, services, itemId, prompt, cancellationToken));
        return channel.Reader;
    }

    /// <summary>
    /// Loads an existing interaction and sends its messages to the caller.
    /// Also joins the caller to the interaction's SignalR group for deferred responses.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    public virtual async Task LoadInteraction(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(itemId)));
            return;
        }

        await ExecuteInScopeAsync(async services =>
        {
            var interactionManager = services.GetRequiredService<ICatalogManager<ChatInteraction>>();
            var promptStore = services.GetRequiredService<IChatInteractionPromptStore>();

            var interaction = await interactionManager.FindByIdAsync(itemId);
            if (interaction is null)
            {
                await Clients.Caller.ReceiveError(GetInteractionNotFoundMessage());
                return;
            }

            if (!await AuthorizeAsync(services, interaction))
            {
                await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());
                return;
            }

            var prompts = await promptStore.GetPromptsAsync(itemId);
            await Groups.AddToGroupAsync(Context.ConnectionId, GetInteractionGroupName(interaction.ItemId));
            await Clients.Caller.LoadInteraction(CreateInteractionPayload(interaction, prompts));
        });
    }

    /// <summary>
    /// Saves interaction settings from a JSON payload.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="settings">The settings.</param>
    public virtual async Task SaveSettings(string itemId, JsonElement settings)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(itemId)));
            return;
        }

        await ExecuteInScopeAsync(async services =>
        {
            var interactionManager = services.GetRequiredService<ICatalogManager<ChatInteraction>>();
            var settingsHandlers = services.GetRequiredService<IEnumerable<IChatInteractionSettingsHandler>>();

            var interaction = await interactionManager.FindByIdAsync(itemId);
            if (interaction == null)
            {
                await Clients.Caller.ReceiveError(GetInteractionNotFoundMessage());
                return;
            }

            if (!await AuthorizeAsync(services, interaction))
            {
                await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());
                return;
            }

            var invalidSetting = ValidateSettings(settings);
            if (invalidSetting != null)
            {
                await Clients.Caller.ReceiveError(GetSettingsValidationMessage(invalidSetting));
                return;
            }

            foreach (var handler in settingsHandlers)
            {
                await handler.UpdatingAsync(interaction, settings);
            }

            await ApplyCoreSettingsAsync(services, interaction, settings);

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(
                    "Saving chat interaction settings for '{InteractionId}' with data source '{DataSourceId}', strictness '{Strictness}', top documents '{TopNDocuments}', and in-scope '{IsInScope}'.",
                    interaction.ItemId,
                    JsonHelper.GetString(settings, "dataSourceId"),
                    JsonHelper.GetInt(settings, "strictness"),
                    JsonHelper.GetInt(settings, "topNDocuments"),
                    JsonHelper.GetBool(settings, "isInScope"));
            }

            await interactionManager.UpdateAsync(interaction);
            await CommitChangesAsync(services);

            foreach (var handler in settingsHandlers)
            {
                await handler.UpdatedAsync(interaction, settings);
            }

            await Clients.Caller.SettingsSaved(interaction.ItemId, interaction.Title);
        });
    }

    /// <summary>
    /// Clears the chat history (prompts) while keeping documents, parameters, and tools intact.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    public virtual async Task ClearHistory(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(itemId)));
            return;
        }

        await ExecuteInScopeAsync(async services =>
        {
            var interactionManager = services.GetRequiredService<ICatalogManager<ChatInteraction>>();
            var promptStore = services.GetRequiredService<IChatInteractionPromptStore>();

            var interaction = await interactionManager.FindByIdAsync(itemId);
            if (interaction == null)
            {
                await Clients.Caller.ReceiveError(GetInteractionNotFoundMessage());
                return;
            }

            if (!await AuthorizeAsync(services, interaction))
            {
                await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());
                return;
            }

            await promptStore.DeleteAllPromptsAsync(itemId);
            await CommitChangesAsync(services);
            await Clients.Caller.HistoryCleared(interaction.ItemId);
        });
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
    /// Starts a real-time conversation with speech-to-text transcription and
    /// text-to-speech synthesis.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="audioChunks">The audio chunks.</param>
    /// <param name="audioFormat">The audio format.</param>
    /// <param name="language">The language.</param>
    public virtual async Task StartConversation(string itemId, IAsyncEnumerable<string> audioChunks, string audioFormat = null, string language = null)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(itemId)));
            return;
        }

        var cancellationToken = Context.ConnectionAborted;
        try
        {
            await ExecuteInScopeAsync(async services =>
            {
                var interactionManager = services.GetRequiredService<ICatalogManager<ChatInteraction>>();
                var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();
                var clientFactory = services.GetRequiredService<IAIClientFactory>();

                var interaction = await interactionManager.FindByIdAsync(itemId);
                if (interaction is null)
                {
                    await Clients.Caller.ReceiveError(GetInteractionNotFoundMessage());
                    return;
                }

                if (!await AuthorizeAsync(services, interaction))
                {
                    await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());
                    return;
                }

                var chatMode = await GetChatModeAsync(services);
                if (chatMode != ChatMode.Conversation)
                {
                    await Clients.Caller.ReceiveError(GetConversationNotEnabledMessage());
                    return;
                }

                var deploymentSettings = await GetDeploymentSettingsAsync(services);

                var speechToTextDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.SpeechToText);
                if (speechToTextDeployment is null)
                {
                    await Clients.Caller.ReceiveError(GetNoSttDeploymentMessage());
                    return;
                }

                var textToSpeechDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.TextToSpeech);
                if (textToSpeechDeployment is null)
                {
                    await Clients.Caller.ReceiveError(GetNoTtsDeploymentMessage());
                    return;
                }

                using var speechToTextClient = await clientFactory.CreateSpeechToTextClientAsync(speechToTextDeployment);
                using var textToSpeechClient = await clientFactory.CreateTextToSpeechClientAsync(textToSpeechDeployment);

                var effectiveVoiceName = deploymentSettings.DefaultTextToSpeechVoiceId;
                var speechLanguage = !string.IsNullOrWhiteSpace(language) ? language : "en-US";

                using var conversationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Context.Items[_conversationCtsKey] = conversationCts;

                try
                {
                    await RunConversationLoopAsync(
                        itemId, audioChunks, audioFormat, speechLanguage,
                        speechToTextClient, textToSpeechClient, effectiveVoiceName,
                        services, conversationCts.Token);
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
    /// Streams audio chunks for speech-to-text transcription.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="audioChunks">The audio chunks.</param>
    /// <param name="audioFormat">The audio format.</param>
    /// <param name="language">The language.</param>
    public virtual async Task SendAudioStream(string itemId, IAsyncEnumerable<string> audioChunks, string audioFormat = null, string language = null)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(itemId)));
            return;
        }

        var cancellationToken = Context.ConnectionAborted;
        try
        {
            await ExecuteInScopeAsync(async services =>
            {
                var interactionManager = services.GetRequiredService<ICatalogManager<ChatInteraction>>();
                var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();
                var clientFactory = services.GetRequiredService<IAIClientFactory>();

                var interaction = await interactionManager.FindByIdAsync(itemId);
                if (interaction is null)
                {
                    await Clients.Caller.ReceiveError(GetInteractionNotFoundMessage());
                    return;
                }

                if (!await AuthorizeAsync(services, interaction))
                {
                    await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());
                    return;
                }

                var speechToTextDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.SpeechToText);
                if (speechToTextDeployment is null)
                {
                    await Clients.Caller.ReceiveError(GetNoSttDeploymentMessage());
                    return;
                }

                using var speechToTextClient = await clientFactory.CreateSpeechToTextClientAsync(speechToTextDeployment);
                var speechLanguage = !string.IsNullOrWhiteSpace(language) ? language : "en-US";
                await StreamTranscriptionAsync(speechToTextClient, itemId, audioChunks, audioFormat, speechLanguage, cancellationToken);
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
    /// <param name="itemId">The item id.</param>
    /// <param name="text">The text.</param>
    /// <param name="voiceName">The voice name.</param>
    public virtual async Task SynthesizeSpeech(string itemId, string text, string voiceName = null)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(itemId)));
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
                var interactionManager = services.GetRequiredService<ICatalogManager<ChatInteraction>>();
                var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();
                var clientFactory = services.GetRequiredService<IAIClientFactory>();

                var interaction = await interactionManager.FindByIdAsync(itemId);
                if (interaction is null)
                {
                    await Clients.Caller.ReceiveError(GetInteractionNotFoundMessage());
                    return;
                }

                if (!await AuthorizeAsync(services, interaction))
                {
                    await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());
                    return;
                }

                var chatMode = await GetChatModeAsync(services);
                var isTtsPlaybackEnabled = await IsTextToSpeechPlaybackEnabledAsync(services);
                if (chatMode != ChatMode.Conversation && !isTtsPlaybackEnabled)
                {
                    await Clients.Caller.ReceiveError(GetTtsNotEnabledMessage());
                    return;
                }

                var deploymentSettings = await GetDeploymentSettingsAsync(services);
                var textToSpeechDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.TextToSpeech);
                if (textToSpeechDeployment is null)
                {
                    await Clients.Caller.ReceiveError(GetNoTtsDeploymentMessage());
                    return;
                }

                using var textToSpeechClient = await clientFactory.CreateTextToSpeechClientAsync(textToSpeechDeployment);
                var effectiveVoiceName = !string.IsNullOrWhiteSpace(voiceName)
                    ? voiceName
                    : deploymentSettings.DefaultTextToSpeechVoiceId;

                await StreamSpeechAsync(textToSpeechClient, itemId, text, effectiveVoiceName, cancellationToken);
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
    /// Gets the SignalR group name for a chat interaction.
    /// </summary>
    // -------------------------------------------------------------------
    //  PROTECTED IMPLEMENTATION - chat prompt processing pipeline
    // -------------------------------------------------------------------

    public static string GetInteractionGroupName(string itemId)
    {
        return $"chat-interaction-{itemId}";
    }

    /// <summary>
    /// Top-level handler for <see cref="SendMessage"/>. Validates input, resolves
    /// the interaction, and processes the streaming response.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="services">The service collection.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected virtual async Task HandlePromptAsync(
        ChannelWriter<CompletionPartialMessage> writer,
        IServiceProvider services,
        string itemId,
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var invocationScope = AIInvocationScope.Begin();

            if (string.IsNullOrWhiteSpace(itemId))
            {
                await Clients.Caller.ReceiveError(GetRequiredFieldMessage("Interaction ID"));
                return;
            }

            var interactionManager = services.GetRequiredService<ICatalogManager<ChatInteraction>>();
            var interaction = await interactionManager.FindByIdAsync(itemId, cancellationToken);
            if (interaction == null)
            {
                await Clients.Caller.ReceiveError(GetInteractionNotFoundMessage());
                return;
            }

            if (!await AuthorizeAsync(services, interaction))
            {
                await Clients.Caller.ReceiveError(GetNotAuthorizedMessage());
                return;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                await Clients.Caller.ReceiveError(GetRequiredFieldMessage(nameof(prompt)));
                return;
            }

            prompt = prompt.Trim();

            await Groups.AddToGroupAsync(Context.ConnectionId, GetInteractionGroupName(interaction.ItemId), cancellationToken);

            var promptStore = services.GetRequiredService<IChatInteractionPromptStore>();
            var handlerResolver = services.GetRequiredService<IChatResponseHandlerResolver>();

            var utcNow = GetUtcNow();
            var userPrompt = new ChatInteractionPrompt
            {
                ItemId = GenerateId(),
                ChatInteractionId = itemId,
                Role = ChatRole.User,
                Text = prompt,
                CreatedUtc = utcNow,
            };

            await promptStore.CreateAsync(userPrompt, cancellationToken);

            var needsTitleUpdate = string.IsNullOrEmpty(interaction.Title);
            if (needsTitleUpdate)
            {
                interaction.Title = prompt.Length > 255 ? prompt[..255] : prompt;
            }

            var existingPrompts = await promptStore.GetPromptsAsync(itemId);
            var conversationHistorySource = existingPrompts.ToList();

            if (!conversationHistorySource.Any(x => x.ItemId == userPrompt.ItemId))
            {
                conversationHistorySource.Add(userPrompt);
            }

            var conversationHistory = conversationHistorySource
                .OrderBy(x => x.CreatedUtc)
                .Where(x => !x.IsGeneratedPrompt)
                .Select(p => new ChatMessage(p.Role, p.Text))
                .ToList();

            var chatMode = await GetChatModeAsync(services);
            var handler = handlerResolver.Resolve(interaction.ResponseHandlerName, chatMode);

            var handlerContext = new ChatResponseHandlerContext
            {
                Prompt = prompt,
                ConnectionId = Context.ConnectionId,
                SessionId = interaction.ItemId,
                ChatType = GetChatContextType(),
                ConversationHistory = conversationHistory,
                Services = services,
                Interaction = interaction,
            };

            var handlerResult = await handler.HandleAsync(handlerContext, cancellationToken);

            if (handlerResult.IsDeferred)
            {
                if (needsTitleUpdate)
                {
                    await interactionManager.UpdateAsync(interaction, cancellationToken: cancellationToken);
                }

                await CommitChangesAsync(services);
                return;
            }

            var assistantPrompt = new ChatInteractionPrompt
            {
                ItemId = GenerateId(),
                ChatInteractionId = itemId,
                Role = ChatRole.Assistant,
                CreatedUtc = utcNow,
            };

            if (handlerContext.AssistantAppearance is not null)
            {
                assistantPrompt.Put(handlerContext.AssistantAppearance);
            }

            using var builder = ZString.CreateStringBuilder();
            var contentItemIds = new HashSet<string>();
            var references = new Dictionary<string, AICompletionReference>();

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
                    SessionId = interaction.ItemId,
                    MessageId = assistantPrompt.ItemId,
                    ResponseId = chunk.ResponseId,
                    Content = chunk.Text,
                    References = references,
                    Appearance = handlerContext.AssistantAppearance,
                };

                await writer.WriteAsync(partialMessage, cancellationToken);
            }

            CollectStreamingReferences(services, handlerContext, references, contentItemIds);

            if (builder.Length > 0)
            {
                assistantPrompt.Text = builder.ToString();
                assistantPrompt.References = references;
                await OnAssistantPromptCreatedAsync(services, assistantPrompt, contentItemIds);
                await promptStore.CreateAsync(assistantPrompt, cancellationToken);
            }

            if (needsTitleUpdate)
            {
                await interactionManager.UpdateAsync(interaction, cancellationToken: cancellationToken);
            }

            await CommitChangesAsync(services);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || (ex is TaskCanceledException && cancellationToken.IsCancellationRequested))
            {
                Logger.LogDebug("Chat interaction processing was cancelled.");
                return;
            }

            Logger.LogError(ex, "An error occurred while processing the chat interaction.");
            try
            {
                var errorMessage = new CompletionPartialMessage
                {
                    SessionId = itemId,
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
    /// Streams speech.
    /// </summary>
    // -------------------------------------------------------------------
    //  PROTECTED TTS / STT HELPERS - shared by conversation methods
    // -------------------------------------------------------------------

    protected async Task StreamSpeechAsync(
        ITextToSpeechClient textToSpeechClient,
        string identifier,
        string text,
        string voiceName,
        CancellationToken cancellationToken)
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
    /// Streams sentences as speech.
    /// </summary>
    /// <param name="textToSpeechClient">The text to speech client.</param>
    /// <param name="getIdentifier">The get identifier.</param>
    /// <param name="sentenceReader">The sentence reader.</param>
    /// <param name="voiceName">The voice name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected async Task StreamSentencesAsSpeechAsync(
        ITextToSpeechClient textToSpeechClient,
        Func<string> getIdentifier,
        ChannelReader<string> sentenceReader,
        string voiceName,
        CancellationToken cancellationToken)
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

    private async Task RunConversationLoopAsync(
        string itemId,
        IAsyncEnumerable<string> audioChunks,
        string audioFormat,
        string speechLanguage,
        ISpeechToTextClient speechToTextClient,
        ITextToSpeechClient textToSpeechClient,
        string voiceName,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var pipe = new Pipe();
        using var errorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var transcriptionTask = TranscribeConversationAsync(
            pipe.Reader, itemId, audioFormat, speechLanguage,
            speechToTextClient, textToSpeechClient, voiceName,
            services, errorCts, cancellationToken);

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

    private async Task TranscribeConversationAsync(
        PipeReader pipeReader,
        string itemId,
        string audioFormat,
        string speechLanguage,
        ISpeechToTextClient speechToTextClient,
        ITextToSpeechClient textToSpeechClient,
        string voiceName,
        IServiceProvider services,
        CancellationTokenSource errorCts,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource currentResponseCts = null;
        Task currentResponseTask = null;

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
                    var display = committedText.Length > 0
                        ? committedText.ToString() + update.Text
                        : update.Text;
                    await Clients.Caller.ReceiveTranscript(itemId, display, false);
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
                                await currentResponseTask;
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

                    await Clients.Caller.ReceiveTranscript(itemId, fullText, true);
                    await Clients.Caller.ReceiveConversationUserMessage(itemId, fullText);

                    committedText.Clear();

                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.LogDebug("TranscribeConversationAsync: Final utterance received: '{Text}'. Dispatching AI response.", fullText);
                    }

                    currentResponseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    currentResponseTask = ProcessConversationPromptAsync(
                        itemId, fullText, textToSpeechClient, voiceName,
                        services, currentResponseCts.Token);
                }
            }

            Logger.LogDebug("TranscribeConversationAsync: STT stream ended.");

            if (currentResponseTask != null)
            {
                try
                {
                    await currentResponseTask;
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
                await Clients.Caller.ReceiveConversationUserMessage(itemId, remainingText);
                try
                {
                    await ProcessConversationPromptAsync(
                        itemId, remainingText, textToSpeechClient, voiceName,
                        services, cancellationToken);
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

    private async Task ProcessConversationPromptAsync(
        string itemId,
        string prompt,
        ITextToSpeechClient textToSpeechClient,
        string voiceName,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("ProcessConversationPromptAsync: Starting for prompt length={PromptLength}.", prompt.Length);
        }

        var channel = Channel.CreateUnbounded<CompletionPartialMessage>();
        var handleTask = HandlePromptAsync(channel.Writer, services, itemId, prompt, cancellationToken);

        var sentenceChannel = Channel.CreateUnbounded<string>();

        string messageId = null;
        string responseId = null;

        var ttsTask = StreamSentencesAsSpeechAsync(
            textToSpeechClient, () => itemId, sentenceChannel.Reader,
            voiceName, cancellationToken);

        using var sentenceBuffer = ZString.CreateStringBuilder();

        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
            {
                messageId ??= chunk.MessageId;
                responseId ??= chunk.ResponseId;

                if (string.IsNullOrEmpty(chunk.Content))
                {
                    continue;
                }

                await Clients.Caller.ReceiveConversationAssistantToken(
                    itemId,
                    messageId ?? string.Empty,
                    chunk.Content,
                    responseId ?? string.Empty,
                    chunk.References,
                    chunk.Appearance);

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
                Logger.LogDebug("ProcessConversationPromptAsync: Completed. ItemId={ItemId}.", itemId);
            }
        }
        finally
        {
            sentenceChannel.Writer.TryComplete();
            sentenceBuffer.Dispose();

            if (!string.IsNullOrEmpty(messageId))
            {
                try
                {
                    await Clients.Caller.ReceiveConversationAssistantComplete(
                        itemId,
                        messageId,
                        await GetPromptReferencesAsync(services, itemId, messageId));
                }
                catch
                {
                    // Best-effort - the client may have disconnected.
                }
            }
        }
    }

    private static async Task<Dictionary<string, AICompletionReference>> GetPromptReferencesAsync(
        IServiceProvider services,
        string itemId,
        string messageId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var promptStore = services.GetRequiredService<IChatInteractionPromptStore>();
        var prompts = await promptStore.GetPromptsAsync(itemId);
        var prompt = prompts.FirstOrDefault(entry => string.Equals(entry.ItemId, messageId, StringComparison.Ordinal));

        return prompt?.References;
    }

    // ----------------- STT transcription (input mode) -----------------

    private async Task StreamTranscriptionAsync(
        ISpeechToTextClient speechToTextClient,
        string itemId,
        IAsyncEnumerable<string> audioChunks,
        string audioFormat,
        string speechLanguage,
        CancellationToken cancellationToken)
    {
        var pipe = new Pipe();
        using var errorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var transcriptionTask = TranscribeAudioInputAsync(
            itemId, pipe, audioFormat, speechLanguage,
            speechToTextClient, errorCts, cancellationToken);

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

    private async Task TranscribeAudioInputAsync(
        string itemId,
        Pipe pipe,
        string audioFormat,
        string speechLanguage,
        ISpeechToTextClient speechToTextClient,
        CancellationTokenSource errorCts,
        CancellationToken cancellationToken)
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
                    var display = committedText.Length > 0
                        ? committedText.ToString() + update.Text
                        : update.Text;
                    await Clients.Caller.ReceiveTranscript(itemId, display, false);
                }
                else
                {
                    if (committedText.Length > 0)
                    {
                        committedText.Append(' ');
                    }

                    committedText.Append(update.Text);
                    await Clients.Caller.ReceiveTranscript(itemId, committedText.ToString(), false);
                }
            }

            var finalText = committedText.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(finalText))
            {
                await Clients.Caller.ReceiveTranscript(itemId, finalText, true);
            }
        }
        catch (Exception)
        {
            await errorCts.CancelAsync();
            throw;
        }
    }

    // -------------------------------------------------------------------
    //  HELPERS
    // -------------------------------------------------------------------

    private static bool IsSpeechAuthenticationFailure(Exception ex)
    {
        var message = ex?.ToString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("AuthenticationFailure", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Authentication error (401)", StringComparison.OrdinalIgnoreCase)
            || message.Contains("check subscription information and region name", StringComparison.OrdinalIgnoreCase);
    }

    protected static class JsonHelper
    {
        /// <summary>
        /// Gets string.
        /// </summary>
        /// <param name="el">The el.</param>
        /// <param name="name">The name.</param>
        public static string GetString(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }

            return null;
        }

        /// <summary>
        /// Gets float.
        /// </summary>
        /// <param name="el">The el.</param>
        /// <param name="name">The name.</param>
        public static float? GetFloat(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetSingle();
                }

                if (prop.ValueKind == JsonValueKind.String && float.TryParse(prop.GetString(), out var f))
                {
                    return f;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets int.
        /// </summary>
        /// <param name="el">The el.</param>
        /// <param name="name">The name.</param>
        public static int? GetInt(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetInt32();
                }

                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var i))
                {
                    return i;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets bool.
        /// </summary>
        /// <param name="el">The el.</param>
        /// <param name="name">The name.</param>
        public static bool? GetBool(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (prop.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var b))
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets string array.
        /// </summary>
        /// <param name="el">The el.</param>
        /// <param name="name">The name.</param>
        public static List<string> GetStringArray(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            list.Add(value);
                        }
                    }
                }

                return list;
            }

            return [];
        }
    }
}
#pragma warning restore MEAI001
