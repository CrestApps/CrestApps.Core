using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class AITemplateViewModel
{
    public string ItemId { get; set; }

    public string Name { get; set; }

    public string DisplayText { get; set; }

    public string Description { get; set; }

    public string Category { get; set; }

    public bool IsListable { get; set; } = true;

    public string Source { get; set; } = AITemplateSources.SystemPrompt;

    // SystemPrompt source fields.
    public string SystemMessage { get; set; }

    // Profile source fields.
    public AIProfileType? ProfileType { get; set; }

    public string ChatDeploymentName { get; set; }

    public string UtilityDeploymentName { get; set; }

    public string OrchestratorName { get; set; }

    public AISessionTitleType? TitleType { get; set; }

    public float? Temperature { get; set; }

    public float? TopP { get; set; }

    public float? FrequencyPenalty { get; set; }

    public float? PresencePenalty { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? PastMessagesCount { get; set; }

    public string WelcomeMessage { get; set; }

    public string PromptTemplate { get; set; }

    public string PromptSubject { get; set; }

    public bool UseCaching { get; set; } = true;

    public bool AddInitialPrompt { get; set; }

    public string InitialPrompt { get; set; }

    // Tool selection for Profile templates.
    public string[] SelectedToolNames { get; set; } = [];

    public List<ToolSelectionItem> AvailableTools { get; set; } = [];

    public string[] SelectedA2AConnectionIds { get; set; } = [];

    public List<A2AConnectionSelectionItem> AvailableA2AConnections { get; set; } = [];

    // Agents.
    public string[] SelectedAgentNames { get; set; } = [];

    public List<AgentSelectionItem> AvailableAgents { get; set; } = [];

    // MCP Connections.
    public string[] SelectedMcpConnectionIds { get; set; } = [];

    public List<McpConnectionSelectionItem> AvailableMcpConnections { get; set; } = [];

    // Data Source.
    public string DataSourceId { get; set; }

    public int? DataSourceStrictness { get; set; }

    public int? DataSourceTopNDocuments { get; set; }

    public bool DataSourceIsInScope { get; set; }

    public string DataSourceFilter { get; set; }

    // Prompt Templates.
    public List<PromptTemplateSelectionItem> PromptTemplates { get; set; } = [];

    public List<PromptTemplateOptionItem> AvailablePromptTemplates { get; set; } = [];

    // Documents.
    public bool AllowSessionDocuments { get; set; }

    public int? DocumentTopN { get; set; }

    public DocumentRetrievalMode? DocumentRetrievalMode { get; set; }

    public bool HasDocumentIndexConfiguration { get; set; }

    public string DocumentIndexProfileName { get; set; }

    public List<DocumentItem> AttachedDocuments { get; set; } = [];

    // Data Extraction.
    public bool EnableDataExtraction { get; set; }

    public int ExtractionCheckInterval { get; set; } = 1;

    public int SessionInactivityTimeoutInMinutes { get; set; } = 30;

    public List<DataExtractionEntryItem> DataExtractionEntries { get; set; } = [];

    // Analytics.
    public bool EnableSessionMetrics { get; set; }

    public bool EnableAIResolutionDetection { get; set; } = true;

    public bool EnableConversionMetrics { get; set; }

    public List<ConversionGoalItem> ConversionGoals { get; set; } = [];

    // Post Session Processing.
    public bool EnablePostSessionProcessing { get; set; }

    public List<PostSessionTaskItem> PostSessionTasks { get; set; } = [];

    // Memory.
    public bool EnableUserMemory { get; set; }

    // Claude.
    public string ClaudeModel { get; set; }

    public ClaudeEffortLevel ClaudeEffortLevel { get; set; }

    public bool ClaudeIsConfigured { get; set; }

    // Settings.
    public bool IsRemovable { get; set; } = true;

    public bool LockSystemMessage { get; set; }

    // Copilot.
    public string CopilotModel { get; set; }

    public CopilotReasoningEffort CopilotReasoningEffort { get; set; }

    public bool CopilotIsAllowAll { get; set; }

    public bool CopilotIsConfigured { get; set; }

    public bool CopilotIsAuthenticated { get; set; }

    public string CopilotGitHubUsername { get; set; }

    public CopilotAuthenticationType CopilotAuthenticationType { get; set; }

    // Dropdown options (populated at load time)
    public List<KeyValuePair<string, string>> ChatDeployments { get; set; } = [];

    public List<KeyValuePair<string, string>> UtilityDeployments { get; set; } = [];

    public List<KeyValuePair<string, string>> Orchestrators { get; set; } = [];

    public List<KeyValuePair<string, string>> CopilotAvailableModels { get; set; } = [];

    public List<KeyValuePair<string, string>> AnthropicAvailableModels { get; set; } = [];

    public List<KeyValuePair<string, string>> DataSources { get; set; } = [];

    public static AITemplateViewModel FromTemplate(AIProfileTemplate template)
    {
        var model = new AITemplateViewModel
        {
            ItemId = template.ItemId,
            Name = template.Name,
            DisplayText = template.DisplayText,
            Description = template.Description,
            Category = template.Category,
            IsListable = template.IsListable,
            Source = template.Source,
        };

        if (template.Source == AITemplateSources.SystemPrompt)
        {
            if (template.TryGet<SystemPromptTemplateMetadata>(out var sysMetadata))
            {
                model.SystemMessage = sysMetadata.SystemMessage;
            }
        }
        else if (template.Source == AITemplateSources.Profile)
        {
            if (template.TryGet<ProfileTemplateMetadata>(out var metadata))
            {
                model.ProfileType = metadata.ProfileType;
                model.SystemMessage = metadata.SystemMessage;
                model.ChatDeploymentName = metadata.ChatDeploymentName;
                model.UtilityDeploymentName = metadata.UtilityDeploymentName;
                model.OrchestratorName = metadata.OrchestratorName;
                model.TitleType = metadata.TitleType;
                model.Temperature = metadata.Temperature;
                model.TopP = metadata.TopP;
                model.FrequencyPenalty = metadata.FrequencyPenalty;
                model.PresencePenalty = metadata.PresencePenalty;
                model.MaxOutputTokens = metadata.MaxOutputTokens;
                model.PastMessagesCount = metadata.PastMessagesCount;
                model.WelcomeMessage = metadata.WelcomeMessage;
                model.PromptTemplate = metadata.PromptTemplate;
                model.PromptSubject = metadata.PromptSubject;
                model.SelectedToolNames = metadata.ToolNames ?? [];
                model.SelectedA2AConnectionIds = metadata.A2AConnectionIds ?? [];
                model.SelectedAgentNames = metadata.AgentNames ?? [];
            }

            if (template.TryGet<AIProfileMcpMetadata>(out var mcpMetadata))
            {
                model.SelectedMcpConnectionIds = mcpMetadata.ConnectionIds ?? [];
            }

            if (template.TryGet<AIProfileMetadata>(out var aiMetadata))
            {
                model.UseCaching = aiMetadata.UseCaching;
                model.AddInitialPrompt = !string.IsNullOrEmpty(aiMetadata.InitialPrompt);
                model.InitialPrompt = aiMetadata.InitialPrompt;
            }

            if (template.TryGet<PromptTemplateMetadata>(out var promptMetadata))
            {
                model.PromptTemplates = (promptMetadata.Templates ?? [])
                    .Where(t => !string.IsNullOrWhiteSpace(t.TemplateId))
                    .Select(t => new PromptTemplateSelectionItem
                    {
                        TemplateId = t.TemplateId,
                        PromptParameters = t.Parameters is { Count: > 0 }
                            ? JsonSerializer.Serialize(t.Parameters)
                            : null,
                    })
                    .ToList();
            }

            if (template.TryGet<DataSourceMetadata>(out var dataSourceMetadata))
            {
                model.DataSourceId = dataSourceMetadata.DataSourceId;
            }

            if (template.TryGet<AIDataSourceRagMetadata>(out var ragMetadata))
            {
                model.DataSourceStrictness = ragMetadata.Strictness;
                model.DataSourceTopNDocuments = ragMetadata.TopNDocuments;
                model.DataSourceIsInScope = ragMetadata.IsInScope;
                model.DataSourceFilter = ragMetadata.Filter;
            }

            if (template.TryGet<AIProfileSessionDocumentsMetadata>(out var sessionDocMetadata))
            {
                model.AllowSessionDocuments = sessionDocMetadata.AllowSessionDocuments;
            }

            if (template.TryGet<DocumentsMetadata>(out var docMetadata))
            {
                model.DocumentTopN = docMetadata.DocumentTopN;
                model.DocumentRetrievalMode = docMetadata.RetrievalMode;
            }

            if (template.TryGet<AIProfileDataExtractionSettings>(out var dataExtractionSettings))
            {
                model.EnableDataExtraction = dataExtractionSettings.EnableDataExtraction;
                model.ExtractionCheckInterval = dataExtractionSettings.ExtractionCheckInterval;
                model.SessionInactivityTimeoutInMinutes = dataExtractionSettings.SessionInactivityTimeoutInMinutes;
                model.DataExtractionEntries = dataExtractionSettings.DataExtractionEntries
                    .Select(e => new DataExtractionEntryItem
                    {
                        Name = e.Name,
                        Description = e.Description,
                        AllowMultipleValues = e.AllowMultipleValues,
                        IsUpdatable = e.IsUpdatable,
                    })
                .ToList();
            }

            if (template.TryGet<AnalyticsMetadata>(out var analyticsMetadata))
            {
                model.EnableSessionMetrics = analyticsMetadata.EnableSessionMetrics;
                model.EnableAIResolutionDetection = analyticsMetadata.EnableAIResolutionDetection;
                model.EnableConversionMetrics = analyticsMetadata.EnableConversionMetrics;
                model.ConversionGoals = analyticsMetadata.ConversionGoals
                    .Select(g => new ConversionGoalItem
                    {
                        Name = g.Name,
                        Description = g.Description,
                        MinScore = g.MinScore,
                        MaxScore = g.MaxScore,
                    })
                .ToList();
            }

            if (template.TryGet<AIProfilePostSessionSettings>(out var postSessionSettings))
            {
                model.EnablePostSessionProcessing = postSessionSettings.EnablePostSessionProcessing;
                model.PostSessionTasks = postSessionSettings.PostSessionTasks
                    .Select(t => new PostSessionTaskItem
                    {
                        Name = t.Name,
                        Type = t.Type,
                        Instructions = t.Instructions,
                        AllowMultipleValues = t.AllowMultipleValues,
                        Options = string.Join(Environment.NewLine, t.Options.Select(o => o.Value)),
                        SelectedToolNames = t.ToolNames ?? [],
                        SelectedAgentNames = t.AgentNames ?? [],
                        SelectedA2AConnectionIds = t.A2AConnectionIds ?? [],
                        SelectedMcpConnectionIds = t.McpConnectionIds ?? [],
                    })
                .ToList();
            }

            model.EnableUserMemory = template.GetMemoryMetadata().EnableUserMemory ?? false;

            if (template.TryGet<AIProfileSettings>(out var profileSettings))
            {
                model.IsRemovable = profileSettings.IsRemovable;
                model.LockSystemMessage = profileSettings.LockSystemMessage;
            }

            if (template.TryGet<CopilotSessionMetadata>(out var copilotMetadata))
            {
                model.CopilotModel = copilotMetadata.CopilotModel;
                model.CopilotReasoningEffort = copilotMetadata.ReasoningEffort;
                model.CopilotIsAllowAll = copilotMetadata.IsAllowAll;
            }

            if (template.TryGet<ClaudeSessionMetadata>(out var anthropicMetadata))
            {
                model.ClaudeModel = anthropicMetadata.ClaudeModel;
                model.ClaudeEffortLevel = anthropicMetadata.EffortLevel;
            }
        }

        return model;
    }

    public void ApplyTo(AIProfileTemplate template)
    {
        template.Name = Name;
        template.DisplayText = DisplayText;
        template.Description = Description;
        template.Category = Category;
        template.IsListable = IsListable;
        template.Source = Source;

        if (Source == AITemplateSources.SystemPrompt)
        {
            template.Put(new SystemPromptTemplateMetadata
            {
                SystemMessage = SystemMessage,
            });
        }
        else if (Source == AITemplateSources.Profile)
        {
            var toolNames = SelectedToolNames?.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            var agentNames = SelectedAgentNames?.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();

            template.Put(new ProfileTemplateMetadata
            {
                ProfileType = ProfileType,
                SystemMessage = SystemMessage,
                ChatDeploymentName = ChatDeploymentName,
                UtilityDeploymentName = UtilityDeploymentName,
                OrchestratorName = OrchestratorName,
                TitleType = TitleType,
                Temperature = Temperature,
                TopP = TopP,
                FrequencyPenalty = FrequencyPenalty,
                PresencePenalty = PresencePenalty,
                MaxOutputTokens = MaxOutputTokens,
                PastMessagesCount = PastMessagesCount,
                WelcomeMessage = AddInitialPrompt ? null : WelcomeMessage,
                PromptTemplate = PromptTemplate,
                PromptSubject = PromptSubject,
                ToolNames = toolNames?.Length > 0 ? toolNames : [],
                AgentNames = agentNames?.Length > 0 ? agentNames : [],
                A2AConnectionIds = SelectedA2AConnectionIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ?? [],
            });

            template.Put(new AIProfileMcpMetadata
            {
                ConnectionIds = SelectedMcpConnectionIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ?? [],
            });

            template.Put(new AIProfileMetadata
            {
                UseCaching = UseCaching,
                InitialPrompt = AddInitialPrompt ? InitialPrompt?.Trim() : null,
            });

            template.Put(new DataSourceMetadata
            {
                DataSourceId = string.IsNullOrWhiteSpace(DataSourceId) ? null : DataSourceId.Trim(),
            });

            template.Put(new AIDataSourceRagMetadata
            {
                Strictness = DataSourceStrictness,
                TopNDocuments = DataSourceTopNDocuments,
                IsInScope = DataSourceIsInScope,
                Filter = string.IsNullOrWhiteSpace(DataSourceFilter) ? null : DataSourceFilter.Trim(),
            });

            template.Put(new PromptTemplateMetadata
            {
                Templates = (PromptTemplates ?? [])
                    .Where(t => !string.IsNullOrWhiteSpace(t.TemplateId))
                    .Select(t =>
                    {
                        var entry = new PromptTemplateSelectionEntry
                        {
                            TemplateId = t.TemplateId,
                        };

                        if (!string.IsNullOrWhiteSpace(t.PromptParameters))
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(t.PromptParameters);

                                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                                {
                                    entry.Parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                                    foreach (var prop in doc.RootElement.EnumerateObject())
                                    {
                                        if (prop.Value.ValueKind == JsonValueKind.String)
                                        {
                                            entry.Parameters[prop.Name] = prop.Value.GetString();
                                        }
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }

                        return entry;
                    })
                    .ToList(),
            });

            template.Put(new AIProfileSessionDocumentsMetadata
            {
                AllowSessionDocuments = AllowSessionDocuments,
            });

            template.Put(new DocumentsMetadata
            {
                DocumentTopN = DocumentTopN,
                RetrievalMode = DocumentRetrievalMode,
            });

            template.Put(new AIProfileDataExtractionSettings
            {
                EnableDataExtraction = EnableDataExtraction,
                ExtractionCheckInterval = ExtractionCheckInterval,
                SessionInactivityTimeoutInMinutes = SessionInactivityTimeoutInMinutes,
                DataExtractionEntries = (DataExtractionEntries ?? [])
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .Select(e => new DataExtractionEntry
                    {
                        Name = e.Name,
                        Description = e.Description,
                        AllowMultipleValues = e.AllowMultipleValues,
                        IsUpdatable = e.IsUpdatable,
                    })
                .ToList(),
            });

            template.Put(new AnalyticsMetadata
            {
                EnableSessionMetrics = EnableSessionMetrics,
                EnableAIResolutionDetection = EnableAIResolutionDetection,
                EnableConversionMetrics = EnableConversionMetrics,
                ConversionGoals = (ConversionGoals ?? [])
                    .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                    .Select(g => new ConversionGoal
                    {
                        Name = g.Name,
                        Description = g.Description,
                        MinScore = g.MinScore,
                        MaxScore = g.MaxScore > 0 ? g.MaxScore : 10,
                    })
                .ToList(),
            });

            template.Put(new AIProfilePostSessionSettings
            {
                EnablePostSessionProcessing = EnablePostSessionProcessing,
                PostSessionTasks = (PostSessionTasks ?? [])
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                    .Select(t => new PostSessionTask
                    {
                        Name = t.Name,
                        Type = t.Type,
                        Instructions = t.Instructions,
                        AllowMultipleValues = t.AllowMultipleValues,
                        Options = (t.Options ?? "")
                        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                        .Select(o => new PostSessionTaskOption { Value = o.Trim() })
                        .ToList(),
                        ToolNames = t.SelectedToolNames ?? [],
                        AgentNames = t.SelectedAgentNames ?? [],
                        A2AConnectionIds = t.SelectedA2AConnectionIds ?? [],
                        McpConnectionIds = t.SelectedMcpConnectionIds ?? [],
                    })
                .ToList(),
            });

            template.WithMemoryMetadata(new MemoryMetadata
            {
                EnableUserMemory = EnableUserMemory,
            });

            template.Put(new AIProfileSettings
            {
                LockSystemMessage = LockSystemMessage,
                IsListable = IsListable,
                IsRemovable = IsRemovable,
            });

            if (!string.IsNullOrEmpty(OrchestratorName) &&
                string.Equals(OrchestratorName, ClaudeOrchestrator.OrchestratorName, StringComparison.OrdinalIgnoreCase))
            {
                template.Put(new ClaudeSessionMetadata
                {
                    ClaudeModel = ClaudeModel,
                    EffortLevel = ClaudeEffortLevel,
                });
            }
            else
            {
                template.Remove<ClaudeSessionMetadata>();
            }

            if (!string.IsNullOrEmpty(OrchestratorName) &&
                string.Equals(OrchestratorName, CopilotOrchestrator.OrchestratorName, StringComparison.OrdinalIgnoreCase))
            {
                template.Put(new CopilotSessionMetadata
                {
                    CopilotModel = CopilotModel,
                    ReasoningEffort = CopilotReasoningEffort,
                    IsAllowAll = CopilotIsAllowAll,
                });
            }
            else
            {
                template.Remove<CopilotSessionMetadata>();
            }
        }
        else
        {
            template.Remove<ClaudeSessionMetadata>();
            template.Remove<CopilotSessionMetadata>();
        }
    }
}
