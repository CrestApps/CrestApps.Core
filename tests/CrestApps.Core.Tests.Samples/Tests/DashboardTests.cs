using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

[Collection("Playwright")]
public class DashboardTests : BothAppsTestBase
{
    public DashboardTests(PlaywrightFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task Dashboard_ShouldContainGettingStartedCard(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        await page.GotoAsync(baseUrl + "/");

        var gettingStarted = page.Locator("text=Getting Started").First;
        await Expect(gettingStarted).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task Dashboard_ShouldContainAllFeatureCards(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        await page.GotoAsync(baseUrl + "/");

        var expectedCards = new[]
        {
            "AI Connections",
            "AI Deployments",
            "Chat Interactions",
            "AI Profiles",
            "Index Profiles",
            "Data Sources",
            "Templates"
        };

        foreach (var card in expectedCards)
        {
            var cardElement = page.Locator($".card-title:has-text('{card}')").First;
            await Expect(cardElement).ToBeVisibleAsync();
        }
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task Dashboard_ShouldContainA2ASection(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        await page.GotoAsync(baseUrl + "/");

        var a2aSection = page.Locator("text=Agent to Agent Protocol").First;
        await Expect(a2aSection).ToBeVisibleAsync();

        var a2aCard = page.Locator(".card-title:has-text('Agent to Agent Hosts')").First;
        await Expect(a2aCard).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task Dashboard_ShouldContainMcpSection(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        await page.GotoAsync(baseUrl + "/");

        var mcpSection = page.Locator("text=Model Context Protocol").First;
        await Expect(mcpSection).ToBeVisibleAsync();

        var expectedCards = new[] { "MCP Hosts", "MCP Prompts", "MCP Resources" };
        foreach (var card in expectedCards)
        {
            var cardElement = page.Locator($".card-title:has-text('{card}')").First;
            await Expect(cardElement).ToBeVisibleAsync();
        }
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
