#nullable enable

using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.Blazor.Web;

/// <summary>
/// Maps minimal API endpoints for authentication operations that require
/// HTTP context (cookie auth cannot be done from Blazor components).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/sign-in", async (HttpContext context, IAntiforgery antiforgery, IConfiguration configuration) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest("Invalid antiforgery token.");
            }

            var form = await context.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();

            var adminUsername = configuration["CrestApps:Admin:Username"] ?? "admin";
            var adminPassword = configuration["CrestApps:Admin:Password"] ?? "admin";

            if (username == adminUsername && password == adminPassword)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, username),
                    new(ClaimTypes.Name, username),
                    new(ClaimTypes.Role, "Administrator"),
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                var redirect = !string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : "/";

                return Results.LocalRedirect(redirect);
            }

            var redirectUrl = QueryString.Create(new Dictionary<string, string?>
            {
                ["returnUrl"] = returnUrl,
                ["error"] = "Invalid username or password.",
            });

            return Results.LocalRedirect($"/account/login{redirectUrl}");
        });

        endpoints.MapPost("/Account/Logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest("Invalid antiforgery token.");
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Results.Redirect("/");
        });

        return endpoints;
    }
}
