using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.Templates.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class ChatInteractionViewModel
{
    public string Title { get; set; }

    public string ChatDeploymentName { get; set; }

    public string OrchestratorName { get; set; }

    public string SystemMessage { get; set; }

    public float? Temperature { get; set; }

    public float? TopP { get; set; }

    public float? FrequencyPenalty { get; set; }

    public float? PresencePenalty { get; set; }

    public int? MaxTokens { get; set; }

    public int? PastMessagesCount { get; set; }

    // A2A Connections
    public string[] SelectedA2AConnectionIds { get; set; } = [];
    public List<A2AConnectionSelectionItem> AvailableA2AConnections { get; set; } = [];

    // MCP Connections
    public string[] SelectedMcpConnectionIds { get; set; } = [];
    public List<McpConnectionSelectionItem> AvailableMcpConnections { get; set; } = [];

    // AI Tools
    public string[] SelectedToolNames { get; set; } = [];
    public List<ToolSelectionItem> AvailableTools { get; set; } = [];

    // AI Agents
    public string[] SelectedAgentNames { get; set; } = [];
    public List<AgentSelectionItem> AvailableAgents { get; set; } = [];

    // Prompt Templates
    public List<PromptTemplateSelectionItem> PromptTemplates { get; set; } = [];
    public List<PromptTemplateOptionItem> AvailablePromptTemplates { get; set; } = [];

    public List<Template> AvailableSystemPromptTemplates { get; set; } = [];

    public bool HasDocumentIndexConfiguration { get; set; }

    public string DocumentIndexProfileName { get; set; }

    public DocumentRetrievalMode? DocumentRetrievalMode { get; set; }

    // Data Sources
    public string DataSourceId { get; set; }
    public int? DataSourceStrictness { get; set; }
    public int? DataSourceTopNDocuments { get; set; }
    public bool DataSourceIsInScope { get; set; }
    public string DataSourceFilter { get; set; }

    // Copilot
    public string CopilotModel { get; set; }
    public CrestApps.Core.AI.Copilot.Models.CopilotReasoningEffort CopilotReasoningEffort { get; set; }
    public bool CopilotIsAllowAll { get; set; }
    public bool CopilotIsConfigured { get; set; }
    public bool CopilotIsAuthenticated { get; set; }
    public string CopilotGitHubUsername { get; set; }
    public int CopilotAuthenticationType { get; set; }

    // Anthropic
    public string ClaudeModel { get; set; }
    public CrestApps.Core.AI.Claude.Models.ClaudeEffortLevel ClaudeEffortLevel { get; set; }
    public bool ClaudeIsConfigured { get; set; }

    public List<SelectOption> DataSources { get; set; } = [];
    public List<SelectOption> Deployments { get; set; } = [];
    public List<SelectOption> Orchestrators { get; set; } = [];
    public List<SelectOption> CopilotAvailableModels { get; set; } = [];
    public List<SelectOption> AnthropicAvailableModels { get; set; } = [];
}

public sealed class SelectOption
{
    public string Text { get; set; }

    public string Value { get; set; }

    public bool Selected { get; set; }

    public SelectOption()
    {
    }

    public SelectOption(
        string text,
        string value,
        bool selected = false)
    {
        Text = text;
        Value = value;
        Selected = selected;
    }
}
