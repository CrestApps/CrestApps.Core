using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Carries the resolved AI settings that govern a single completion request,
/// including model parameters, tool configuration, and deployment routing.
/// </summary>
public sealed class AICompletionContext
{
    /// <summary>
    /// Gets or sets a value indicating whether all registered tools should be suppressed for this request.
    /// </summary>
    public bool DisableTools { get; set; }

    /// <summary>
    /// Gets or sets the system message prepended to the conversation.
    /// </summary>
    public string SystemMessage { get; set; }

    /// <summary>
    /// Gets or sets the sampling temperature controlling output randomness (0.0-2.0).
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// Gets or sets the nucleus-sampling probability mass (0.0-1.0).
    /// </summary>
    public float? TopP { get; set; }

    /// <summary>
    /// Gets or sets the frequency penalty that reduces repetition of already-seen tokens (0.0-2.0).
    /// </summary>
    public float? FrequencyPenalty { get; set; }

    /// <summary>
    /// Gets or sets the presence penalty that encourages the model to discuss new topics (0.0-2.0).
    /// </summary>
    public float? PresencePenalty { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of tokens to generate in the completion.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Gets or sets the number of past conversation messages included as context.
    /// </summary>
    public int? PastMessagesCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether response caching is enabled for this request.
    /// </summary>
    public bool UseCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets the names of the tools available for this completion request.
    /// </summary>
    public string[] ToolNames { get; set; }

    /// <summary>
    /// Gets or sets the names of the agent profiles that can be invoked during this completion.
    /// </summary>
    public string[] AgentNames { get; set; }

    /// <summary>
    /// Gets or sets the MCP (Model Context Protocol) connection identifiers available to this request.
    /// </summary>
    public string[] McpConnectionIds { get; set; }

    /// <summary>
    /// Gets or sets the Agent-to-Agent (A2A) connection identifiers available to this request.
    /// </summary>
    public string[] A2AConnectionIds { get; set; }

    /// <summary>
    /// Gets or sets the data source identifier used for retrieval-augmented generation.
    /// </summary>
    public string DataSourceId { get; set; }

    /// <summary>
    /// Gets or sets the deployment name used to resolve the chat model.
    /// </summary>
    public string ChatDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the deployment name used to resolve the utility model (e.g., for summarization).
    /// </summary>
    public string UtilityDeploymentName { get; set; }

    [JsonInclude]
    [JsonPropertyName("DeploymentId")]
    private string _deploymentIdBackingField
    {
        set => ChatDeploymentName = value;
    }

    [JsonInclude]
    [JsonPropertyName("ChatDeploymentId")]
    private string _chatDeploymentIdBackingField
    {
        set => ChatDeploymentName = value;
    }

    [JsonInclude]
    [JsonPropertyName("UtilityDeploymentId")]
    private string _utilityDeploymentIdBackingField
    {
        set => UtilityDeploymentName = value;
    }

    /// <summary>
    /// Gets the additional provider-specific properties applied to the completion request.
    /// </summary>
    public Dictionary<string, object> AdditionalProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
}
