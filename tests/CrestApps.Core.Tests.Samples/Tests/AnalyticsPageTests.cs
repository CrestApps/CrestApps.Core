using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

[Collection("Playwright")]
public class AnalyticsPageTests : BothAppsTestBase
{
    public AnalyticsPageTests(PlaywrightFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(AppInstance.Mvc, "/AIChat/ChatAnalytics")]
    [InlineData(AppInstance.Blazor, "/ai-chat/analytics")]
    public async Task ChatAnalyticsPage_ShouldExist(AppInstance app, string path)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var response = await page.GotoAsync(baseUrl + path);
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Chat Analytics returned {response.Status} for {app}");

        var body = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Analytics", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AppInstance.Mvc, "/AIChat/UsageAnalytics")]
    [InlineData(AppInstance.Blazor, "/ai-chat/usage-analytics")]
    public async Task UsageAnalyticsPage_ShouldExist(AppInstance app, string path)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var response = await page.GotoAsync(baseUrl + path);
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Usage Analytics returned {response.Status} for {app}");

        var body = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Usage", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AppInstance.Mvc, "/AIChat/ChatExtractedData")]
    [InlineData(AppInstance.Blazor, "/ai-chat/extracted-data")]
    public async Task ExtractedDataPage_ShouldExist(AppInstance app, string path)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var response = await page.GotoAsync(baseUrl + path);
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Extracted Data returned {response.Status} for {app}");

        var body = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Extracted", body, StringComparison.OrdinalIgnoreCase);
    }
}
