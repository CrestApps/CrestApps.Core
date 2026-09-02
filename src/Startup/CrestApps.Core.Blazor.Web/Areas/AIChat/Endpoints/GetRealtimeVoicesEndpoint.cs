using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Speech;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Endpoints;

internal static class GetRealtimeVoicesEndpoint
{
    public static IEndpointRouteBuilder AddGetRealtimeVoicesEndpoint(this IEndpointRouteBuilder builder)
    {
        _ = builder.MapGet("api/chat/realtime-voices", HandleAsync)
            .RequireAuthorization();

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string deploymentName,
        IAIDeploymentStore deploymentStore,
        IRealtimeVoiceResolver realtimeVoiceResolver)
    {
        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            return TypedResults.Ok(new { voices = Array.Empty<object>() });
        }

        var deployment = await deploymentStore.FindByNameAsync(deploymentName);

        if (deployment is null)
        {
            return TypedResults.Ok(new { voices = Array.Empty<object>() });
        }

        var voices = (await realtimeVoiceResolver.GetVoicesAsync(deployment))
            .OrderBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)
            .Select(voice => new
            {
                id = voice.Id,
                name = voice.Name,
                gender = voice.Gender.ToString(),
            });

        return TypedResults.Ok(new { voices });
    }
}
