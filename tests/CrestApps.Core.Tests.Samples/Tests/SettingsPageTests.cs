using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

[Collection("Playwright")]
public class SettingsPageTests : BothAppsTestBase
{
    public SettingsPageTests(PlaywrightFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task SettingsPage_ShouldExist(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var path = app == AppInstance.Mvc ? "/Admin/Settings" : "/admin/settings";

        var response = await page.GotoAsync(baseUrl + path);
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Settings page returned {response.Status} for {app}");
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task SettingsPage_ShouldHaveSiteNameField(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var path = app == AppInstance.Mvc ? "/Admin/Settings" : "/admin/settings";

        await page.GotoAsync(baseUrl + path);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Site Name", bodyText, StringComparison.OrdinalIgnoreCase);
    }
}
