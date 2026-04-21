using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

[Collection("Playwright")]
public class NavigationTests : BothAppsTestBase
{
    public NavigationTests(PlaywrightFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task Sidebar_ShouldContainAllNavigationLinks(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        await page.GotoAsync(baseUrl + "/");

        var expectedLinks = new[]
        {
            "Dashboard",
            "AI Connections",
            "AI Deployments",
            "Chat Interactions",
            "AI Profiles",
            "Index Profiles",
            "Data Sources",
            "Templates",
            "Articles",
            "Chat Analytics",
            "AI Usage Analytics",
            "Chat Extracted Data",
            "Agent to Agent Hosts",
            "MCP Hosts",
            "MCP Prompts",
            "MCP Resources",
            "Settings"
        };

        foreach (var linkText in expectedLinks)
        {
            var link = page.Locator($"#sidebar .nav-link:has-text('{linkText}'), nav .nav-link:has-text('{linkText}')").First;
            await Assertions.Expect(link).ToBeVisibleAsync();
        }
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task Sidebar_ShouldContainBootstrapIcons(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        await page.GotoAsync(baseUrl + "/");

        // Check that sidebar nav links have bootstrap icons
        var iconsInSidebar = page.Locator("#sidebar .nav-link i.bi, nav .nav-link i.bi");
        var count = await iconsInSidebar.CountAsync();
        Assert.True(count >= 15, $"Expected at least 15 icons in sidebar, found {count} for {app}");
    }
}
