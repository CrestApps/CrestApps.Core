namespace CrestApps.Core.AI.Services;

internal static class PromptSecurityRuleCategories
{
    public const string InstructionOverride = "instruction-override";
    public const string RoleConfusion = "role-confusion";
    public const string PrivilegeEscalation = "privilege-escalation";
    public const string PromptLeakage = "prompt-leakage";
    public const string ContextExtraction = "context-extraction";
    public const string HistoryExtraction = "history-extraction";
    public const string MemoryExtraction = "memory-extraction";
    public const string ConfigurationDisclosure = "configuration-disclosure";
    public const string ToolDiscovery = "tool-discovery";
    public const string AgentDiscovery = "agent-discovery";
    public const string FunctionSchemaDiscovery = "function-schema-discovery";
    public const string DataExfiltration = "data-exfiltration";
    public const string EncodedExfiltration = "encoded-exfiltration";
    public const string DelimiterManipulation = "delimiter-manipulation";
    public const string RagInjection = "rag-injection";
    public const string AuthorityImpersonation = "authority-impersonation";
    public const string HarmfulContentGeneration = "harmful-content-generation";
    public const string SensitiveDataExposure = "sensitive-data-exposure";
}
