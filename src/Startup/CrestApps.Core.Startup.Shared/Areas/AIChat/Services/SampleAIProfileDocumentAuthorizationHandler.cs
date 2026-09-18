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
/// read what it cites. Deciding who that is belongs to the deployment, and a sample cannot know it, so this
/// grants the requirement to administrators only — the same rule the chat-interaction document handler uses,
/// and the narrower of the two readings a sample could take. A real deployment replaces this with whatever
/// rule it uses to decide who may chat with a given profile, so that a user who cannot reach the profile
/// cannot reach its figures either.
/// <para>
/// The sample handlers do not all agree: the chat-session one grants to any authenticated user. Widening this
/// to match it would hand every signed-in user every profile's documents, which is not a default worth
/// shipping in something people copy.
/// </para>
/// </remarks>
public sealed class SampleAIProfileDocumentAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, AIProfile>
{
    /// <summary>
    /// Grants the requirement when the caller is an administrator.
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
            context.User.IsInRole("Administrator"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
