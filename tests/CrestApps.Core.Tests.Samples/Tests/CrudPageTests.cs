using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

/// <summary>
/// Tests that all CRUD index and create pages exist with the expected structure.
/// These tests validate that both MVC and Blazor apps have identical page structures.
/// </summary>
[Collection("Playwright")]
public class CrudPageTests : BothAppsTestBase
{
    public CrudPageTests(PlaywrightFixture fixture) : base(fixture) { }

    public static TheoryData<AppInstance, string, string, string> IndexPages => new()
    {
        // app, mvcPath, blazorPath, expectedHeading
        { AppInstance.Mvc, "/AI/AIConnection", "/ai/connections", "AI Connections" },
        { AppInstance.Blazor, "/AI/AIConnection", "/ai/connections", "AI Connections" },
        { AppInstance.Mvc, "/AI/AIDeployment", "/ai/deployments", "AI Deployments" },
        { AppInstance.Blazor, "/AI/AIDeployment", "/ai/deployments", "AI Deployments" },
        { AppInstance.Mvc, "/AI/AIProfile", "/ai/profiles", "AI Profiles" },
        { AppInstance.Blazor, "/AI/AIProfile", "/ai/profiles", "AI Profiles" },
        { AppInstance.Mvc, "/AI/AITemplate", "/ai/templates", "Templates" },
        { AppInstance.Blazor, "/AI/AITemplate", "/ai/templates", "Templates" },
        { AppInstance.Mvc, "/ChatInteractions/ChatInteraction", "/chat-interactions", "Chat Interactions" },
        { AppInstance.Blazor, "/ChatInteractions/ChatInteraction", "/chat-interactions", "Chat Interactions" },
        { AppInstance.Mvc, "/Indexing/IndexProfile", "/indexing/profiles", "Index Profiles" },
        { AppInstance.Blazor, "/Indexing/IndexProfile", "/indexing/profiles", "Index Profiles" },
        { AppInstance.Mvc, "/DataSources/AIDataSource", "/datasources", "Data Sources" },
        { AppInstance.Blazor, "/DataSources/AIDataSource", "/datasources", "Data Sources" },
        { AppInstance.Mvc, "/Admin/Article", "/admin/articles", "Articles" },
        { AppInstance.Blazor, "/Admin/Article", "/admin/articles", "Articles" },
        { AppInstance.Mvc, "/A2A/A2AConnection", "/a2a/connections", "A2A" },
        { AppInstance.Blazor, "/A2A/A2AConnection", "/a2a/connections", "A2A" },
        { AppInstance.Mvc, "/Mcp/McpConnection", "/mcp/connections", "MCP" },
        { AppInstance.Blazor, "/Mcp/McpConnection", "/mcp/connections", "MCP" },
        { AppInstance.Mvc, "/Mcp/McpPrompt", "/mcp/prompts", "MCP Prompts" },
        { AppInstance.Blazor, "/Mcp/McpPrompt", "/mcp/prompts", "MCP Prompts" },
        { AppInstance.Mvc, "/Mcp/McpResource", "/mcp/resources", "MCP Resources" },
        { AppInstance.Blazor, "/Mcp/McpResource", "/mcp/resources", "MCP Resources" },
    };

    [Theory]
    [MemberData(nameof(IndexPages))]
    public async Task IndexPage_ShouldExistAndHaveHeading(AppInstance app, string mvcPath, string blazorPath, string expectedHeading)
    {
        var page = await Fixture.CreatePageAsync();
        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var path = app == AppInstance.Mvc ? mvcPath : blazorPath;

        await LoginAsync(page, app);

        var response = await page.GotoAsync(baseUrl + path);
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Page {path} returned status {response.Status} for {app}");

        // Page should contain the expected heading text somewhere
        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains(expectedHeading, bodyText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task IndexPages_ShouldHaveCreateButton(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);

        // Test a few representative index pages for create buttons
        var paths = app == AppInstance.Mvc
            ? new[] { "/AI/AIConnection", "/AI/AIDeployment", "/AI/AIProfile" }
            : new[] { "/ai/connections", "/ai/deployments", "/ai/profiles" };

        foreach (var path in paths)
        {
            await page.GotoAsync(baseUrl + path);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Should have a "Create" or "Add" button/link
            var createButton = page.Locator("a:has-text('Create'), a:has-text('Add'), a.btn-success").First;
            await Assertions.Expect(createButton).ToBeVisibleAsync();
        }
    }
}
