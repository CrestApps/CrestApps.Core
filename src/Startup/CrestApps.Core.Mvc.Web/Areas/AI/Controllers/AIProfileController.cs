using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Mvc.Web.Areas.A2A.ViewModels;
using CrestApps.Core.Mvc.Web.Areas.AI.Services;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;
using CrestApps.Core.Mvc.Web.Areas.ChatInteractions.ViewModels;
using CrestApps.Core.Mvc.Web.Areas.Mcp.ViewModels;
using CrestApps.Core.Mvc.Web.Areas.Tooling.ViewModels;
using CrestApps.Core.Mvc.Web.Services;
using CrestApps.Core.Services;
using CrestApps.Core.Startup.Shared.Services;
using CrestApps.Core.Templates.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Controllers;

[Area("AI")]
[Authorize(Policy = "Admin")]
public sealed class AIProfileController : Controller
{
    private readonly IAIProfileManager _profileManager;
    private readonly ICatalog<AIDeployment> _deploymentCatalog;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIProfileTemplateManager _templateManager;
    private readonly ICatalog<A2AConnection> _a2aConnectionCatalog;
    private readonly ICatalog<McpConnection> _mcpConnectionCatalog;
    private readonly ISourceCatalog<AIToolInstance> _toolInstanceCatalog;
    private readonly IAIDocumentStore _documentStore;
    private readonly AIProfileDocumentService _profileDocumentService;
    private readonly AIProfileTemplateDocumentService _templateDocumentService;
    private readonly IOptionsMonitor<InteractionDocumentOptions> _interactionDocumentOptions;
    private readonly ISearchIndexProfileStore _indexProfileStore;
    private readonly ITemplateService _aiTemplateService;
    private readonly OrchestratorOptions _orchestratorOptions;
    private readonly IOptionsSnapshot<ClaudeOptions> _anthropicOptions;
    private readonly ClaudeClientService _anthropicClientService;
    private readonly IOptionsSnapshot<CopilotOptions> _copilotOptions;
    private readonly GitHubOAuthService _oauthService;
    private readonly AIToolDefinitionOptions _toolOptions;
    private readonly IAIToolAccessEvaluator _toolAccessEvaluator;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly AIDeploymentParameterViewService _modelParameterViewService;
    private readonly IAIDeploymentCapabilityService _capabilityService;
    private readonly IMcpServerMetadataCacheProvider _mcpMetadataProvider;

    public AIProfileController(
        IAIProfileManager profileManager,
        ICatalog<AIDeployment> deploymentCatalog,
        IAIDeploymentManager deploymentManager,
        IAIProfileTemplateManager templateManager,
        ICatalog<A2AConnection> a2aConnectionCatalog,
        ICatalog<McpConnection> mcpConnectionCatalog,
        ISourceCatalog<AIToolInstance> toolInstanceCatalog,
        IAIDocumentStore documentStore,
        AIProfileDocumentService profileDocumentService,
        AIProfileTemplateDocumentService templateDocumentService,
        IOptionsMonitor<InteractionDocumentOptions> interactionDocumentOptions,
        ISearchIndexProfileStore indexProfileStore,
        ITemplateService aiTemplateService,
        IOptions<OrchestratorOptions> orchestratorOptions,
        IOptionsSnapshot<ClaudeOptions> anthropicOptions,
        ClaudeClientService anthropicClientService,
        IOptionsSnapshot<CopilotOptions> copilotOptions,
        GitHubOAuthService oauthService,
        IOptions<AIToolDefinitionOptions> toolOptions,
        IAIToolAccessEvaluator toolAccessEvaluator,
        IAIDataSourceStore dataSourceStore,
        AIDeploymentParameterViewService modelParameterViewService,
        IAIDeploymentCapabilityService capabilityService,
        IMcpServerMetadataCacheProvider mcpMetadataProvider)
    {
        _profileManager = profileManager;
        _deploymentCatalog = deploymentCatalog;
        _deploymentManager = deploymentManager;
        _templateManager = templateManager;
        _a2aConnectionCatalog = a2aConnectionCatalog;
        _mcpConnectionCatalog = mcpConnectionCatalog;
        _toolInstanceCatalog = toolInstanceCatalog;
        _documentStore = documentStore;
        _profileDocumentService = profileDocumentService;
        _templateDocumentService = templateDocumentService;
        _interactionDocumentOptions = interactionDocumentOptions;
        _indexProfileStore = indexProfileStore;
        _aiTemplateService = aiTemplateService;
        _orchestratorOptions = orchestratorOptions.Value;
        _anthropicOptions = anthropicOptions;
        _anthropicClientService = anthropicClientService;
        _copilotOptions = copilotOptions;
        _oauthService = oauthService;
        _toolOptions = toolOptions.Value;
        _toolAccessEvaluator = toolAccessEvaluator;
        _dataSourceStore = dataSourceStore;
        _modelParameterViewService = modelParameterViewService;
        _capabilityService = capabilityService;
        _mcpMetadataProvider = mcpMetadataProvider;
    }

    public async Task<IActionResult> Index()
    {
        var profiles = await _profileManager.GetAllAsync();
        return View(profiles);
    }

    public async Task<IActionResult> Create([FromQuery] string templateId = null)
    {
        var model = new AIProfileViewModel
        {
            Type = AIProfileType.Chat,
            UseCaching = true,
            IsListable = true,
            IsRemovable = true
        };
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            var template = await _templateManager.FindByIdAsync(templateId);
            if (template != null)
            {
                var profile = new AIProfile
                {
                    Type = AIProfileType.Chat
                };
                ApplyTemplateToProfile(profile, template);
                model = AIProfileViewModel.FromProfile(profile);
                model.SelectedTemplateId = templateId;
                await NormalizeDeploymentSelectorsAsync(model);
                await PopulateAttachedDocumentsAsync(model, template.ItemId, AIReferenceTypes.Document.ProfileTemplate);
            }
        }

        await PopulateDropdownsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AIProfileViewModel model, List<IFormFile> Documents)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Name is required.");
        }

        await ValidateDeploymentCapabilitiesAsync(model);

        if (!ModelState.IsValid)
        {
            if (!string.IsNullOrWhiteSpace(model.SelectedTemplateId))
            {
                await PopulateAttachedDocumentsAsync(model, model.SelectedTemplateId, AIReferenceTypes.Document.ProfileTemplate);
            }

            await PopulateDropdownsAsync(model);
            return View(model);
        }

        var profile = new AIProfile
        {
            Type = AIProfileType.Chat
        };
        model.SelectedToolNames = await GetValidToolNamesAsync(model.SelectedToolNames);
        model.SelectedAgentNames = await GetValidAgentNamesAsync(model.SelectedAgentNames);
        model.SelectedA2AConnectionIds = await GetValidA2AConnectionIdsAsync(model.SelectedA2AConnectionIds);
        model.SelectedMcpConnectionIds = await GetValidMcpConnectionIdsAsync(model.SelectedMcpConnectionIds);
        model.SelectedToolInstanceNames = await GetValidToolInstanceNamesAsync(model.SelectedToolInstanceNames);
        model.ApplyTo(profile);
        // Assign ItemId early so document processing can use it as a reference.
        profile.ItemId = Guid.NewGuid().ToString("N");
        if (Documents is { Count: > 0 })
        {
            await _profileDocumentService.UploadDocumentsAsync(profile, Documents);
        }

        if (!string.IsNullOrWhiteSpace(model.SelectedTemplateId))
        {
            var template = await _templateManager.FindByIdAsync(model.SelectedTemplateId);
            if (template != null)
            {
                await _templateDocumentService.CloneDocumentsToProfileAsync(template, profile);
            }
        }

        await _profileManager.CreateAsync(profile);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var profile = await _profileManager.FindByIdAsync(id);
        if (profile == null)
        {
            return NotFound();
        }

        var model = AIProfileViewModel.FromProfile(profile);
        await PopulateAttachedDocumentsAsync(model, model.ItemId, AIReferenceTypes.Document.Profile);
        await NormalizeDeploymentSelectorsAsync(model);
        await PopulateDropdownsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AIProfileViewModel model, List<IFormFile> Documents, string[] RemovedDocumentIds)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Name is required.");
        }

        await ValidateDeploymentCapabilitiesAsync(model);

        if (!ModelState.IsValid)
        {
            await PopulateAttachedDocumentsAsync(model, model.ItemId, AIReferenceTypes.Document.Profile);
            await PopulateDropdownsAsync(model);
            return View(model);
        }

        var existing = await _profileManager.FindByIdAsync(model.ItemId);
        if (existing == null)
        {
            return NotFound();
        }

        model.SelectedToolNames = await GetValidToolNamesAsync(model.SelectedToolNames);
        model.SelectedAgentNames = await GetValidAgentNamesAsync(model.SelectedAgentNames);
        model.SelectedA2AConnectionIds = await GetValidA2AConnectionIdsAsync(model.SelectedA2AConnectionIds);
        model.SelectedMcpConnectionIds = await GetValidMcpConnectionIdsAsync(model.SelectedMcpConnectionIds);
        model.SelectedToolInstanceNames = await GetValidToolInstanceNamesAsync(model.SelectedToolInstanceNames);
        model.ApplyTo(existing);
        if (RemovedDocumentIds is { Length: > 0 })
        {
            await _profileDocumentService.RemoveDocumentsAsync(existing, RemovedDocumentIds);
        }

        if (Documents is { Count: > 0 })
        {
            await _profileDocumentService.UploadDocumentsAsync(existing, Documents);
        }

        await _profileManager.UpdateAsync(existing);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var profile = await _profileManager.FindByIdAsync(id);
        if (profile == null)
        {
            return NotFound();
        }

        await _profileDocumentService.RemoveAllDocumentsAsync(profile);
        await _profileManager.DeleteAsync(profile);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Validates that the selected deployments can perform the roles the profile assigns them: the chat
    /// deployment serves text turns, and the conversation deployment runs the voice session. A model that
    /// cannot do the job it was picked for would otherwise fail silently at request time.
    /// </summary>
    private async Task ValidateDeploymentCapabilitiesAsync(AIProfileViewModel model)
    {
        var deployments = await _deploymentCatalog.GetAllAsync();

        // The chat deployment is the text model this profile talks to. A speech-to-speech model cannot answer a
        // typed turn, which is exactly why the conversation deployment is a separate field.
        var chatDeployment = FindDeployment(deployments, model.ChatDeploymentName);

        if (chatDeployment is not null
            && (!_capabilityService.SupportsFeatureOrUnconstrained(chatDeployment, AIDeploymentFeatureNames.TextGeneration)
                || _capabilityService.GetCapabilities(chatDeployment).SupportsFeature(AIDeploymentFeatureNames.Realtime)))
        {
            ModelState.AddModelError(nameof(model.ChatDeploymentName), "The selected chat deployment cannot hold a text conversation. Choose a deployment whose model declares text generation, and name a speech-to-speech model as the conversation deployment instead.");
        }

        var conversationDeployment = FindDeployment(deployments, model.ConversationDeploymentName);

        if (conversationDeployment is not null
            && !_capabilityService.GetCapabilities(conversationDeployment).SupportsFeature(AIDeploymentFeatureNames.Realtime))
        {
            ModelState.AddModelError(nameof(model.ConversationDeploymentName), "The selected conversation deployment does not declare the 'realtime' capability. Choose a realtime-capable deployment, or clear the selection to use the site default.");
        }
    }

    private static AIDeployment FindDeployment(IEnumerable<AIDeployment> deployments, string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? null
            : deployments.FirstOrDefault(deployment => string.Equals(deployment.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PopulateDropdownsAsync(AIProfileViewModel model)
    {
        model.ModelParameterEditor = await _modelParameterViewService.BuildAsync(model.ModelParameters);
        model.UtilityModelParameterEditor = await _modelParameterViewService.BuildAsync(
            model.UtilityModelParameters,
            deploymentFieldName: nameof(AIProfileViewModel.UtilityDeploymentName),
            fieldPrefix: nameof(AIProfileViewModel.UtilityModelParameters),
            elementPrefix: "utilityModelParameters",
            title: "Utility model parameters");

        // The chat deployment is the text model the profile talks to, so the picker offers the chat slot only.
        // A speech-to-speech model belongs in the conversation deployment picker below it.
        model.ChatDeployments = (await _deploymentManager.GetAllBySlotAsync(AIDeploymentSlotNames.Chat)).Select(d => new SelectListItem(BuildDeploymentLabel(d), d.Name)).ToList();
        model.UtilityDeployments = (await _deploymentManager.GetAllBySlotAsync(AIDeploymentSlotNames.Utility)).Select(d => new SelectListItem(BuildDeploymentLabel(d), d.Name)).ToList();
        model.RealtimeDeployments = (await _deploymentManager.GetAllBySlotAsync(AIDeploymentSlotNames.Realtime)).Select(d => new SelectListItem(BuildDeploymentLabel(d), d.Name)).ToList();
        var orchestrators = _orchestratorOptions.GetOrchestratorDescriptors();
        var hasAnthropicOptions = _anthropicOptions.TryGetValidValue(out var anthropicOptions);
        model.Orchestrators = orchestrators.Select(o => new SelectListItem(o.Value.Title ?? o.Key, o.Key)).ToList();
        model.ClaudeIsConfigured = hasAnthropicOptions && anthropicOptions.IsConfigured();
        await PopulateClaudeModelsAsync(model, anthropicOptions);
        // Copilot
        var hasCopilotOptions = _copilotOptions.TryGetValidValue(out var copilotOptions);
        model.CopilotAuthenticationType = hasCopilotOptions ? (int)copilotOptions.AuthenticationType : 0;
        model.CopilotIsConfigured = hasCopilotOptions && copilotOptions.IsConfigured();

        if (hasCopilotOptions && copilotOptions.AuthenticationType == CopilotAuthenticationType.GitHubOAuth)
        {
            var userId = User.Identity?.Name;
            if (!string.IsNullOrEmpty(userId))
            {
                var isAuth = await _oauthService.IsAuthenticatedAsync(userId);
                model.CopilotIsAuthenticated = isAuth;
                if (isAuth)
                {
                    var cred = await _oauthService.GetCredentialAsync(userId);
                    model.CopilotGitHubUsername = cred?.GitHubUsername;
                    var models = await _oauthService.ListModelsAsync(userId);
                    model.CopilotAvailableModels = models.Select(m => new SelectListItem(FormatCopilotModelName(m), m.Id)).ToList();
                }
            }
        }

        var templates = await _templateManager.GetAllAsync();
        var listableTemplates = await _templateManager.GetListableAsync();
        model.Templates = templates.Select(t => new SelectListItem(t.DisplayText ?? t.Name, t.ItemId)).ToList();
        model.AvailableProfileTemplates = listableTemplates.Where(t => string.Equals(t.Source, AITemplateSources.Profile, StringComparison.OrdinalIgnoreCase)).Select(t => new SelectListItem(t.DisplayText ?? t.Name, t.ItemId)).ToList();
        model.AvailableSystemPromptTemplates = (await _aiTemplateService.GetByKindAsync(AITemplateSources.SystemPrompt))
            .Where(template => template.Metadata.IsListable)
            .OrderBy(template => template.Metadata.Title ?? template.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedNames = new HashSet<string>(model.SelectedToolNames ?? [], StringComparer.OrdinalIgnoreCase);
        // Only list tools the current user is authorized to select. The default IAIToolAccessEvaluator
        // permits everything; replace it to enforce a real per-user tool permission model.
        var authorizedTools = await SelectableToolAccessFilter.GetAuthorizedSelectableToolsAsync(_toolOptions, _toolAccessEvaluator, User);
        model.AvailableTools = authorizedTools.Select(kvp => new ToolSelectionItem { Name = kvp.Key, Title = kvp.Value.Title ?? kvp.Key, Description = kvp.Value.Description, Category = kvp.Value.Category ?? "Miscellaneous", IsSelected = selectedNames.Contains(kvp.Key), }).OrderBy(t => t.Category).ThenBy(t => t.Title).ToList();
        var connections = await _a2aConnectionCatalog.GetAllAsync();
        var selectedConnectionIds = new HashSet<string>(model.SelectedA2AConnectionIds ?? [], StringComparer.Ordinal);
        model.AvailableA2AConnections = connections.OrderBy(connection => connection.DisplayText, StringComparer.OrdinalIgnoreCase).Select(connection => new A2AConnectionSelectionItem { ItemId = connection.ItemId, DisplayText = connection.DisplayText, Endpoint = connection.Endpoint, IsSelected = selectedConnectionIds.Contains(connection.ItemId), }).ToList();
        var mcpConnections = await _mcpConnectionCatalog.GetAllAsync();
        var selectedMcpIds = new HashSet<string>(model.SelectedMcpConnectionIds ?? [], StringComparer.Ordinal);
        model.AvailableMcpConnections = mcpConnections.OrderBy(c => c.DisplayText, StringComparer.OrdinalIgnoreCase).Select(c => new McpConnectionSelectionItem { ItemId = c.ItemId, DisplayText = c.DisplayText, Source = c.Source, IsSelected = selectedMcpIds.Contains(c.ItemId), }).ToList();
        await PopulateMcpToolSelectionsAsync(model, mcpConnections);
        var toolInstances = await _toolInstanceCatalog.GetAllAsync();
        var selectedToolInstanceNames = new HashSet<string>(model.SelectedToolInstanceNames ?? [], StringComparer.OrdinalIgnoreCase);
        model.AvailableToolInstances = toolInstances.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => new AIToolInstanceSelectionItem { ItemId = i.ItemId, Name = i.Name, Description = i.Description, Source = i.Source, IsSelected = selectedToolInstanceNames.Contains(i.Name), }).ToList();
        var allAgents = await _profileManager.GetAsync(AIProfileType.Agent) ?? [];
        var selectedAgentNames = new HashSet<string>(model.SelectedAgentNames ?? [], StringComparer.OrdinalIgnoreCase);
        model.AvailableAgents = allAgents.Where(a => a.IsUserSelectableAgent()).OrderBy(a => a.DisplayText ?? a.Name, StringComparer.OrdinalIgnoreCase).Select(a => new AgentSelectionItem { Name = a.Name, DisplayText = a.DisplayText ?? a.Name, Description = a.Description, IsSelected = selectedAgentNames.Contains(a.Name), }).ToList();
        var allDataSources = await _dataSourceStore.GetAllAsync();
        model.DataSources = allDataSources.OrderBy(ds => ds.DisplayText, StringComparer.OrdinalIgnoreCase).Select(ds => new SelectListItem(ds.DisplayText, ds.ItemId)).ToList();
        var documentSettings = _interactionDocumentOptions.CurrentValue;
        model.DocumentIndexProfileName = documentSettings.IndexProfileName;
        if (!string.IsNullOrWhiteSpace(documentSettings.IndexProfileName))
        {
            var documentIndexProfile = await _indexProfileStore.FindByNameAsync(documentSettings.IndexProfileName);
            model.HasDocumentIndexConfiguration = documentIndexProfile != null && string.Equals(documentIndexProfile.Type, IndexProfileTypes.AIDocuments, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            model.HasDocumentIndexConfiguration = false;
        }

        var promptTemplates = await _aiTemplateService.GetByKindAsync(AITemplateSources.SystemPrompt);
        model.AvailablePromptTemplates = promptTemplates.Where(t => t.Metadata.IsListable).OrderBy(t => t.Metadata.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Metadata.Title ?? t.Id, StringComparer.OrdinalIgnoreCase).Select(t => new PromptTemplateOptionItem { TemplateId = t.Id, Title = t.Metadata.Title ?? t.Id, Description = t.Metadata.Description, Category = t.Metadata.Category ?? "General", Parameters = (t.Metadata.Parameters ?? []).Select(p => new PromptTemplateParameterItem { Name = p.Name, Description = p.Description, }).ToList(), }).ToList();
    }

    private async Task<string[]> GetValidToolNamesAsync(IEnumerable<string> selectedNames)
    {
        // Reject any persisted tool name the current user is not authorized to select, so a tampered
        // form post cannot save a listable tool the author lacks access to.
        var validToolNames = await SelectableToolAccessFilter.GetAuthorizedSelectableToolNamesAsync(_toolOptions, _toolAccessEvaluator, User);

        return (selectedNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name) && validToolNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string[]> GetValidAgentNamesAsync(IEnumerable<string> selectedNames)
    {
        var allAgents = await _profileManager.GetAsync(AIProfileType.Agent) ?? [];
        var validAgentNames = allAgents
            .Where(agent => agent.IsUserSelectableAgent())
            .Select(agent => agent.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (selectedNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name) && validAgentNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string[]> GetValidA2AConnectionIdsAsync(IEnumerable<string> selectedIds)
    {
        var allIds = (await _a2aConnectionCatalog.GetAllAsync()).Select(connection => connection.ItemId).ToHashSet(StringComparer.Ordinal);

        return (selectedIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id) && allIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Lists each selected connection's tools so the form can offer them individually. Only selected connections
    /// are asked, because it is a round-trip to the MCP server and there is no script on this page to defer it, so
    /// a newly ticked connection shows its tools after the profile is saved and reopened.
    /// </summary>
    private async Task PopulateMcpToolSelectionsAsync(AIProfileViewModel model, IEnumerable<McpConnection> mcpConnections)
    {
        var connectionsById = mcpConnections.ToDictionary(c => c.ItemId, StringComparer.Ordinal);

        foreach (var item in model.AvailableMcpConnections)
        {
            item.UseAllTools = !(model.SelectedMcpToolNames?.ContainsKey(item.ItemId) ?? false);

            if (!item.IsSelected || !connectionsById.TryGetValue(item.ItemId, out var connection))
            {
                continue;
            }

            try
            {
                var capabilities = await _mcpMetadataProvider.GetCapabilitiesAsync(connection);
                var selected = model.SelectedMcpToolNames is not null &&
                    model.SelectedMcpToolNames.TryGetValue(item.ItemId, out var names) &&
                    names is not null
                        ? new HashSet<string>(names, StringComparer.Ordinal)
                        : null;

                item.Tools = (capabilities?.Tools ?? [])
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(t => new McpToolSelectionItem { Name = t.Name, Description = t.Description, IsSelected = selected is null || selected.Contains(t.Name) })
                    .ToList();
                item.ToolsLoaded = true;
            }
            catch (Exception ex)
            {
                // A server that is down must not take the profile editor down with it.
                item.ToolsError = $"Could not load this connection's tools: {ex.Message}";
            }
        }
    }

    private async Task<string[]> GetValidMcpConnectionIdsAsync(IEnumerable<string> selectedIds)
    {
        var allIds = (await _mcpConnectionCatalog.GetAllAsync()).Select(c => c.ItemId).ToHashSet(StringComparer.Ordinal);

        return (selectedIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id) && allIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<string[]> GetValidToolInstanceNamesAsync(IEnumerable<string> selectedNames)
    {
        var allNames = (await _toolInstanceCatalog.GetAllAsync())
            .Select(i => i.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (selectedNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name) && allNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task PopulateAttachedDocumentsAsync(AIProfileViewModel model, string referenceId, string referenceType)
    {
        if (string.IsNullOrWhiteSpace(referenceId) || string.IsNullOrWhiteSpace(referenceType))
        {
            return;
        }

        var storedDocuments = await _documentStore.GetDocumentsAsync(referenceId, referenceType);

        var documentsById = (model.AttachedDocuments ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d.DocumentId))
            .ToDictionary(d => d.DocumentId, StringComparer.OrdinalIgnoreCase);

        foreach (var document in storedDocuments)
        {
            if (string.IsNullOrWhiteSpace(document.ItemId))
            {
                continue;
            }

            documentsById[document.ItemId] = new DocumentItem
            {
                DocumentId = document.ItemId,
                FileName = document.FileName,
                ContentType = document.ContentType,
                FileSize = document.FileSize,
            };
        }

        model.AttachedDocuments = documentsById.Values.OrderBy(d => d.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ApplyTemplateToProfile(AIProfile profile, AIProfileTemplate template)
    {
        if (template.TryGet<AIDeploymentParametersMetadata>(out var templateModelParameters) &&
            (templateModelParameters.Values is { Count: > 0 } || templateModelParameters.UtilityValues is { Count: > 0 }))
        {
            profile.Alter<AIDeploymentParametersMetadata>(m =>
            {
                foreach (var entry in templateModelParameters.Values ?? [])
                {
                    m.Values[entry.Key] = entry.Value;
                }

                foreach (var entry in templateModelParameters.UtilityValues ?? [])
                {
                    m.UtilityValues[entry.Key] = entry.Value;
                }
            });
        }

        if (!template.TryGet<ProfileTemplateMetadata>(out var metadata))
        {
            return;
        }

        if (metadata.ProfileType.HasValue)
        {
            profile.Type = metadata.ProfileType.Value;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ChatDeploymentName))
        {
            profile.ChatDeploymentName = metadata.ChatDeploymentName;
        }

        if (!string.IsNullOrWhiteSpace(metadata.UtilityDeploymentName))
        {
            profile.UtilityDeploymentName = metadata.UtilityDeploymentName;
        }

        // A template written before realtime became a model capability named its speech-to-speech model
        // separately. That model is the conversation deployment now, and naming one is what makes the profile
        // a voice profile.
#pragma warning disable CS0618 // Type or member is obsolete
        var conversationDeploymentName = !string.IsNullOrWhiteSpace(metadata.ConversationDeploymentName)
            ? metadata.ConversationDeploymentName
            : metadata.RealtimeDeploymentName;
#pragma warning restore CS0618 // Type or member is obsolete

        // Carry the chat mode (and its voice/TTS options) so a template can seed a voice profile.
        if (metadata.ChatMode.HasValue || !string.IsNullOrWhiteSpace(conversationDeploymentName) || !string.IsNullOrWhiteSpace(metadata.VoiceName) || metadata.EnableTextToSpeechPlayback.HasValue)
        {
            profile.AlterSettings<ChatModeProfileSettings>(chatModeSettings =>
            {
                if (metadata.ChatMode.HasValue)
                {
                    chatModeSettings.ChatMode = metadata.ChatMode.Value;
                }

                if (!string.IsNullOrWhiteSpace(conversationDeploymentName))
                {
                    chatModeSettings.ConversationDeploymentName = conversationDeploymentName;

                    // A template that names a model to speak with is asking for a spoken conversation. Without
                    // this the profile would carry the deployment and never use it.
                    if (!metadata.ChatMode.HasValue)
                    {
                        chatModeSettings.ChatMode = ChatMode.Conversation;
                    }
                }

                if (!string.IsNullOrWhiteSpace(metadata.VoiceName))
                {
                    chatModeSettings.VoiceName = metadata.VoiceName;
                }

                if (metadata.EnableTextToSpeechPlayback.HasValue)
                {
                    chatModeSettings.EnableTextToSpeechPlayback = metadata.EnableTextToSpeechPlayback.Value;
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(metadata.OrchestratorName))
        {
            profile.OrchestratorName = metadata.OrchestratorName;
        }

        if (!string.IsNullOrWhiteSpace(metadata.WelcomeMessage))
        {
            profile.WelcomeMessage = metadata.WelcomeMessage;
        }

        if (!string.IsNullOrWhiteSpace(metadata.PromptTemplate))
        {
            profile.PromptTemplate = metadata.PromptTemplate;
        }

        if (!string.IsNullOrWhiteSpace(metadata.PromptSubject))
        {
            profile.PromptSubject = metadata.PromptSubject;
        }

        if (metadata.TitleType.HasValue)
        {
            profile.TitleType = metadata.TitleType.Value;
        }

        if (metadata.AgentAvailability.HasValue || metadata.AllowToolInvocation.HasValue)
        {
            var agentMetadata = profile.GetOrCreate<AgentMetadata>();

            if (metadata.AgentAvailability.HasValue)
            {
                agentMetadata.Availability = metadata.AgentAvailability.Value;
            }

            if (metadata.AllowToolInvocation.HasValue)
            {
                agentMetadata.AllowToolInvocation = metadata.AllowToolInvocation.Value;
            }

            profile.Put(agentMetadata);
        }

        var profileMetadata = profile.GetOrCreate<AIProfileMetadata>();
        if (!string.IsNullOrWhiteSpace(metadata.SystemMessage))
        {
            profileMetadata.SystemMessage = metadata.SystemMessage;
        }

        if (metadata.Temperature.HasValue)
        {
            profileMetadata.Temperature = metadata.Temperature;
        }

        if (metadata.TopP.HasValue)
        {
            profileMetadata.TopP = metadata.TopP;
        }

        if (metadata.FrequencyPenalty.HasValue)
        {
            profileMetadata.FrequencyPenalty = metadata.FrequencyPenalty;
        }

        if (metadata.PresencePenalty.HasValue)
        {
            profileMetadata.PresencePenalty = metadata.PresencePenalty;
        }

        if (metadata.MaxOutputTokens.HasValue)
        {
            profileMetadata.MaxTokens = metadata.MaxOutputTokens;
        }

        if (metadata.PastMessagesCount.HasValue)
        {
            profileMetadata.PastMessagesCount = metadata.PastMessagesCount;
        }

        profile.Put(profileMetadata);
        if (metadata.ToolNames?.Length > 0)
        {
            profile.Put(new FunctionInvocationMetadata { Names = metadata.ToolNames, });
        }

        if (metadata.A2AConnectionIds?.Length > 0)
        {
            profile.Put(new AIProfileA2AMetadata { ConnectionIds = metadata.A2AConnectionIds, });
        }

        if (template.TryGet<CopilotSessionMetadata>(out var copilotMetadata))
        {
            profile.Put(new CopilotSessionMetadata { CopilotModel = copilotMetadata.CopilotModel, ReasoningEffort = copilotMetadata.ReasoningEffort, IsAllowAll = copilotMetadata.IsAllowAll, });
        }
        else
        {
            profile.Remove<CopilotSessionMetadata>();
        }

        if (template.TryGet<ClaudeSessionMetadata>(out var anthropicMetadata))
        {
            profile.Put(new ClaudeSessionMetadata { ClaudeModel = anthropicMetadata.ClaudeModel, EffortLevel = anthropicMetadata.EffortLevel, });
        }
        else
        {
            profile.Remove<ClaudeSessionMetadata>();
        }

        if (!string.IsNullOrWhiteSpace(metadata.Description))
        {
            profile.Description = metadata.Description;
        }
        // The template stores the session-document settings, but applying it dropped them: a profile
        // built from a template came out with uploads switched off, so neither the ceiling nor the
        // figure setting the template carried could ever take effect.
        if (template.TryGet<AIProfileSessionDocumentsMetadata>(out var templateSessionDocuments))
        {
            profile.Put(new AIProfileSessionDocumentsMetadata
            {
                AllowSessionDocuments = templateSessionDocuments.AllowSessionDocuments,
                AllowSessionImageUploads = templateSessionDocuments.AllowSessionImageUploads,
            });
        }

        if (template.TryGet<DocumentsMetadata>(out var templateDocuments))
        {
            // Altering rather than replacing keeps any documents already attached to the profile.
            profile.Alter<DocumentsMetadata>(documents =>
            {
                documents.DocumentTopN = templateDocuments.DocumentTopN;
                documents.RetrievalMode = templateDocuments.RetrievalMode;
                documents.MaxIndexableCharacters = templateDocuments.MaxIndexableCharacters;
                documents.DescribeFiguresInUploads = templateDocuments.DescribeFiguresInUploads;
            });
        }
    }

    private async Task NormalizeDeploymentSelectorsAsync(AIProfileViewModel model)
    {
        model.ChatDeploymentName = await NormalizeDeploymentSelectorAsync(model.ChatDeploymentName);
        model.UtilityDeploymentName = await NormalizeDeploymentSelectorAsync(model.UtilityDeploymentName);
        model.ConversationDeploymentName = await NormalizeDeploymentSelectorAsync(model.ConversationDeploymentName);

        // A profile stored before the conversation deployment existed names its speech-to-speech model as its
        // chat deployment, and the chat picker no longer offers it. Show it where it now belongs, so saving the
        // profile writes the current shape. The fold cannot happen while deserializing -- only the deployment's
        // own capability distinguishes such a profile, and that needs the catalog.
        var conversation = await _deploymentManager.ResolveConversationModeAsync(
            model.ChatMode,
            model.ConversationDeploymentName,
            model.ChatDeploymentName,
            hasSpeechToText: false,
            hasTextToSpeech: false);

        if (conversation.FoldedFromChatDeployment)
        {
            model.ConversationDeploymentName = conversation.RequestedDeploymentName;
            model.ChatMode = ChatMode.Conversation;
            model.ChatDeploymentName = null;
        }
    }

    private async Task<string> NormalizeDeploymentSelectorAsync(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return selector;
        }

        var deployment = await _deploymentCatalog.FindByIdAsync(selector);
        return deployment?.Name ?? selector;
    }

    private static string BuildDeploymentLabel(AIDeployment deployment)
    {
        return string.Equals(deployment.Name, deployment.ModelName, StringComparison.OrdinalIgnoreCase) ? deployment.Name : $"{deployment.Name} ({deployment.ModelName})";
    }

    private async Task PopulateClaudeModelsAsync(AIProfileViewModel model, ClaudeOptions anthropicOptions = null)
    {
        if (anthropicOptions is null || !anthropicOptions.IsConfigured())
        {
            model.AnthropicAvailableModels = ClaudeModelSelectListFactory.Build([], model.ClaudeModel, anthropicOptions?.DefaultModel);
            return;
        }

        var models = await _anthropicClientService.ListModelsAsync();
        model.AnthropicAvailableModels = ClaudeModelSelectListFactory.Build(models, model.ClaudeModel, anthropicOptions.DefaultModel);
    }

    private static string FormatCopilotModelName(CopilotModelInfo model)
    {
        var name = !string.IsNullOrWhiteSpace(model.Name) ? model.Name : model.Id;

        return model.CostMultiplier > 0
            ? $"{name} (x{model.CostMultiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})"
            : name;
    }
}
