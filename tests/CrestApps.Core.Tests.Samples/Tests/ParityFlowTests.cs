using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

[Collection("Playwright")]
public class ParityFlowTests : BothAppsTestBase
{
    public ParityFlowTests(PlaywrightFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task CreateTemplate_ShouldExposeMatchingCoreFields(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var path = app == AppInstance.Mvc ? "/AI/AITemplate/Create" : "/ai/templates/create";

        await page.GotoAsync(baseUrl + path);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var expectedTexts = new[]
        {
            "Title",
            "Technical name",
            "Source",
            "Category",
            "Basic Info",
            "AI Parameters",
            "Capabilities",
            "Knowledge",
            "Data Processing & Metrics",
            "Settings",
        };

        var bodyText = await page.Locator("body").InnerTextAsync();
        foreach (var expectedText in expectedTexts)
        {
            Assert.Contains(expectedText, bodyText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task CreateTemplate_ShouldAutoGenerateTechnicalName(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var path = app == AppInstance.Mvc ? "/AI/AITemplate/Create" : "/ai/templates/create";
        var titleSelector = app == AppInstance.Mvc ? "#templateTitle" : "#templateTitle";
        var technicalNameSelector = app == AppInstance.Mvc ? "#templateTechName" : "#templateName";

        await page.GotoAsync(baseUrl + path);
        await page.FillAsync(titleSelector, "Customer Support Prompt");

        await Assertions.Expect(page.Locator(technicalNameSelector)).ToHaveValueAsync("CustomerSupportPrompt");
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task CreateConnection_AzureProvider_ShouldShowAuthenticationTypeAndApiKey(AppInstance app)
    {
        var page = await Fixture.CreatePageAsync();
        await LoginAsync(page, app);

        var baseUrl = PlaywrightFixture.GetBaseUrl(app);
        var path = app == AppInstance.Mvc ? "/AI/AIConnection/Create" : "/ai/connections/create";

        await page.GotoAsync(baseUrl + path);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.SelectOptionAsync("#providerSelect", new[] { "Azure" });

        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Authentication type", bodyText, StringComparison.OrdinalIgnoreCase);

        await page.SelectOptionAsync("#authTypeSelect", new[] { "ApiKey" });

        var apiKeyInput = page.Locator("input[type='password'][name='ApiKey'], input[type='password'][name='AIConnection.ApiKey'], #ApiKey, #apiKey");
        await Assertions.Expect(apiKeyInput.First).ToBeVisibleAsync();
    }
}
