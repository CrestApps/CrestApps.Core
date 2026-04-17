using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace CrestApps.Core.AI.Documents;

public static class AIChatDocumentOperations
{
    public static OperationAuthorizationRequirement ManageDocuments { get; } = new()
    {
        Name = nameof(ManageDocuments),
    };
}
