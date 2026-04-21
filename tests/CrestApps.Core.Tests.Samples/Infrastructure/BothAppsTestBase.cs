using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Infrastructure;

[CollectionDefinition("Playwright")]
public class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }

public abstract class BothAppsTestBase : IClassFixture<PlaywrightFixture>
{
    protected readonly PlaywrightFixture Fixture;

    protected BothAppsTestBase(PlaywrightFixture fixture)
    {
        Fixture = fixture;
    }

    /// <summary>
    /// Gets the MVC and Blazor URL for a given relative path.
    /// Both paths are mapped so MVC and Blazor have equivalent URLs.
    /// </summary>
    protected static (string mvcUrl, string blazorUrl) GetUrls(string mvcPath, string blazorPath)
    {
        return (
            TestConstants.MvcBaseUrl + mvcPath,
            TestConstants.BlazorBaseUrl + blazorPath
        );
    }

    /// <summary>
    /// Runs an assertion action against both MVC and Blazor apps.
    /// </summary>
    protected async Task TestBothAppsAsync(
        string mvcPath,
        string blazorPath,
        Func<IPage, string, AppInstance, Task> testAction)
    {
        // Test MVC
        var mvcPage = await Fixture.CreatePageAsync();
        await testAction(mvcPage, TestConstants.MvcBaseUrl + mvcPath, AppInstance.Mvc);

        // Test Blazor
        var blazorPage = await Fixture.CreatePageAsync();
        await testAction(blazorPage, TestConstants.BlazorBaseUrl + blazorPath, AppInstance.Blazor);
    }

    /// <summary>
    /// Loads the given path on both apps (logged in) and returns the two pages.
    /// Caller is responsible for using/closing the pages.
    /// </summary>
    protected async Task<(IPage mvcPage, IPage blazorPage)> LoadBothAsync(string mvcPath, string blazorPath)
    {
        var mvcPage = await Fixture.CreatePageAsync();
        await LoginAsync(mvcPage, AppInstance.Mvc);
        await mvcPage.GotoAsync(TestConstants.MvcBaseUrl + mvcPath);
        await mvcPage.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var blazorPage = await Fixture.CreatePageAsync();
        await LoginAsync(blazorPage, AppInstance.Blazor);
        await blazorPage.GotoAsync(TestConstants.BlazorBaseUrl + blazorPath);
        await blazorPage.WaitForLoadStateAsync(LoadState.NetworkIdle);

        return (mvcPage, blazorPage);
    }

    /// <summary>
    /// Logs in to the specified app via its login page.
    /// </summary>
    protected static async Task LoginAsync(IPage page, AppInstance app)
    {
        var baseUrl = app == AppInstance.Mvc ? TestConstants.MvcBaseUrl : TestConstants.BlazorBaseUrl;
        var loginPath = app == AppInstance.Mvc ? "/Account/Login" : "/account/login";

        await page.GotoAsync(baseUrl + loginPath);

        await page.FillAsync("#username", TestConstants.TestUsername);
        await page.FillAsync("#password", TestConstants.TestPassword);
        await page.ClickAsync("button[type='submit']");

        // Wait for navigation after login
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
