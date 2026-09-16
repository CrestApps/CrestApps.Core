using System.Security.Claims;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace CrestApps.Core.Startup.Shared.Areas.AIChat.Services;

/// <summary>
/// Lets the data source's owner, or a user in the Administrator role, download a figure cited in an answer.
/// </summary>
/// <remarks>
/// This is a SAMPLE policy, sized for the sample sites where a single administrator account creates every data
/// source, and it is not a model for a real deployment. A real host decides who may see a data source from its
/// own authorization model - tenant or group membership, a sharing list, or whatever permission already guards
/// the data source itself - and registers its own handler for
/// <see cref="AIKnowledgeOperations.ViewFigures"/> in place of this one. Ownership alone is rarely the whole
/// answer, because a figure is normally readable by everyone allowed to query the data source rather than only
/// by whoever created it.
/// </remarks>
public sealed class SampleKnowledgeFigureAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, AIDataSource>
{
    private const string AdministratorRole = "Administrator";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        AIDataSource resource)
    {
        if (resource != null &&
            requirement.Name == AIKnowledgeOperations.ViewFigures.Name &&
            context.User.Identity?.IsAuthenticated == true &&
            (context.User.IsInRole(AdministratorRole) || IsOwner(context.User, resource)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool IsOwner(ClaimsPrincipal user, AIDataSource resource)
    {
        // The catalog handler stamps OwnerId with the name identifier claim and Author with the user name, so
        // both are checked. A data source that recorded neither belongs to nobody rather than to everybody.
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrEmpty(userId) &&
            string.Equals(resource.OwnerId, userId, StringComparison.Ordinal))
        {
            return true;
        }

        var userName = user.Identity?.Name;

        return !string.IsNullOrEmpty(userName) &&
            string.Equals(resource.Author, userName, StringComparison.Ordinal);
    }
}
