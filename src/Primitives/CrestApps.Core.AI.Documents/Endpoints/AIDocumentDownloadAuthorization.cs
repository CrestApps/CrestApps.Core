using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Decides whether the current user may read a stored document or anything stored under it, by the resource
/// that owns the document.
/// </summary>
/// <remarks>
/// The original file and the figures read out of it are served from different routes and the same rule
/// applies to both: whoever may manage the owning chat interaction, chat session or profile may read what was
/// stored for it. Keeping the rule in one place is what keeps the two routes from drifting apart.
/// </remarks>
internal static class AIDocumentDownloadAuthorization
{
    /// <summary>
    /// Authorizes a read of the supplied document.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="authorizationService">The authorization service.</param>
    /// <param name="interactionManager">The chat interaction manager.</param>
    /// <param name="sessionManager">The chat session manager.</param>
    /// <param name="profileManager">The profile manager.</param>
    /// <param name="document">The document being read.</param>
    /// <returns>
    /// <see langword="null"/> when the read is allowed, otherwise the result to return: not found when the
    /// owning resource is gone, a challenge for an anonymous caller, forbidden for a signed-in one.
    /// </returns>
    public static async Task<IResult> AuthorizeAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        ICatalogManager<ChatInteraction> interactionManager,
        IAIChatSessionManager sessionManager,
        IAIProfileManager profileManager,
        AIDocument document)
    {
        switch (document.ReferenceType)
        {
            case AIReferenceTypes.Document.ChatInteraction:
                {
                    var interaction = await interactionManager.FindByIdAsync(document.ReferenceId);

                    if (interaction is null)
                    {
                        return Results.NotFound();
                    }

                    var authorization = await authorizationService.AuthorizeAsync(
                        httpContext.User,
                        interaction,
                        [AIChatDocumentOperations.ManageDocuments]);

                    return authorization.Succeeded ? null : CreateUnauthorizedResult(httpContext);
                }
            case AIReferenceTypes.Document.ChatSession:
                {
                    var session = await sessionManager.FindAsync(document.ReferenceId);

                    if (session is null)
                    {
                        return Results.NotFound();
                    }

                    var profile = await profileManager.FindByIdAsync(session.ProfileId);

                    if (profile is null)
                    {
                        return Results.NotFound();
                    }

                    var authorization = await authorizationService.AuthorizeAsync(
                        httpContext.User,
                        new AIChatSessionDocumentAuthorizationContext(profile, session),
                        [AIChatDocumentOperations.ManageDocuments]);

                    return authorization.Succeeded ? null : CreateUnauthorizedResult(httpContext);
                }
            case AIReferenceTypes.Document.Profile:
                {
                    var profile = await profileManager.FindByIdAsync(document.ReferenceId);

                    if (profile is null)
                    {
                        return Results.NotFound();
                    }

                    var authorization = await authorizationService.AuthorizeAsync(
                        httpContext.User,
                        profile,
                        [AIChatDocumentOperations.ManageDocuments]);

                    return authorization.Succeeded ? null : CreateUnauthorizedResult(httpContext);
                }
            default:

                return Results.NotFound();
        }
    }

    private static IResult CreateUnauthorizedResult(HttpContext httpContext)
    {
        return httpContext.User.Identity?.IsAuthenticated == true
            ? Results.Forbid()
            : Results.Challenge();
    }
}
