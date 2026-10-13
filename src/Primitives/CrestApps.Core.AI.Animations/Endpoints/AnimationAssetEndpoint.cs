using CrestApps.Core.AI.Animations.Storage;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.AI.Animations.Endpoints;

/// <summary>
/// Serves a stored animation's video and poster to the chat that shows them.
/// </summary>
/// <remarks>
/// Authorized with the same owner rule as a document download: whoever may manage the chat interaction or chat
/// session the animation was made in may watch it. The video is served with range support, so the player can
/// seek and start before the whole file has arrived.
/// </remarks>
public static class AnimationAssetEndpoint
{
    public const string DefaultRouteName = "AnimationAsset";

    /// <summary>
    /// Adds the endpoint that serves animation videos and posters.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="routeName">The route name the animation tool builds its links with.</param>
    public static IEndpointRouteBuilder AddAnimationAssetEndpoint(this IEndpointRouteBuilder builder, string routeName = DefaultRouteName)
    {
        var endpoint = builder.MapGet("ai/animations/{referenceType}/{referenceId}/{animationId}/{asset}", HandleAsync);

        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string referenceType,
        string referenceId,
        string animationId,
        string asset,
        HttpContext httpContext,
        [FromServices] IAnimationStore animationStore,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] ICatalogManager<ChatInteraction> interactionManager,
        [FromServices] IAIChatSessionManager sessionManager,
        [FromServices] IAIProfileManager profileManager)
    {
        if (!Enum.TryParse<AnimationAsset>(asset, ignoreCase: true, out var animationAsset) || !Enum.IsDefined(animationAsset))
        {
            return Results.NotFound();
        }

        var authorizationResult = await AIDocumentDownloadAuthorization.AuthorizeOwnerAsync(
            httpContext,
            authorizationService,
            interactionManager,
            sessionManager,
            profileManager,
            referenceType,
            referenceId);

        if (authorizationResult is not null)
        {
            return authorizationResult;
        }

        var stream = await animationStore.OpenReadAsync(new AnimationOwner(referenceType, referenceId), animationId, animationAsset, httpContext.RequestAborted);

        if (stream is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "private, max-age=3600";

        var contentType = animationAsset == AnimationAsset.Poster
            ? "image/png"
            : "video/mp4";

        return Results.File(stream, contentType, enableRangeProcessing: true);
    }
}
