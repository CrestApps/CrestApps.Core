using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Provides well-known keys for <see cref="Models.AICompletionContext.AdditionalProperties"/>.
/// These keys are used by orchestration handlers to communicate context availability
/// to downstream consumers such as <see cref="IToolRegistryProvider"/> implementations.
/// </summary>
public static class AICompletionContextKeys
{
    public const string CompletionContext = "CompletionContext";

    public const string Session = "Session";

    public const string Interaction = "Interaction";

    public const string InteractionId = "InteractionId";

    public const string ClientName = "ClientName";

    /// <summary>
    /// Holds the SignalR connection identifier (a <see cref="string"/>) of the chat client that sent the message being
    /// answered. Tool registry providers use it to reach that browser tab, for example to run the WebMCP page tools it
    /// registered.
    /// </summary>
    public const string ChatConnectionId = "ChatConnectionId";

    /// <summary>
    /// Holds the <see cref="Models.ResolvedAIPersona"/> resolved for the request when the profile or interaction links to
    /// one, so downstream consumers can read its name, avatar or voice without resolving it again.
    /// </summary>
    public const string Persona = "Persona";

    /// <summary>
    /// When set to a string in a request's options additional properties, records that value as the usage
    /// category (<see cref="Models.AICompletionUsageRecord.ContextType"/>) for that one request, taking precedence
    /// over <see cref="AIUsageScope"/>.
    /// </summary>
    public const string UsageContextType = "UsageContextType";

    /// <summary>
    /// When set to a string in a request's options additional properties, records that value as the usage
    /// purpose (<see cref="Models.AICompletionUsageRecord.Purpose"/>) for that one request, taking precedence over
    /// <see cref="AIUsageScope"/>.
    /// </summary>
    public const string UsagePurpose = "UsagePurpose";

    /// <summary>
    /// When set to a string in a request's additional properties, records that value as the usage purpose only
    /// when neither the request (<see cref="UsagePurpose"/>) nor the current <see cref="AIUsageScope"/> names one.
    /// The framework uses it to label general-purpose requests, such as a conversation reply, without overriding a
    /// purpose the caller chose.
    /// </summary>
    public const string DefaultUsagePurpose = "DefaultUsagePurpose";

    /// <summary>
    /// When set to <see langword="true"/> in <see cref="Models.AICompletionContext.AdditionalProperties"/>,
    /// indicates that documents are available for the current session. This enables
    /// document processing system tools (e.g., <c>search_documents</c>, <c>list_documents</c>)
    /// to be included in the tool registry.
    /// </summary>
    public const string HasDocuments = "HasDocuments";

    /// <summary>
    /// When set to <see langword="true"/> in <see cref="Models.AICompletionContext.AdditionalProperties"/>,
    /// indicates that authenticated user memory is available for the current request. This enables
    /// memory-related system tools to be included in the tool registry.
    /// </summary>
    public const string HasMemory = "HasMemory";

    /// <summary>
    /// The key in <see cref="AICompletionContext.AdditionalProperties"/> whose <see langword="true"/> value
    /// indicates that the current conversation has older messages the conversation-memory tools can search.
    /// </summary>
    public const string HasConversationMemory = "HasConversationMemory";

    /// <summary>
    /// The key in <see cref="AICompletionContext.AdditionalProperties"/> whose <see langword="true"/> value
    /// indicates that the past-conversation tools are available for the signed-in user.
    /// </summary>
    public const string HasPastConversations = "HasPastConversations";

    /// <summary>
    /// Stores tool names that were included to satisfy declared tool dependencies for the current request.
    /// Orchestrators can use this to keep required helper tools available during tool scoping.
    /// </summary>
    public const string DependencyToolNames = "DependencyToolNames";

    /// <summary>
    /// The language the conversation is currently in (a <see cref="string"/> BCP-47 tag). A host sets it from a
    /// conversation variable or a detected caller language; it overrides the profile's starting language.
    /// </summary>
    public const string ConversationLanguage = "ConversationLanguage";

    /// <summary>
    /// The key whose <see langword="true"/> value tells the tool pipeline that the caller can ask a person to
    /// approve a tool call, such as an AG-UI chat run. Tools that require an approval then pause the run for one
    /// instead of running at once.
    /// </summary>
    public const string ToolApprovalSupported = "ToolApprovalSupported";

    /// <summary>
    /// The key of the context the client sent with the message (an <see cref="IReadOnlyList{T}"/> of
    /// <see cref="Models.ChatClientContextEntry"/>), such as the page the user is on. It reaches the model as data,
    /// never as instructions.
    /// </summary>
    public const string ClientContext = "ClientContext";

    /// <summary>
    /// The key of the state the user and the agent share during a chat run (a
    /// <see cref="Models.ChatSharedState"/>). The agent changes it with the shared state tool, and the client is
    /// told about each change.
    /// </summary>
    public const string SharedState = "SharedState";

    /// <summary>
    /// The key of the tools the client runs itself (an <see cref="IReadOnlyList{T}"/> of
    /// <see cref="Microsoft.Extensions.AI.AITool"/> declarations), such as the frontend tools of an AG-UI client. The
    /// model may call them; a call ends the request, and the client sends the result with its next request.
    /// </summary>
    public const string ClientTools = "ClientTools";
}
