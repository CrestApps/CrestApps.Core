using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIProfileHandler : CatalogEntryHandlerBase<AIProfile>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IAIProfileStore _store;
    private readonly IAIDeploymentStore _deploymentStore;

    internal readonly IStringLocalizer S;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="stringLocalizer">The string localizer.</param>
    public AIProfileHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IAIProfileStore store,
        IAIDeploymentStore deploymentStore,
        IStringLocalizer<AIProfileHandler> stringLocalizer)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _store = store;
        _deploymentStore = deploymentStore;
        S = stringLocalizer;
    }

    /// <summary>
    /// Initializings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializingAsync(InitializingContext<AIProfile> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, cancellationToken);

    /// <summary>
    /// Updatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task UpdatingAsync(UpdatingContext<AIProfile> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data, cancellationToken);

    /// <summary>
    /// Initializeds the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializedAsync(InitializedContext<AIProfile> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task CreatingAsync(CreatingContext<AIProfile> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task ValidatingAsync(ValidatingContext<AIProfile> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            context.Result.Fail(new ValidationResult(S["Name is required."], [nameof(AIProfile.Name)]));
        }

        if (context.Model.Type == AIProfileType.Agent && string.IsNullOrWhiteSpace(context.Model.Description))
        {
            context.Result.Fail(new ValidationResult(S["Description is required for agent profiles."], [nameof(AIProfile.Description)]));
        }

        if (context.Model.Type == AIProfileType.TemplatePrompt)
        {
            if (string.IsNullOrWhiteSpace(context.Model.PromptTemplate))
            {
                context.Result.Fail(new ValidationResult(S["Prompt template is required."], [nameof(AIProfile.PromptTemplate)]));
            }
        }

        return ValidateAsync(context, cancellationToken);
    }

    private void EnsureCreatedDefaults(AIProfile profile)
    {
        if (profile.CreatedUtc == default)
        {
            profile.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayText))
        {
            profile.DisplayText = profile.Name;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user == null)
        {
            return;
        }

        profile.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        profile.Author ??= user.Identity?.Name;
    }

    private async Task ValidateAsync(ValidatingContext<AIProfile> context, CancellationToken cancellationToken)
    {
        await ValidateUniqueNameAsync(context, cancellationToken);
        await ValidateDeploymentAsync(context.Model.ChatDeploymentName, nameof(AIProfile.ChatDeploymentName), context, cancellationToken);
        await ValidateDeploymentAsync(context.Model.UtilityDeploymentName, nameof(AIProfile.UtilityDeploymentName), context, cancellationToken);
    }

    private async Task ValidateUniqueNameAsync(ValidatingContext<AIProfile> context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            return;
        }

        var existing = await _store.FindByNameAsync(context.Model.Name, cancellationToken);

        if (existing is not null && !string.Equals(existing.ItemId, context.Model.ItemId, StringComparison.Ordinal))
        {
            context.Result.Fail(new ValidationResult(S["An AI profile with the same name already exists."], [nameof(AIProfile.Name)]));
        }
    }

    private async Task ValidateDeploymentAsync(string selector, string memberName, ValidatingContext<AIProfile> context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return;
        }

        if (await FindDeploymentAsync(selector, cancellationToken) is null)
        {
            context.Result.Fail(new ValidationResult(S["Invalid deployment selection provided."], [memberName]));
        }
    }

    private async Task PopulateAsync(AIProfile profile, JsonNode data, CancellationToken cancellationToken)
    {
        if (data is not JsonObject json)
        {
            return;
        }

        MergeProperties(profile, json);
        MergeSettings(profile, json);

        json.TryUpdateTrimmedStringValue(nameof(AIProfile.Name), value => profile.Name = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.DisplayText), value => profile.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.Source), value => profile.Source = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.Description), value => profile.Description = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.OrchestratorName), value => profile.OrchestratorName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.WelcomeMessage), value => profile.WelcomeMessage = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.PromptSubject), value => profile.PromptSubject = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.PromptTemplate), value => profile.PromptTemplate = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.OwnerId), value => profile.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.Author), value => profile.Author = value);

#pragma warning disable CS0618 // Type or member is obsolete
        json.TryUpdateTrimmedStringValue(nameof(AIProfile.ConnectionName), value => profile.ConnectionName = value);
#pragma warning restore CS0618 // Type or member is obsolete

        if (!json.TryUpdateTrimmedStringValue(nameof(AIProfile.ChatDeploymentName), value => profile.ChatDeploymentName = value))
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (!json.TryUpdateTrimmedStringValue(nameof(AIProfile.ChatDeploymentId), value => profile.ChatDeploymentName = value))
            {
                json.TryUpdateTrimmedStringValue(nameof(AIProfile.DeploymentId), value => profile.ChatDeploymentName = value);
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        profile.ChatDeploymentName = await ResolveLegacyDeploymentSelectionAsync(profile.ChatDeploymentName, cancellationToken);

        if (!json.TryUpdateTrimmedStringValue(nameof(AIProfile.UtilityDeploymentName), value => profile.UtilityDeploymentName = value))
        {
#pragma warning disable CS0618 // Type or member is obsolete
            json.TryUpdateTrimmedStringValue(nameof(AIProfile.UtilityDeploymentId), value => profile.UtilityDeploymentName = value);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        profile.UtilityDeploymentName = await ResolveLegacyDeploymentSelectionAsync(profile.UtilityDeploymentName, cancellationToken);

        if (json.TryGetEnumValue(nameof(AIProfile.Type), out AIProfileType type))
        {
            profile.Type = type;
        }

        if (json.TryGetNullableEnumValue(nameof(AIProfile.TitleType), out AISessionTitleType? titleType))
        {
            profile.TitleType = titleType;
        }

        if (json.TryGetDateTimeValue(nameof(AIProfile.CreatedUtc), out var createdUtc))
        {
            profile.CreatedUtc = createdUtc;
        }

        var settings = profile.GetSettings<AIProfileSettings>();
        var metadataUpdated = false;
        var settingsUpdated = false;
        var chatModeSettingsUpdated = false;
        var metadata = profile.GetOrCreate<AIProfileMetadata>();

        if (json.TryGetTrimmedStringValue(nameof(AIProfileMetadata.SystemMessage), out var systemMessage) && !settings.LockSystemMessage)
        {
            metadata.SystemMessage = systemMessage;
            metadataUpdated = true;
        }

        if (json.TryGetTrimmedStringValue(nameof(AIProfileMetadata.InitialPrompt), out var initialPrompt))
        {
            metadata.InitialPrompt = initialPrompt;
            metadataUpdated = true;
        }

        if (json.TryGetNullableSingleValue(nameof(AIProfileMetadata.Temperature), out var temperature))
        {
            metadata.Temperature = temperature;
            metadataUpdated = true;
        }

        if (json.TryGetNullableSingleValue(nameof(AIProfileMetadata.TopP), out var topP))
        {
            metadata.TopP = topP;
            metadataUpdated = true;
        }

        if (json.TryGetNullableSingleValue(nameof(AIProfileMetadata.FrequencyPenalty), out var frequencyPenalty))
        {
            metadata.FrequencyPenalty = frequencyPenalty;
            metadataUpdated = true;
        }

        if (json.TryGetNullableSingleValue(nameof(AIProfileMetadata.PresencePenalty), out var presencePenalty))
        {
            metadata.PresencePenalty = presencePenalty;
            metadataUpdated = true;
        }

        if (json.TryGetNullableInt32Value(nameof(AIProfileMetadata.MaxTokens), out var maxTokens))
        {
            metadata.MaxTokens = maxTokens;
            metadataUpdated = true;
        }

        if (json.TryGetNullableInt32Value(nameof(AIProfileMetadata.PastMessagesCount), out var pastMessagesCount))
        {
            metadata.PastMessagesCount = pastMessagesCount;
            metadataUpdated = true;
        }

        if (json.TryGetBooleanValue(nameof(AIProfileMetadata.UseCaching), out var useCaching))
        {
            metadata.UseCaching = useCaching;
            metadataUpdated = true;
        }

        if (metadataUpdated)
        {
            profile.Put(metadata);
        }

        if (json.TryGetBooleanValue(nameof(AIProfileSettings.LockSystemMessage), out var lockSystemMessage))
        {
            settings.LockSystemMessage = lockSystemMessage;
            settingsUpdated = true;
        }

        if (json.TryGetBooleanValue(nameof(AIProfileSettings.IsListable), out var isListable))
        {
            settings.IsListable = isListable;
            settingsUpdated = true;
        }

        if (json.TryGetBooleanValue(nameof(AIProfileSettings.IsRemovable), out var isRemovable))
        {
            settings.IsRemovable = isRemovable;
            settingsUpdated = true;
        }

        if (settingsUpdated)
        {
            profile.WithSettings(settings);
        }

        var chatModeSettings = profile.GetSettings<ChatModeProfileSettings>();

        if (json.TryGetEnumValue(nameof(ChatModeProfileSettings.ChatMode), out ChatMode chatMode))
        {
            chatModeSettings.ChatMode = chatMode;
            chatModeSettingsUpdated = true;
        }

        if (json.TryGetTrimmedStringValue(nameof(ChatModeProfileSettings.VoiceName), out var voiceName))
        {
            chatModeSettings.VoiceName = voiceName;
            chatModeSettingsUpdated = true;
        }

        if (json.TryGetBooleanValue(nameof(ChatModeProfileSettings.EnableTextToSpeechPlayback), out var enableTextToSpeechPlayback))
        {
            chatModeSettings.EnableTextToSpeechPlayback = enableTextToSpeechPlayback;
            chatModeSettingsUpdated = true;
        }

        if (chatModeSettingsUpdated)
        {
            profile.WithSettings(chatModeSettings);
        }

    }

    private async Task<string> ResolveLegacyDeploymentSelectionAsync(string selector, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return selector;
        }

        var deployment = await _deploymentStore.FindByIdAsync(selector, cancellationToken);

        return deployment?.Name ?? selector;
    }

    private async ValueTask<AIDeployment> FindDeploymentAsync(string selector, CancellationToken cancellationToken)
    {
        return await _deploymentStore.FindByIdAsync(selector, cancellationToken) ??
            await _deploymentStore.FindByNameAsync(selector, cancellationToken);
    }

    private static void MergeProperties(AIProfile profile, JsonObject json)
    {
        if (!json.TryGetObjectValue(nameof(AIProfile.Properties), out var properties) || properties == null)
        {
            return;
        }

        var currentJson = JsonExtensions.FromObject(profile.Properties ?? new Dictionary<string, object>(), ExtensibleEntityExtensions.JsonSerializerOptions);
        var existingPropertiesSnapshot = currentJson.Clone();

        AIPropertiesMergeHelper.Merge(currentJson, properties);
        AIPropertiesMergeHelper.MergeNamedEntries(currentJson, existingPropertiesSnapshot);

        profile.Properties = JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson, ExtensibleEntityExtensions.JsonSerializerOptions) ?? [];
    }

    private static void MergeSettings(AIProfile profile, JsonObject json)
    {
        if (!json.TryGetObjectValue(nameof(AIProfile.Settings), out var settings) || settings == null)
        {
            return;
        }

        var existingSettingsSnapshot = profile.Settings.Clone();

        AIPropertiesMergeHelper.Merge(profile.Settings, settings);
        AIPropertiesMergeHelper.MergeNamedEntries(profile.Settings, existingSettingsSnapshot);
    }
}
