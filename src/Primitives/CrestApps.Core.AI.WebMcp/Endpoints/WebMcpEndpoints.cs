using System.Text.Json;
using CrestApps.Core.AI.WebMcp.Models;
using CrestApps.Core.AI.WebMcp.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.AI.WebMcp.Endpoints;

/// <summary>
/// Maps the endpoints the WebMCP page script calls to list and call the AI tools a user may give to browser agents.
/// </summary>
public static class WebMcpEndpoints
{
    /// <summary>
    /// The largest tool input, in bytes, the invoke endpoint reads.
    /// </summary>
    public const long MaxRequestBodyLength = 256 * 1024;

    /// <summary>
    /// Maps <c>GET {prefix}/tools</c>, which lists the tools the signed-in user may call, and
    /// <c>POST {prefix}/tools/{name}/invoke</c>, which calls one with the JSON object in the body. Both need a user
    /// <see cref="IWebMcpAccessEvaluator"/> allows; the call also needs a valid antiforgery token in the request header.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="prefix">The route prefix. Defaults to <see cref="WebMcpConstants.DefaultRoutePrefix"/>.</param>
    /// <returns>The route group, so the host can add its own conventions such as rate limiting.</returns>
    public static RouteGroupBuilder MapWebMcpTools(this IEndpointRouteBuilder endpoints, string prefix = WebMcpConstants.DefaultRoutePrefix)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(string.IsNullOrWhiteSpace(prefix) ? WebMcpConstants.DefaultRoutePrefix : prefix);

        // The invoke endpoint validates the antiforgery token itself, from the header the page script sends, because
        // the framework's automatic check only reads form posts.
        group.MapGet("tools", GetToolsAsync)
            .DisableAntiforgery();

        group.MapPost("tools/{name}/invoke", InvokeAsync)
            .DisableAntiforgery();

        return group;
    }

    /// <summary>
    /// Lists the tools the signed-in user may call.
    /// </summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <param name="accessEvaluator">The evaluator that decides whether the user may use WebMCP tools.</param>
    /// <param name="toolService">The WebMCP tool service.</param>
    /// <returns>The tools, or the reason they are refused.</returns>
    internal static async Task<IResult> GetToolsAsync(
        HttpContext httpContext,
        [FromServices] IWebMcpAccessEvaluator accessEvaluator,
        [FromServices] IWebMcpToolService toolService)
    {
        var user = httpContext.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return TypedResults.Unauthorized();
        }

        if (!await accessEvaluator.CanUseAsync(user, httpContext.RequestAborted))
        {
            return TypedResults.Forbid();
        }

        var tools = await toolService.GetToolsAsync(user, httpContext.RequestAborted);

        return TypedResults.Ok(new WebMcpToolListResponse
        {
            Tools = tools,
        });
    }

    /// <summary>
    /// Calls one tool for the signed-in user.
    /// </summary>
    /// <param name="name">The function name of the tool.</param>
    /// <param name="httpContext">The HTTP context.</param>
    /// <param name="accessEvaluator">The evaluator that decides whether the user may use WebMCP tools.</param>
    /// <param name="antiforgery">The antiforgery service.</param>
    /// <param name="toolService">The WebMCP tool service.</param>
    /// <returns>The tool's answer as an MCP tool result, or the reason the call is refused.</returns>
    internal static async Task<IResult> InvokeAsync(
        [FromRoute] string name,
        HttpContext httpContext,
        [FromServices] IWebMcpAccessEvaluator accessEvaluator,
        [FromServices] IAntiforgery antiforgery,
        [FromServices] IWebMcpToolService toolService)
    {
        var user = httpContext.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return TypedResults.Unauthorized();
        }

        if (!await accessEvaluator.CanUseAsync(user, httpContext.RequestAborted))
        {
            return TypedResults.Forbid();
        }

        if (!await antiforgery.IsRequestValidAsync(httpContext))
        {
            return TypedResults.BadRequest("The antiforgery token is missing or not valid.");
        }

        if (httpContext.Request.ContentLength > MaxRequestBodyLength)
        {
            return TypedResults.BadRequest("The tool input is too large.");
        }

        var arguments = await ReadArgumentsAsync(httpContext.Request, httpContext.RequestAborted);

        if (arguments is null)
        {
            return TypedResults.BadRequest("The tool input must be a JSON object.");
        }

        var result = await toolService.InvokeAsync(user, name, arguments, httpContext.RequestAborted);

        if (result.Status == WebMcpToolInvocationStatus.NotFound)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new WebMcpToolCallResponse
        {
            Content =
            [
                new WebMcpTextContent
                {
                    Text = result.Text,
                },
            ],
            IsError = result.IsError,
        });
    }

    private static async Task<IDictionary<string, object>> ReadArgumentsAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object>(StringComparer.Ordinal);

        if (request.ContentLength == 0)
        {
            return arguments;
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                arguments[property.Name] = property.Value.Clone();
            }

            return arguments;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
