using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Provides functionality for AI Chat Document Operations.
/// </summary>
public static class AIChatDocumentOperations
{
    /// <summary>
    /// Gets the manage Documents.
    /// </summary>
    public static OperationAuthorizationRequirement ManageDocuments { get; } = new()
    {
        Name = nameof(ManageDocuments),
    };
}
