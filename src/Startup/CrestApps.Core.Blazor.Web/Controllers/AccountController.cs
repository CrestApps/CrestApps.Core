using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Blazor.Web.Controllers;

[Route("Account")]
public sealed class AccountController : Controller
{
    private readonly IConfiguration _configuration;

    public AccountController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("LoginPost")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPost(string username, string password, string returnUrl = null)
    {
        var adminUsername = _configuration["CrestApps:Admin:Username"] ?? "admin";
        var adminPassword = _configuration["CrestApps:Admin:Password"] ?? "admin";

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

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return Redirect("/");
        }

        return Redirect("/Account/Login?error=" + Uri.EscapeDataString("Invalid username or password."));
    }

    [HttpPost("Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Redirect("/");
    }
}
