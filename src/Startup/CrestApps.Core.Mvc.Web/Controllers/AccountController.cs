using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly IConfiguration _configuration;

    public AccountController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string returnUrl = null)
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

            return RedirectToAction("Index", "Home");
        }

        // Optional non-admin users (CrestApps:Users:0:Username, CrestApps:Users:0:Password, ...) for trying the
        // site as ordinary signed-in users, for example to check that one user never sees another's conversations.
        var regularUser = _configuration.GetSection("CrestApps:Users").GetChildren().FirstOrDefault(section =>
            !string.IsNullOrEmpty(section["Username"]) &&
            !string.IsNullOrEmpty(section["Password"]) &&
            string.Equals(section["Username"], username, StringComparison.Ordinal) &&
            string.Equals(section["Password"], password, StringComparison.Ordinal));

        if (regularUser is not null)
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, username),
                new Claim(ClaimTypes.Name, username),
            ], CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        ViewData["Error"] = "Invalid username or password.";
        ViewData["ReturnUrl"] = returnUrl;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
