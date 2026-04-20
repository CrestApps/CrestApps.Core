using System.Globalization;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Mvc.Web.Areas.Admin.ViewModels;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Models;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;
using CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Models;
using CrestApps.Core.Mvc.Web.Models;
using CrestApps.Core.Mvc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "Admin")]
public sealed class SettingsController : Controller
{
    private const string CopilotProtectorPurpose = "CrestApps.Core.Mvc.Web.CopilotSettings";
    private const string AnthropicProtectorPurpose = "CrestApps.Core.Mvc.Web.ClaudeSettings";
    private const string MemoryIndexProfileType = "AIMemory";

    private readonly SiteSettingsStore _siteSettings;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIProfileManager _profileManager;
    private readonly ISearchIndexProfileStore _indexProfileStore;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ISpeechVoiceResolver _speechVoiceResolver;
    private readonly ClaudeClientService _anthropicClientService;

    public SettingsController(
        SiteSettingsStore siteSettings,
        IAIDeploymentManager deploymentManager,
        IAIProfileManager profileManager,
        ISearchIndexProfileStore indexProfileStore,
        IDataProtectionProvider dataProtectionProvider,
        ISpeechVoiceResolver speechVoiceResolver,
        ClaudeClientService anthropicClientService)
    {
        _siteSettings = siteSettings;
        _deploymentManager = deploymentManager;
        _profileManager = profileManager;
        _indexProfileStore = indexProfileStore;
        _dataProtectionProvider = dataProtectionProvider;
        _speechVoiceResolver = speechVoiceResolver;
        _anthropicClientService = anthropicClientService;
    }

    public async Task<IActionResult> Index()
    {
        var settings = _siteSettings.Get<GeneralAISettings>();
        var chatInteractionSettings = _siteSettings.Get<ChatInteractionSettings>();
        var defaultOrchestratorSettings = _siteSettings.Get<DefaultOrchestratorSettings>();
        var deploymentDefaults = _siteSettings.Get<DefaultAIDeploymentSettings>();
        var memorySettings = _siteSettings.Get<AIMemorySettings>();
        var documentSettings = _siteSettings.Get<InteractionDocumentSettings>();
        var dataSourceSettings = _siteSettings.Get<AIDataSourceSettings>();
        var mcpServerSettings = _siteSettings.Get<McpServerOptions>();
        var chatInteractionMemorySettings = _siteSettings.Get<MemoryMetadata>();
        var copilotSettings = _siteSettings.Get<CopilotSettings>();
        var anthropicSettings = _siteSettings.Get<ClaudeSettings>();
        var paginationSettings = _siteSettings.Get<PaginationSettings>();
        var adminWidgetSettings = _siteSettings.Get<AIChatAdminWidgetSettings>();

        var model = new SettingsViewModel
        {
            EnableAIUsageTracking = settings.EnableAIUsageTracking,
            EnablePreemptiveMemoryRetrieval = settings.EnablePreemptiveMemoryRetrieval,
            MaximumIterationsPerRequest = settings.MaximumIterationsPerRequest,
            EnableDistributedCaching = settings.EnableDistributedCaching,
            EnableOpenTelemetry = settings.EnableOpenTelemetry,
            ChatInteractionChatMode = chatInteractionSettings.ChatMode,
            ChatInteractionEnableTextToSpeechPlayback = chatInteractionSettings.EnableTextToSpeechPlayback,
            DefaultOrchestratorEnablePreemptiveRag = defaultOrchestratorSettings.EnablePreemptiveRag,
            MemoryIndexProfileName = memorySettings.IndexProfileName,
            MemoryTopN = memorySettings.TopN,
            EnableUserMemoryByDefault = chatInteractionMemorySettings.EnableUserMemory ?? true,
            DefaultChatDeploymentName = deploymentDefaults.DefaultChatDeploymentName,
            DefaultUtilityDeploymentName = deploymentDefaults.DefaultUtilityDeploymentName,
            DefaultEmbeddingDeploymentName = deploymentDefaults.DefaultEmbeddingDeploymentName,
            DefaultImageDeploymentName = deploymentDefaults.DefaultImageDeploymentName,
            DefaultSpeechToTextDeploymentName = deploymentDefaults.DefaultSpeechToTextDeploymentName,
            DefaultTextToSpeechDeploymentName = deploymentDefaults.DefaultTextToSpeechDeploymentName,
            DefaultTextToSpeechVoiceId = deploymentDefaults.DefaultTextToSpeechVoiceId,
            DocumentIndexProfileName = documentSettings.IndexProfileName,
            DocumentTopN = documentSettings.TopN,
            DocumentRetrievalMode = documentSettings.RetrievalMode,
            DataSourceDefaultStrictness = dataSourceSettings.DefaultStrictness,
            DataSourceDefaultTopNDocuments = dataSourceSettings.DefaultTopNDocuments,
            McpServerAuthenticationType = mcpServerSettings.AuthenticationType,
            McpServerApiKey = mcpServerSettings.ApiKey,
            McpServerRequireAccessPermission = mcpServerSettings.RequireAccessPermission,
            CopilotAuthenticationType = copilotSettings.AuthenticationType,
            CopilotClientId = copilotSettings.ClientId,
            CopilotHasSecret = !string.IsNullOrWhiteSpace(copilotSettings.ProtectedClientSecret),
            CopilotProviderType = copilotSettings.ProviderType,
            CopilotBaseUrl = copilotSettings.BaseUrl,
            CopilotHasApiKey = !string.IsNullOrWhiteSpace(copilotSettings.ProtectedApiKey),
            CopilotWireApi = copilotSettings.WireApi ?? "completions",
            CopilotDefaultModel = copilotSettings.DefaultModel,
            CopilotAzureApiVersion = copilotSettings.AzureApiVersion,
            CopilotCallbackUrl = Url.Action("OAuthCallback", "CopilotAuth", new { area = "AIChat" }, Request.Scheme),
            AnthropicAuthenticationType = anthropicSettings.AuthenticationType,
            AnthropicBaseUrl = anthropicSettings.BaseUrl,
            AnthropicHasApiKey = !string.IsNullOrWhiteSpace(anthropicSettings.ProtectedApiKey),
            AnthropicDefaultModel = anthropicSettings.DefaultModel,
            AdminPageSize = paginationSettings.AdminPageSize,
            AdminWidgetProfileId = adminWidgetSettings.ProfileId,
            AdminWidgetPrimaryColor = string.IsNullOrWhiteSpace(adminWidgetSettings.PrimaryColor)
                ? AIChatAdminWidgetSettings.DefaultSecondaryColor
                : adminWidgetSettings.PrimaryColor,
        };

        await NormalizeDeploymentSelectorsAsync(model);
        await PopulateDeploymentDropdownsAsync(model);
        await PopulateAdminWidgetProfilesAsync(model);
        await PopulateClaudeModelsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SettingsViewModel model)
    {
        if (model.MaximumIterationsPerRequest < 1)
        {
            ModelState.AddModelError(nameof(model.MaximumIterationsPerRequest), "Must be at least 1.");
        }

        if (model.DocumentTopN < 1)
        {
            ModelState.AddModelError(nameof(model.DocumentTopN), "Must be at least 1.");
        }

        if (model.MemoryTopN < 1 || model.MemoryTopN > 20)
        {
            ModelState.AddModelError(nameof(model.MemoryTopN), "Must be between 1 and 20.");
        }

        if (model.DataSourceDefaultStrictness < AIDataSourceSettings.MinStrictness || model.DataSourceDefaultStrictness > AIDataSourceSettings.MaxStrictness)
        {
            ModelState.AddModelError(nameof(model.DataSourceDefaultStrictness), $"Must be between {AIDataSourceSettings.MinStrictness} and {AIDataSourceSettings.MaxStrictness}.");
        }

        if (model.DataSourceDefaultTopNDocuments < AIDataSourceSettings.MinTopNDocuments || model.DataSourceDefaultTopNDocuments > AIDataSourceSettings.MaxTopNDocuments)
        {
            ModelState.AddModelError(nameof(model.DataSourceDefaultTopNDocuments), $"Must be between {AIDataSourceSettings.MinTopNDocuments} and {AIDataSourceSettings.MaxTopNDocuments}.");
        }

        if (model.McpServerAuthenticationType == McpServerAuthenticationType.ApiKey &&
            string.IsNullOrWhiteSpace(model.McpServerApiKey))
        {
            ModelState.AddModelError(nameof(model.McpServerApiKey), "API key is required when the MCP server uses API key authentication.");
        }

        var existingAnthropic = _siteSettings.Get<ClaudeSettings>();
        if (model.AnthropicAuthenticationType == ClaudeAuthenticationType.ApiKey &&
            string.IsNullOrWhiteSpace(model.AnthropicApiKey) &&
            string.IsNullOrWhiteSpace(existingAnthropic.ProtectedApiKey))
        {
            ModelState.AddModelError(nameof(model.AnthropicApiKey), "API key is required when Claude uses API key authentication.");
        }

        if (model.AdminPageSize < 1 || model.AdminPageSize > 200)
        {
            ModelState.AddModelError(nameof(model.AdminPageSize), "Page size must be between 1 and 200.");
        }

        if (!string.IsNullOrWhiteSpace(model.AdminWidgetPrimaryColor) &&
            !Regex.IsMatch(
                model.AdminWidgetPrimaryColor,
                "^#(?:[0-9a-fA-F]{3}){1,2}$",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
        {
            ModelState.AddModelError(nameof(model.AdminWidgetPrimaryColor), "Color must be a valid hex value such as #6c757d.");
        }

        if (!string.IsNullOrWhiteSpace(model.AdminWidgetProfileId))
        {
            var profile = await _profileManager.FindByIdAsync(model.AdminWidgetProfileId);

            if (profile is null || profile.Type != AIProfileType.Chat)
            {
                ModelState.AddModelError(nameof(model.AdminWidgetProfileId), "Invalid admin widget profile.");
            }
        }

        if (!string.IsNullOrWhiteSpace(model.MemoryIndexProfileName))
        {
            var indexProfile = await _indexProfileStore.FindByNameAsync(model.MemoryIndexProfileName);

            if (indexProfile is null || !string.Equals(indexProfile.Type, MemoryIndexProfileType, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.MemoryIndexProfileName), "Invalid memory index profile.");
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDeploymentDropdownsAsync(model);
            await PopulateAdminWidgetProfilesAsync(model);
            await PopulateClaudeModelsAsync(model);

            return View(nameof(Index), model);
        }

        // Save general AI settings.
        _siteSettings.Set<GeneralAISettings>(settings =>
        {
            settings.EnablePreemptiveMemoryRetrieval = model.EnablePreemptiveMemoryRetrieval;
            settings.EnableAIUsageTracking = model.EnableAIUsageTracking;
            settings.MaximumIterationsPerRequest = model.MaximumIterationsPerRequest;
            settings.EnableDistributedCaching = model.EnableDistributedCaching;
            settings.EnableOpenTelemetry = model.EnableOpenTelemetry;
        });

        _siteSettings.Set<ChatInteractionSettings>(settings =>
        {
            settings.ChatMode = model.ChatInteractionChatMode;
            settings.EnableTextToSpeechPlayback = model.ChatInteractionEnableTextToSpeechPlayback;
        });

        _siteSettings.Set(new DefaultOrchestratorSettings
        {
            EnablePreemptiveRag = model.DefaultOrchestratorEnablePreemptiveRag,
        });

        _siteSettings.Set(new DefaultAIDeploymentSettings
        {
            DefaultChatDeploymentName = model.DefaultChatDeploymentName,
            DefaultUtilityDeploymentName = model.DefaultUtilityDeploymentName,
            DefaultEmbeddingDeploymentName = model.DefaultEmbeddingDeploymentName,
            DefaultImageDeploymentName = model.DefaultImageDeploymentName,
            DefaultSpeechToTextDeploymentName = model.DefaultSpeechToTextDeploymentName,
            DefaultTextToSpeechDeploymentName = model.DefaultTextToSpeechDeploymentName,
            DefaultTextToSpeechVoiceId = model.DefaultTextToSpeechVoiceId?.Trim(),
        });

        _siteSettings.Set(new AIMemorySettings
        {
            IndexProfileName = string.IsNullOrWhiteSpace(model.MemoryIndexProfileName)
                ? null
                : model.MemoryIndexProfileName.Trim(),
            TopN = model.MemoryTopN,
        });

        _siteSettings.Set(new InteractionDocumentSettings
        {
            IndexProfileName = model.DocumentIndexProfileName?.Trim(),
            TopN = model.DocumentTopN,
            RetrievalMode = model.DocumentRetrievalMode,
        });

        _siteSettings.Set(new AIDataSourceSettings
        {
            DefaultStrictness = model.DataSourceDefaultStrictness,
            DefaultTopNDocuments = model.DataSourceDefaultTopNDocuments,
        });

        _siteSettings.Set(new McpServerOptions
        {
            AuthenticationType = model.McpServerAuthenticationType,
            ApiKey = model.McpServerApiKey?.Trim(),
            RequireAccessPermission = model.McpServerRequireAccessPermission,
        });

        _siteSettings.Set(new MemoryMetadata
        {
            EnableUserMemory = model.EnableUserMemoryByDefault,
        });

        // Save Copilot settings, preserving existing protected secrets.
        var existingCopilot = _siteSettings.Get<CopilotSettings>();
        var protector = _dataProtectionProvider.CreateProtector(CopilotProtectorPurpose);

        var copilotSettings = new CopilotSettings
        {
            AuthenticationType = model.CopilotAuthenticationType,
            ClientId = model.CopilotClientId?.Trim(),
            ProtectedClientSecret = existingCopilot.ProtectedClientSecret,
            ProviderType = model.CopilotProviderType,
            BaseUrl = model.CopilotBaseUrl?.Trim(),
            ProtectedApiKey = existingCopilot.ProtectedApiKey,
            WireApi = model.CopilotWireApi,
            DefaultModel = model.CopilotDefaultModel?.Trim(),
            AzureApiVersion = model.CopilotAzureApiVersion?.Trim(),
        };

        if (!string.IsNullOrWhiteSpace(model.CopilotClientSecret))
        {
            copilotSettings.ProtectedClientSecret = protector.Protect(model.CopilotClientSecret.Trim());
        }

        if (!string.IsNullOrWhiteSpace(model.CopilotApiKey))
        {
            copilotSettings.ProtectedApiKey = protector.Protect(model.CopilotApiKey.Trim());
        }

        _siteSettings.Set(copilotSettings);

        var anthropicProtector = _dataProtectionProvider.CreateProtector(AnthropicProtectorPurpose);
        var anthropicSettings = new ClaudeSettings
        {
            AuthenticationType = model.AnthropicAuthenticationType,
            BaseUrl = string.IsNullOrWhiteSpace(model.AnthropicBaseUrl)
                ? "https://api.anthropic.com"
                : model.AnthropicBaseUrl.Trim(),
            ProtectedApiKey = existingAnthropic.ProtectedApiKey,
            DefaultModel = model.AnthropicDefaultModel?.Trim(),
        };

        if (!string.IsNullOrWhiteSpace(model.AnthropicApiKey))
        {
            anthropicSettings.ProtectedApiKey = anthropicProtector.Protect(model.AnthropicApiKey.Trim());
        }

        _siteSettings.Set(anthropicSettings);

        _siteSettings.Set(new PaginationSettings
        {
            AdminPageSize = model.AdminPageSize,
        });

        _siteSettings.Set(new AIChatAdminWidgetSettings
        {
            ProfileId = string.IsNullOrWhiteSpace(model.AdminWidgetProfileId) ? null : model.AdminWidgetProfileId.Trim(),
            PrimaryColor = string.IsNullOrWhiteSpace(model.AdminWidgetPrimaryColor)
                ? AIChatAdminWidgetSettings.DefaultSecondaryColor
                : model.AdminWidgetPrimaryColor.Trim(),
        });

        // Persist everything to disk in a single atomic file write.
        await _siteSettings.SaveChangesAsync();

        TempData["SuccessMessage"] = "Settings saved successfully.";

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDeploymentDropdownsAsync(SettingsViewModel model)
    {
        model.ChatDeployments = BuildGroupedDeploymentItems(
            await _deploymentManager.GetByTypeAsync(AIDeploymentType.Chat));

        model.UtilityDeployments = BuildGroupedDeploymentItems(
            await _deploymentManager.GetByTypeAsync(AIDeploymentType.Utility));

        model.EmbeddingDeployments = BuildGroupedDeploymentItems(
            await _deploymentManager.GetByTypeAsync(AIDeploymentType.Embedding));

        model.ImageDeployments = BuildGroupedDeploymentItems(
            await _deploymentManager.GetByTypeAsync(AIDeploymentType.Image));

        model.SpeechToTextDeployments = BuildGroupedDeploymentItems(
            await _deploymentManager.GetByTypeAsync(AIDeploymentType.SpeechToText));

        model.TextToSpeechDeployments = BuildGroupedDeploymentItems(
            await _deploymentManager.GetByTypeAsync(AIDeploymentType.TextToSpeech));

        model.ChatInteractionModes =
        [
            new SelectListItem("Text input", nameof(ChatMode.TextInput)),
            new SelectListItem("Audio input", nameof(ChatMode.AudioInput)),
            new SelectListItem("Conversation", nameof(ChatMode.Conversation)),
        ];

        model.DocumentIndexProfiles = (await _indexProfileStore.GetByTypeAsync(IndexProfileTypes.AIDocuments))
            .OrderBy(profile => profile.DisplayText ?? profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new SelectListItem(profile.DisplayText ?? profile.Name, profile.Name));

        model.MemoryIndexProfiles = (await _indexProfileStore.GetByTypeAsync(MemoryIndexProfileType))
            .OrderBy(profile => profile.DisplayText ?? profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new SelectListItem(profile.DisplayText ?? profile.Name, profile.Name));
    }

    private async Task PopulateAdminWidgetProfilesAsync(SettingsViewModel model)
    {
        model.AdminWidgetProfiles = (await _profileManager.GetAsync(AIProfileType.Chat))
            .OrderBy(profile => profile.DisplayText ?? profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new SelectListItem(
                profile.DisplayText ?? profile.Name,
                profile.ItemId,
                profile.ItemId == model.AdminWidgetProfileId));
    }

    private async Task PopulateClaudeModelsAsync(SettingsViewModel model)
    {
        var settings = _siteSettings.Get<ClaudeSettings>();
        if (!settings.IsConfigured())
        {
            model.AnthropicAvailableModels = ClaudeModelSelectListFactory.Build([], model.AnthropicDefaultModel);
            return;
        }

        var models = await _anthropicClientService.ListModelsAsync();
        model.AnthropicAvailableModels = ClaudeModelSelectListFactory.Build(models, model.AnthropicDefaultModel, settings.DefaultModel);
    }

    private static IEnumerable<SelectListItem> BuildGroupedDeploymentItems(IEnumerable<AIDeployment> deployments)
    {
        var groups = new Dictionary<string, SelectListGroup>(StringComparer.OrdinalIgnoreCase);

        return deployments
            .OrderBy(d => d.ConnectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d =>
            {
                SelectListGroup group = null;
                var groupKey = d.ConnectionName;

                if (!string.IsNullOrEmpty(groupKey) && !groups.TryGetValue(groupKey, out group))
                {
                    group = new SelectListGroup { Name = groupKey };

                    groups[groupKey] = group;
                }

                var label = string.Equals(d.Name, d.ModelName, StringComparison.OrdinalIgnoreCase)
                ? d.Name
                : $"{d.Name} ({d.ModelName})";

                return new SelectListItem(label, d.Name) { Group = group };
            });
    }

    private async Task NormalizeDeploymentSelectorsAsync(SettingsViewModel model)
    {
        model.DefaultChatDeploymentName = await NormalizeDeploymentSelectorAsync(model.DefaultChatDeploymentName);
        model.DefaultUtilityDeploymentName = await NormalizeDeploymentSelectorAsync(model.DefaultUtilityDeploymentName);
        model.DefaultEmbeddingDeploymentName = await NormalizeDeploymentSelectorAsync(model.DefaultEmbeddingDeploymentName);
        model.DefaultImageDeploymentName = await NormalizeDeploymentSelectorAsync(model.DefaultImageDeploymentName);
        model.DefaultSpeechToTextDeploymentName = await NormalizeDeploymentSelectorAsync(model.DefaultSpeechToTextDeploymentName);
        model.DefaultTextToSpeechDeploymentName = await NormalizeDeploymentSelectorAsync(model.DefaultTextToSpeechDeploymentName);
    }

    private async Task<string> NormalizeDeploymentSelectorAsync(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return selector;
        }

        var deployment = await _deploymentManager.FindByIdAsync(selector);

        return deployment?.Name ?? selector;
    }

    [HttpGet]
    public async Task<IActionResult> GetVoices(string deploymentName)
    {
        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            return Json(new { voices = Array.Empty<object>() });
        }

        var deployment = await _deploymentManager.FindByNameAsync(deploymentName);

        if (deployment is null)
        {
            return Json(new { voices = Array.Empty<object>() });
        }

        var voices = (await _speechVoiceResolver.GetSpeechVoicesAsync(deployment))
            .OrderBy(voice => voice.Language, StringComparer.OrdinalIgnoreCase)
            .ThenBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)
            .Select(voice => new
            {
                voice.Id,
                voice.Name,
                voice.Language,
                LanguageDisplayName = GetCultureDisplayName(voice.Language),
                Gender = voice.Gender.ToString(),
            });

        return Json(new { voices });
    }

    private static string GetCultureDisplayName(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "Unknown";
        }

        try
        {
            return CultureInfo.GetCultureInfo(language).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return language;
        }
    }
}
