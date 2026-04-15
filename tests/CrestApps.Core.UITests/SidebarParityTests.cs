namespace CrestApps.Core.UITests;

/// <summary>
/// Verifies that the sidebar navigation items are identical between MVC and Blazor.
/// </summary>
public class SidebarParityTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public SidebarParityTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Expected sidebar item labels in order. Both MVC and Blazor must match.
    /// </summary>
    private static readonly string[] ExpectedSidebarItems =
    [
        "Dashboard",
        "AI Connections",
        "AI Deployments",
        "Chat Interactions",
        "AI Profiles",
        "Index Profiles",
        "Data Sources",
        "Templates",
        "Articles",
        "Reports",
        "Chat Analytics",
        "AI Usage Analytics",
        "Chat Extracted Data",
        "Agent to Agent Hosts",
        "MCP Hosts",
        "MCP Prompts",
        "MCP Resources",
        "Settings",
    ];

    [Fact]
    public async Task Mvc_Sidebar_Contains_Expected_Items()
    {
        await VerifySidebarItems(TestConfiguration.MvcBaseUrl);
    }

    [Fact]
    public async Task Blazor_Sidebar_Contains_Expected_Items()
    {
        await VerifySidebarItems(TestConfiguration.BlazorBaseUrl);
    }

    [Fact]
    public async Task Mvc_Sidebar_Does_Not_Contain_AI_Chat()
    {
        await VerifyNoAIChatInSidebar(TestConfiguration.MvcBaseUrl);
    }

    [Fact]
    public async Task Blazor_Sidebar_Does_Not_Contain_AI_Chat()
    {
        await VerifyNoAIChatInSidebar(TestConfiguration.BlazorBaseUrl);
    }

    [Fact]
    public async Task Both_Sidebars_Have_Same_Items()
    {
        var mvcItems = await GetSidebarItemTexts(TestConfiguration.MvcBaseUrl);
        var blazorItems = await GetSidebarItemTexts(TestConfiguration.BlazorBaseUrl);

        Assert.Equal(mvcItems, blazorItems);
    }

    private async Task VerifySidebarItems(string baseUrl)
    {
        var items = await GetSidebarItemTexts(baseUrl);

        foreach (var expected in ExpectedSidebarItems)
        {
            Assert.Contains(expected, items);
        }
    }

    private async Task VerifyNoAIChatInSidebar(string baseUrl)
    {
        var items = await GetSidebarItemTexts(baseUrl);

        Assert.DoesNotContain("AI Chat", items);
    }

    private async Task<List<string>> GetSidebarItemTexts(string baseUrl)
    {
        await using var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(baseUrl);
        await page.WaitForSelectorAsync("nav#sidebar");

        var navItems = await page.QuerySelectorAllAsync("nav#sidebar .nav-link");
        var texts = new List<string>();

        foreach (var item in navItems)
        {
            var text = (await item.TextContentAsync())?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                texts.Add(text);
            }
        }

        return texts;
    }
}
