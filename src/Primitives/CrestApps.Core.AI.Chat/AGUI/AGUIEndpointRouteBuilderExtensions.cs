using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using AGUI.Abstractions;
using AGUI.Formatting;
using CrestApps.Core.AI.AGUI;
using CrestApps.Core.AI.Chat.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Chat.AGUI;

/// <summary>
/// Maps the AG-UI HTTP endpoint of the AI chat: a <c>POST</c> of an AG-UI <c>RunAgentInput</c> answered with the
/// run's events as Server-Sent Events, so any AG-UI client can chat with an AI profile.
/// </summary>
public static class AGUIEndpointRouteBuilderExtensions
{
    /// <summary>
    /// The default route of the endpoint.
    /// </summary>
    public const string DefaultPattern = "api/ai/agui";

    /// <summary>
    /// Maps the AG-UI endpoint on the given chat hub. The run goes through the hub's own pipeline, so the hub's
    /// profile and session authorization apply. Add authorization and rate limiting to the returned builder as the
    /// host needs.
    /// </summary>
    /// <typeparam name="THub">The chat hub of the host.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route of the endpoint.</param>
    /// <returns>The endpoint builder.</returns>
    public static IEndpointConventionBuilder MapAGUIChat<THub>(this IEndpointRouteBuilder endpoints, string pattern = DefaultPattern)
        where THub : AIChatHubCore<IAIChatHubClient>
    {
        return endpoints.MapAGUIChat<THub, IAIChatHubClient>(pattern);
    }

    /// <summary>
    /// Maps the AG-UI endpoint on the given chat hub, whose client contract extends <see cref="IAIChatHubClient"/>.
    /// The run goes through the hub's own pipeline, so the hub's profile and session authorization apply. Add
    /// authorization and rate limiting to the returned builder as the host needs.
    /// </summary>
    /// <typeparam name="THub">The chat hub of the host.</typeparam>
    /// <typeparam name="TClient">The hub's client contract.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route of the endpoint.</param>
    /// <returns>The endpoint builder.</returns>
    public static IEndpointConventionBuilder MapAGUIChat<THub, TClient>(this IEndpointRouteBuilder endpoints, string pattern = DefaultPattern)
        where THub : AIChatHubCore<TClient>
        where TClient : class, IAIChatHubClient
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        return endpoints.MapPost(pattern, (HttpContext httpContext) => RunAsync<THub, TClient>(httpContext))
            .DisableAntiforgery();
    }

    private static async Task RunAsync<THub, TClient>(HttpContext httpContext)
        where THub : AIChatHubCore<TClient>
        where TClient : class, IAIChatHubClient
    {
        // Only JSON is read. A cross-site page cannot send JSON without the browser asking this site first, so the
        // endpoint needs no antiforgery token.
        if (!httpContext.Request.HasJsonContentType())
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;

            return;
        }

        RunAgentInput input;

        try
        {
            input = (RunAgentInput)await JsonSerializer.DeserializeAsync(
                httpContext.Request.Body,
                AGUIJson.SerializerOptions.GetTypeInfo(typeof(RunAgentInput)),
                httpContext.RequestAborted);
        }
        catch (JsonException)
        {
            input = null;
        }

        if (input is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        var cancellationToken = httpContext.RequestAborted;

        httpContext.Response.ContentType = SseEventStreamFormatter.ServerSentEventsMediaType;
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var events = Channel.CreateUnbounded<BaseEvent>(new UnboundedChannelOptions { SingleReader = true });
        var formatter = new SseEventStreamFormatter();
        var writing = formatter.WriteAsync(events.Reader.ReadAllAsync(cancellationToken), httpContext.Response.Body, cancellationToken);

        var hub = ActivatorUtilities.CreateInstance<THub>(httpContext.RequestServices);

        hub.Context = new AGUIHttpHubCallerContext(httpContext, $"agui-{input.RunId ?? Guid.NewGuid().ToString("n")}");
        hub.Groups = AGUINoGroupManager.Instance;

        try
        {
            await hub.RunAgentWithoutConnectionAsync(
                input,
                httpContext.RequestServices,
                (evt, token) => events.Writer.WriteAsync(evt, token),
                cancellationToken);
        }
        finally
        {
            events.Writer.TryComplete();

            await writing;
        }
    }

    /// <summary>
    /// The caller of an AG-UI run over HTTP, as the hub sees it.
    /// </summary>
    private sealed class AGUIHttpHubCallerContext : HubCallerContext
    {
        private readonly HttpContext _httpContext;

        public AGUIHttpHubCallerContext(HttpContext httpContext, string connectionId)
        {
            _httpContext = httpContext;
            ConnectionId = connectionId;
        }

        public override string ConnectionId { get; }

        public override string UserIdentifier => _httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        public override ClaimsPrincipal User => _httpContext.User;

        public override IDictionary<object, object> Items { get; } = new Dictionary<object, object>();

        public override IFeatureCollection Features => _httpContext.Features;

        public override CancellationToken ConnectionAborted => _httpContext.RequestAborted;

        public override void Abort()
        {
            _httpContext.Abort();
        }
    }

    /// <summary>
    /// The groups of an AG-UI run over HTTP: there are none, since nothing can be pushed to the caller later.
    /// </summary>
    private sealed class AGUINoGroupManager : IGroupManager
    {
        public static AGUINoGroupManager Instance { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
