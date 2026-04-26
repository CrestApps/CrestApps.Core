using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

[Collection("Playwright")]
public class LoginTests : BothAppsTestBase
{
    public LoginTests(PlaywrightFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task LoginPage_ShouldHaveUsernameAndPasswordFields(AppInstance app)
    {
        await EnsureAppIsReachableOrSkipAsync(app);

        var page = await Fixture.CreatePageAsync();
        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var loginPath = app == AppInstance.Mvc ? "/Account/Login" : "/account/login";

        await page.GotoAsync(baseUrl + loginPath);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should have username input
        var usernameField = page.Locator("#username").First;
        await Assertions.Expect(usernameField).ToBeVisibleAsync();

        // Should have password input
        var passwordField = page.Locator("#password").First;
        await Assertions.Expect(passwordField).ToBeVisibleAsync();

        // Should have submit button
        var submitButton = page.Locator("button[type='submit']").First;
        await Assertions.Expect(submitButton).ToBeVisibleAsync();
    }
}
