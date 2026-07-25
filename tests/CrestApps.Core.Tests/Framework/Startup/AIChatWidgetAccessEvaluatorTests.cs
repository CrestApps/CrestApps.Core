using System.Security.Claims;
using CrestApps.Core.Startup.Shared.Services;

namespace CrestApps.Core.Tests.Framework.Startup;

public sealed class AIChatWidgetAccessEvaluatorTests
{
    [Fact]
    public void CanAccessProfile_AllowsAuthenticatedUsers()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "admin"),
        ], "Cookies"));

        var result = AIChatWidgetAccessEvaluator.CanAccessProfile(user, "widget-profile", "other-profile");

        Assert.True(result);
    }

    [Fact]
    public void CanAccessProfile_AllowsAnonymousUsersForConfiguredWidgetProfile()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var result = AIChatWidgetAccessEvaluator.CanAccessProfile(user, "widget-profile", "widget-profile");

        Assert.True(result);
    }

    [Fact]
    public void CanAccessProfile_BlocksAnonymousUsersForOtherProfiles()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var result = AIChatWidgetAccessEvaluator.CanAccessProfile(user, "widget-profile", "other-profile");

        Assert.False(result);
    }
}
