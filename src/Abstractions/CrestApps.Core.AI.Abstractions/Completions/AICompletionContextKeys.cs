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
    /// Stores tool names that were included to satisfy declared tool dependencies for the current request.
    /// Orchestrators can use this to keep required helper tools available during tool scoping.
    /// </summary>
    public const string DependencyToolNames = "DependencyToolNames";

    /// <summary>
    /// The language the conversation is currently in (a <see cref="string"/> BCP-47 tag). A host sets it from a
    /// conversation variable or a detected caller language; it overrides the profile's starting language.
    /// </summary>
    public const string ConversationLanguage = "ConversationLanguage";
}
