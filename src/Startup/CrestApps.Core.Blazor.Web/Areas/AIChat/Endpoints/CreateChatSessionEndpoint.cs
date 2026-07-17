using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Endpoints;

internal static class CreateChatSessionEndpoint
{
    public static IEndpointRouteBuilder AddCreateChatSessionEndpoint(this IEndpointRouteBuilder builder)
    {
        var endpointRateLimitingOptions = builder.ServiceProvider.GetRequiredService<IOptions<AIChatEndpointRateLimitingOptions>>().Value;

        _ = builder.MapPost("api/chat/create-session", HandleAsync)
            .AddEndpointFilter<StoreCommitterEndpointFilter>()
            .RequireRateLimiting(endpointRateLimitingOptions.AnonymousSessionStartPolicyName)
            .RequireAuthorization()
            .DisableAntiforgery();

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] CreateChatSessionRequest request,
        IAIProfileManager profileManager,
        IAIChatSessionManager sessionManager)
    {
        if (string.IsNullOrWhiteSpace(request?.ProfileId))
        {
            return TypedResults.BadRequest(new { error = "ProfileId is required." });
        }

        var profile = await profileManager.FindByIdAsync(request.ProfileId);

        if (profile == null)
        {
            return TypedResults.NotFound(new { error = "Profile not found." });
        }

        var session = await sessionManager.NewAsync(profile, new NewAIChatSessionContext());
        await sessionManager.SaveAsync(session);

        return TypedResults.Ok(new { sessionId = session.SessionId, profileId = profile.ItemId });
    }

    private sealed class CreateChatSessionRequest
    {
        public string ProfileId { get; set; }
    }
}
