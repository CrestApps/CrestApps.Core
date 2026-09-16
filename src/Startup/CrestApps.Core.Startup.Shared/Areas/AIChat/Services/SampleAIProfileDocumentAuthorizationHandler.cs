using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace CrestApps.Core.Startup.Shared.Areas.AIChat.Services;

/// <summary>
/// Sample policy for reading a document, or a figure read out of it, that is attached to an AI profile.
/// </summary>
/// <remarks>
/// A profile's attached documents are part of what the profile is for, so whoever may use the profile may
/// read what it cites. This sample grants that to any authenticated user, matching the other sample document
/// handlers. A real deployment replaces this with the same rule it uses to decide who may chat with a given
/// profile, so a user who cannot reach the profile cannot reach its figures either.
/// </remarks>
public sealed class SampleAIProfileDocumentAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, AIProfile>
{
    /// <summary>
    /// Grants the requirement when the caller is authenticated.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <param name="requirement">The requirement being evaluated.</param>
    /// <param name="resource">The profile that owns the document.</param>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        AIProfile resource)
    {
        if (resource != null &&
            requirement.Name == AIChatDocumentOperations.ManageDocuments.Name &&
            context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
