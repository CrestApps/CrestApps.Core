using System.Text.Json;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class AIProfileViewModel
{
    // Basic Info
    public string ItemId { get; set; }

    public string Name { get; set; }

    public string DisplayText { get; set; }

    public AIProfileType Type { get; set; }

    public string Source { get; set; }

    public string ChatDeploymentName { get; set; }

    public string UtilityDeploymentName { get; set; }

    public string OrchestratorName { get; set; }

    public string PromptTemplate { get; set; }

    public string PromptSubject { get; set; }

    public string Description { get; set; }

    public AISessionTitleType? TitleType { get; set; }

    // Chat-type interaction fields
    public string WelcomeMessage { get; set; }

    public bool AddInitialPrompt { get; set; }

    public string InitialPrompt { get; set; }

    public ChatMode ChatMode { get; set; }

    public string VoiceName { get; set; }

    public bool EnableTextToSpeechPlayback { get; set; }

    // AI Parameters (from AIProfileMetadata)
    public string SystemMessage { get; set; }

    public float? Temperature { get; set; }

    public float? TopP { get; set; }

    public float? FrequencyPenalty { get; set; }

    public float? PresencePenalty { get; set; }

    public int? MaxTokens { get; set; }

    public int? PastMessagesCount { get; set; }

    public bool UseCaching { get; set; } = true;

    // Settings (from AIProfileSettings)
    public bool LockSystemMessage { get; set; }

    public bool IsListable { get; set; } = true;

    public bool IsRemovable { get; set; } = true;

    // AI Tools
    public string[] SelectedToolNames { get; set; } = [];

    public List<ToolSelectionItem> AvailableTools { get; set; } = [];

    // AI Agents
    public string[] SelectedAgentNames { get; set; } = [];

    public List<AgentSelectionItem> AvailableAgents { get; set; } = [];

    // Data Source
    public string DataSourceId { get; set; }

    public int? DataSourceStrictness { get; set; }

    public int? DataSourceTopNDocuments { get; set; }

    public bool DataSourceIsInScope { get; set; }

    public string DataSourceFilter { get; set; }

    // A2A Connections
    public string[] SelectedA2AConnectionIds { get; set; } = [];

    public List<A2AConnectionSelectionItem> AvailableA2AConnections { get; set; } = [];

    // MCP Connections
    public string[] SelectedMcpConnectionIds { get; set; } = [];

    public List<McpConnectionSelectionItem> AvailableMcpConnections { get; set; } = [];

    // Prompt Templates
    public List<PromptTemplateSelectionItem> PromptTemplates { get; set; } = [];

    public List<PromptTemplateOptionItem> AvailablePromptTemplates { get; set; } = [];

    public List<Template> AvailableSystemPromptTemplates { get; set; } = [];

    // Documents
    public List<DocumentItem> AttachedDocuments { get; set; } = [];

    public int? DocumentTopN { get; set; }

    public DocumentRetrievalMode? DocumentRetrievalMode { get; set; }

    public bool AllowSessionDocuments { get; set; }

    public bool HasDocumentIndexConfiguration { get; set; }

    public string DocumentIndexProfileName { get; set; }

    // Data Extraction
    public bool EnableDataExtraction { get; set; }

    public int ExtractionCheckInterval { get; set; } = 1;

    public int SessionInactivityTimeoutInMinutes { get; set; } = 30;

    public List<DataExtractionEntryItem> DataExtractionEntries { get; set; } = [];

    // Session Metrics
    public bool EnableSessionMetrics { get; set; }

    public bool EnableAIResolutionDetection { get; set; } = true;

    public bool EnableConversionMetrics { get; set; }

    public List<ConversionGoalItem> ConversionGoals { get; set; } = [];

    // Post Session Processing
    public bool EnablePostSessionProcessing { get; set; }

    public List<PostSessionTaskItem> PostSessionTasks { get; set; } = [];

    // Template
    public string SelectedTemplateId { get; set; }

    // Memory
    public bool EnableUserMemory { get; set; }

    // Claude
    public string ClaudeModel { get; set; }

    public ClaudeEffortLevel ClaudeEffortLevel { get; set; }

    public bool ClaudeIsConfigured { get; set; }

    // Copilot
    public string CopilotModel { get; set; }

    public CopilotReasoningEffort CopilotReasoningEffort { get; set; }

    public bool CopilotIsAllowAll { get; set; }

    public bool CopilotIsConfigured { get; set; }

    public bool CopilotIsAuthenticated { get; set; }

    public string CopilotGitHubUsername { get; set; }

    public int CopilotAuthenticationType { get; set; }

    // Dropdown options (populated at load time)
    public List<KeyValuePair<string, string>> DataSources { get; set; } = [];

    public List<KeyValuePair<string, string>> Orchestrators { get; set; } = [];

    public List<KeyValuePair<string, string>> ChatDeployments { get; set; } = [];

    public List<KeyValuePair<string, string>> UtilityDeployments { get; set; } = [];

    public List<KeyValuePair<string, string>> Templates { get; set; } = [];

    public List<KeyValuePair<string, string>> AvailableProfileTemplates { get; set; } = [];

    public List<KeyValuePair<string, string>> CopilotAvailableModels { get; set; } = [];

    public List<KeyValuePair<string, string>> AnthropicAvailableModels { get; set; } = [];

    public static AIProfileViewModel FromProfile(AIProfile profile)
    {
        var settings = profile.GetOrCreateSettings<AIProfileSettings>();
        var dataExtractionSettings = profile.GetOrCreateSettings<AIProfileDataExtractionSettings>();
        var postSessionSettings = profile.GetOrCreateSettings<AIProfilePostSessionSettings>();
        var memoryMetadata = profile.GetOrCreate<MemoryMetadata>();
        profile.TryGetSettings<ChatModeProfileSettings>(out var chatModeSettings);

        var vm = new AIProfileViewModel
        {
            ItemId = profile.ItemId,
            Name = profile.Name,
            DisplayText = profile.DisplayText,
            Type = profile.Type,
            Source = profile.Source,
            ChatDeploymentName = profile.ChatDeploymentName,
            UtilityDeploymentName = profile.UtilityDeploymentName,
            OrchestratorName = profile.OrchestratorName,
            WelcomeMessage = profile.WelcomeMessage,
            PromptTemplate = profile.PromptTemplate,
            PromptSubject = profile.PromptSubject,
            Description = profile.Description,
            TitleType = profile.TitleType,

            ChatMode = chatModeSettings?.ChatMode ?? ChatMode.TextInput,
            VoiceName = chatModeSettings?.VoiceName,
            EnableTextToSpeechPlayback = chatModeSettings?.EnableTextToSpeechPlayback ?? false,

            LockSystemMessage = settings.LockSystemMessage,
            IsListable = settings.IsListable,
            IsRemovable = settings.IsRemovable,

            EnableDataExtraction = dataExtractionSettings.EnableDataExtraction,
            ExtractionCheckInterval = dataExtractionSettings.ExtractionCheckInterval,
            SessionInactivityTimeoutInMinutes = dataExtractionSettings.SessionInactivityTimeoutInMinutes,
            DataExtractionEntries = dataExtractionSettings.DataExtractionEntries
                .Select(e => new DataExtractionEntryItem
                {
                    Name = e.Name,
                    Description = e.Description,
                    AllowMultipleValues = e.AllowMultipleValues,
                    IsUpdatable = e.IsUpdatable,
                })
            .ToList(),

            EnablePostSessionProcessing = postSessionSettings.EnablePostSessionProcessing,
            PostSessionTasks = postSessionSettings.PostSessionTasks.Select(t => new PostSessionTaskItem
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
            }).ToList(),

            EnableUserMemory = memoryMetadata.EnableUserMemory ?? false,
        };

        if (profile.TryGet<AIProfileMetadata>(out var metadata))
        {
            vm.AddInitialPrompt = !string.IsNullOrEmpty(metadata.InitialPrompt);
            vm.InitialPrompt = metadata.InitialPrompt;
            vm.SystemMessage = metadata.SystemMessage;
            vm.Temperature = metadata.Temperature;
            vm.TopP = metadata.TopP;
            vm.FrequencyPenalty = metadata.FrequencyPenalty;
            vm.PresencePenalty = metadata.PresencePenalty;
            vm.MaxTokens = metadata.MaxTokens;
            vm.PastMessagesCount = metadata.PastMessagesCount;
            vm.UseCaching = metadata.UseCaching;
        }

        if (profile.TryGet<FunctionInvocationMetadata>(out var toolMetadata))
        {
            vm.SelectedToolNames = toolMetadata.Names ?? [];
        }

        if (profile.TryGet<AgentInvocationMetadata>(out var agentMetadata))
        {
            vm.SelectedAgentNames = agentMetadata.Names ?? [];
        }

        if (profile.TryGet<DataSourceMetadata>(out var dataSourceMetadata))
        {
            vm.DataSourceId = dataSourceMetadata.DataSourceId;
        }

        if (profile.TryGet<AIDataSourceRagMetadata>(out var dataSourceRagMetadata))
        {
            vm.DataSourceStrictness = dataSourceRagMetadata.Strictness;
            vm.DataSourceTopNDocuments = dataSourceRagMetadata.TopNDocuments;
            vm.DataSourceIsInScope = dataSourceRagMetadata.IsInScope;
            vm.DataSourceFilter = dataSourceRagMetadata.Filter;
        }

        if (profile.TryGet<AIProfileA2AMetadata>(out var a2aMetadata))
        {
            vm.SelectedA2AConnectionIds = a2aMetadata.ConnectionIds ?? [];
        }

        if (profile.TryGet<AIProfileMcpMetadata>(out var mcpMetadata))
        {
            vm.SelectedMcpConnectionIds = mcpMetadata.ConnectionIds ?? [];
        }

        if (profile.TryGet<PromptTemplateMetadata>(out var promptMetadata))
        {
            vm.PromptTemplates = (promptMetadata.Templates ?? [])
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

        if (profile.TryGet<DocumentsMetadata>(out var docMetadata))
        {
            vm.DocumentTopN = docMetadata.DocumentTopN;
            vm.DocumentRetrievalMode = docMetadata.RetrievalMode;
            vm.AttachedDocuments = (docMetadata.Documents ?? []).Select(d => new DocumentItem
            {
                DocumentId = d.DocumentId,
                FileName = d.FileName,
                ContentType = d.ContentType,
                FileSize = d.FileSize,
            }).ToList();
        }

        if (profile.TryGet<AIProfileSessionDocumentsMetadata>(out var sessionDocMetadata))
        {
            vm.AllowSessionDocuments = sessionDocMetadata.AllowSessionDocuments;
        }

        if (profile.TryGet<AnalyticsMetadata>(out var analyticsMetadata))
        {
            vm.EnableSessionMetrics = analyticsMetadata.EnableSessionMetrics;
            vm.EnableAIResolutionDetection = analyticsMetadata.EnableAIResolutionDetection;
            vm.EnableConversionMetrics = analyticsMetadata.EnableConversionMetrics;
            vm.ConversionGoals = analyticsMetadata.ConversionGoals
                .Select(g => new ConversionGoalItem
                {
                    Name = g.Name,
                    Description = g.Description,
                    MinScore = g.MinScore,
                    MaxScore = g.MaxScore,
                })
            .ToList();
        }

        if (profile.TryGet<CopilotSessionMetadata>(out var copilotMeta))
        {
            vm.CopilotModel = copilotMeta.CopilotModel;
            vm.CopilotReasoningEffort = copilotMeta.ReasoningEffort;
            vm.CopilotIsAllowAll = copilotMeta.IsAllowAll;
        }

        if (profile.TryGet<ClaudeSessionMetadata>(out var anthropicMeta))
        {
            vm.ClaudeModel = anthropicMeta.ClaudeModel;
            vm.ClaudeEffortLevel = anthropicMeta.EffortLevel;
        }

        return vm;
    }

    public void ApplyTo(AIProfile profile)
    {
        profile.Name = Name;
        profile.DisplayText = DisplayText;
        profile.Type = Type;
        profile.Source = Source;
        profile.ChatDeploymentName = ChatDeploymentName;
        profile.UtilityDeploymentName = UtilityDeploymentName;
        profile.OrchestratorName = OrchestratorName;
        profile.PromptTemplate = PromptTemplate;
        profile.PromptSubject = PromptSubject;
        profile.Description = Description;
        profile.TitleType = TitleType;

        profile.WelcomeMessage = AddInitialPrompt ? null : WelcomeMessage;

        profile.Alter<AIProfileMetadata>(m =>
        {
            m.SystemMessage = SystemMessage;
            m.InitialPrompt = AddInitialPrompt ? InitialPrompt?.Trim() : null;
            m.Temperature = Temperature;
            m.TopP = TopP;
            m.FrequencyPenalty = FrequencyPenalty;
            m.PresencePenalty = PresencePenalty;
            m.MaxTokens = MaxTokens;
            m.PastMessagesCount = PastMessagesCount;
            m.UseCaching = UseCaching;
        });

        profile.AlterSettings<AIProfileSettings>(s =>
        {
            s.LockSystemMessage = LockSystemMessage;
            s.IsListable = IsListable;
            s.IsRemovable = IsRemovable;
        });

        profile.AlterSettings<ChatModeProfileSettings>(settings =>
        {
            settings.ChatMode = ChatMode;
            settings.VoiceName = ChatMode == ChatMode.Conversation
                ? VoiceName?.Trim()
                : null;
            settings.EnableTextToSpeechPlayback = EnableTextToSpeechPlayback;
        });

        var toolNames = SelectedToolNames?.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();

        profile.Alter<FunctionInvocationMetadata>(x =>
        {
            x.Names = toolNames?.Length > 0 ? toolNames : null;
        });

        profile.Alter<AIProfileA2AMetadata>(a =>
        {
            a.ConnectionIds = SelectedA2AConnectionIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? [];
        });

        profile.Alter<AIProfileMcpMetadata>(x =>
        {
            x.ConnectionIds = SelectedMcpConnectionIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? [];
        });

        profile.Alter<AgentInvocationMetadata>(a =>
        {
            var agentNames = SelectedAgentNames?.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();

            a.Names = agentNames?.Length > 0 ? agentNames : [];
        });

        profile.Alter<DataSourceMetadata>(c =>
        {
            c.DataSourceId = DataSourceId;
        });

        profile.Alter<AIDataSourceRagMetadata>(x =>
        {
            x.Strictness = DataSourceStrictness;
            x.TopNDocuments = DataSourceTopNDocuments;
            x.IsInScope = DataSourceIsInScope;
            x.Filter = DataSourceFilter;
        });

        profile.Alter<PromptTemplateMetadata>(metadata =>
        {
            metadata.SetSelections(
                (PromptTemplates ?? [])
                    .Where(t => !string.IsNullOrWhiteSpace(t.TemplateId))
                    .Select(t =>
                    {
                        var entry = new PromptTemplateSelectionEntry { TemplateId = t.TemplateId };

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
                            catch { }
                        }

                        return entry;
                    }));
        });

        profile.Alter<DocumentsMetadata>(metadata =>
        {
            metadata.DocumentTopN = DocumentTopN;
            metadata.RetrievalMode = DocumentRetrievalMode;
        });

        profile.Alter<AIProfileSessionDocumentsMetadata>(metadata =>
        {
            metadata.AllowSessionDocuments = AllowSessionDocuments;
        });

        profile.AlterSettings<AIProfileDataExtractionSettings>(s =>
        {
            s.EnableDataExtraction = EnableDataExtraction;
            s.ExtractionCheckInterval = ExtractionCheckInterval;
            s.SessionInactivityTimeoutInMinutes = SessionInactivityTimeoutInMinutes;
            s.DataExtractionEntries = (DataExtractionEntries ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new DataExtractionEntry
            {
                Name = e.Name,
                Description = e.Description,
                AllowMultipleValues = e.AllowMultipleValues,
                IsUpdatable = e.IsUpdatable,
            })
        .ToList();
        });

        profile.Alter<AnalyticsMetadata>(metadata =>
        {
            metadata.EnableSessionMetrics = EnableSessionMetrics;
            metadata.EnableAIResolutionDetection = EnableAIResolutionDetection;
            metadata.EnableConversionMetrics = EnableConversionMetrics;
            metadata.ConversionGoals = (ConversionGoals ?? [])
                .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                .Select(g => new ConversionGoal
                {
                    Name = g.Name,
                    Description = g.Description,
                    MinScore = g.MinScore,
                    MaxScore = g.MaxScore > 0 ? g.MaxScore : 10,
                })
            .ToList();
        });

        profile.AlterSettings<AIProfilePostSessionSettings>(s =>
        {
            s.EnablePostSessionProcessing = EnablePostSessionProcessing;
            s.PostSessionTasks = PostSessionTasks
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
            }).ToList();
        });

        profile.Alter<MemoryMetadata>(m =>
        {
            m.EnableUserMemory = EnableUserMemory;
        });

        if (!string.IsNullOrEmpty(OrchestratorName) &&
            string.Equals(OrchestratorName, ClaudeOrchestrator.OrchestratorName, StringComparison.OrdinalIgnoreCase))
        {
            profile.Alter<ClaudeSessionMetadata>(metadata =>
            {
                metadata.ClaudeModel = ClaudeModel;
                metadata.EffortLevel = ClaudeEffortLevel;
            });
        }
        else
        {
            profile.Remove<ClaudeSessionMetadata>();
        }

        if (!string.IsNullOrEmpty(OrchestratorName) &&
            string.Equals(OrchestratorName, CopilotOrchestrator.OrchestratorName, StringComparison.OrdinalIgnoreCase))
        {
            profile.Alter<CopilotSessionMetadata>(metadata =>
            {
                metadata.CopilotModel = CopilotModel;
                metadata.ReasoningEffort = CopilotReasoningEffort;
                metadata.IsAllowAll = CopilotIsAllowAll;
            });
        }
        else
        {
            profile.Remove<CopilotSessionMetadata>();
        }
    }
}

public sealed class ToolSelectionItem
{
    public string Name { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string Category { get; set; }

    public bool IsSelected { get; set; }
}

public sealed class DocumentItem
{
    public string DocumentId { get; set; }

    public string FileName { get; set; }

    public string ContentType { get; set; }

    public long FileSize { get; set; }
}

public sealed class PostSessionTaskItem
{
    public string Name { get; set; }

    public PostSessionTaskType Type { get; set; }

    public string Instructions { get; set; }

    public bool AllowMultipleValues { get; set; }

    public string Options { get; set; }

    public string[] SelectedToolNames { get; set; } = [];

    public string[] SelectedAgentNames { get; set; } = [];

    public string[] SelectedA2AConnectionIds { get; set; } = [];

    public string[] SelectedMcpConnectionIds { get; set; } = [];
}

public sealed class PromptTemplateSelectionItem
{
    public string TemplateId { get; set; }

    public string PromptParameters { get; set; }
}

public sealed class PromptTemplateOptionItem
{
    public string TemplateId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string Category { get; set; }

    public List<PromptTemplateParameterItem> Parameters { get; set; } = [];
}

public sealed class PromptTemplateParameterItem
{
    public string Name { get; set; }

    public string Description { get; set; }
}

public sealed class DataExtractionEntryItem
{
    public string Name { get; set; }

    public string Description { get; set; }

    public bool AllowMultipleValues { get; set; }

    public bool IsUpdatable { get; set; }
}

public sealed class ConversionGoalItem
{
    public string Name { get; set; }

    public string Description { get; set; }

    public int MinScore { get; set; }

    public int MaxScore { get; set; } = 10;
}

public sealed class AgentSelectionItem
{
    public string Name { get; set; }

    public string DisplayText { get; set; }

    public string Description { get; set; }

    public bool IsSelected { get; set; }
}

public sealed class A2AConnectionSelectionItem
{
    public string ItemId { get; set; }

    public string DisplayText { get; set; }

    public string Endpoint { get; set; }

    public bool IsSelected { get; set; }
}

public sealed class McpConnectionSelectionItem
{
    public string ItemId { get; set; }

    public string DisplayText { get; set; }

    public string Source { get; set; }

    public bool IsSelected { get; set; }
}
