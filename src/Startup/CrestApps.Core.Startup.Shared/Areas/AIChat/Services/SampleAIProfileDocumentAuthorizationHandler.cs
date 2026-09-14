using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace CrestApps.Core.Startup.Shared.Areas.AIChat.Services;

public sealed class SampleAIProfileDocumentAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, AIProfile>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        AIProfile resource)
    {
        if (resource != null &&
            requirement.Name == AIChatDocumentOperations.ManageDocuments.Name &&
            context.User.IsInRole("Administrator"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
