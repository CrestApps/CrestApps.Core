using System.Text.Json;
using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

/// <summary>
/// Opens the WebMCP playground on both hosts and acts as a WebMCP agent through the standard
/// <c>document.modelContext</c> API: the fake store's tools, the page tools and the site's AI tools must all be
/// listed by <c>getTools()</c>, and <c>executeTool()</c> must run them on the page.
/// </summary>
[Collection("Playwright")]
public sealed class WebMcpPlaygroundTests : BothAppsTestBase
{
    private static readonly string[] s_storeToolNames =
    [
        "store.search_products",
        "store.get_order",
        "store.read_reviews",
        "store.add_to_cart",
        "store.place_order",
    ];

    private static readonly string[] s_pageToolNames =
    [
        "page_get_info",
        "page_list_links",
        "page_navigate",
        "page_list_form_fields",
        "page_set_form_field",
    ];

    public WebMcpPlaygroundTests(PlaywrightFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task Agent_lists_the_store_tools_and_the_page_tools(AppInstance app)
    {
        var page = await OpenPlaygroundAsync(app);

        var names = await page.EvaluateAsync<string[]>("async () => (await document.modelContext.getTools()).map((tool) => tool.name)");

        Assert.All(s_storeToolNames, name => Assert.Contains(name, names));
        Assert.All(s_pageToolNames, name => Assert.Contains(name, names));
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());

        var annotations = await page.EvaluateAsync<JsonElement>("""
            async () => Object.fromEntries((await document.modelContext.getTools())
                .filter((tool) => tool.name.startsWith('store.'))
                .map((tool) => [tool.name, tool.annotations || {}]))
            """);

        Assert.True(annotations.GetProperty("store.get_order").GetProperty("readOnlyHint").GetBoolean());
        Assert.True(annotations.GetProperty("store.read_reviews").GetProperty("untrustedContentHint").GetBoolean());
        Assert.True(annotations.GetProperty("store.place_order").GetProperty("consequentialHint").GetBoolean());
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task Agent_runs_the_store_tools_on_the_page(AppInstance app)
    {
        var page = await OpenPlaygroundAsync(app);

        var order = await RunToolAsync(page, "store.get_order", new { orderId = "A-1001" });

        Assert.Equal("shipped", order.GetProperty("status").GetString());

        await RunToolAsync(page, "store.add_to_cart", new { productId = "lamp", quantity = 2 });

        var cartLine = page.Locator("#webmcp-playground-cart li[data-product='lamp']");

        await Assertions.Expect(cartLine).ToBeVisibleAsync();
        await Assertions.Expect(cartLine).ToContainTextAsync("x2");

        var placed = await RunToolAsync(page, "store.place_order", new { });

        Assert.StartsWith("A-", placed.GetProperty("orderId").GetString());
        await Assertions.Expect(page.Locator("#webmcp-playground-cart li[data-product]")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task A_failing_tool_rejects(AppInstance app)
    {
        var page = await OpenPlaygroundAsync(app);

        var rejected = await page.EvaluateAsync<bool>("""
            async () => {
                const tool = (await document.modelContext.getTools()).find((candidate) => candidate.name === 'store.get_order');

                try {
                    await document.modelContext.executeTool(tool, { orderId: 'Z-9' });

                    return false;
                } catch {
                    return true;
                }
            }
            """);

        Assert.True(rejected);
    }

    [Theory]
    [InlineData(AppInstance.Mvc)]
    [InlineData(AppInstance.Blazor)]
    public async Task The_playground_lists_every_tool_the_page_exposes(AppInstance app)
    {
        var page = await OpenPlaygroundAsync(app);

        var rows = page.Locator("#webmcp-playground-tools tr td:first-child");

        await Assertions.Expect(rows.Filter(new LocatorFilterOptions { HasTextString = "store.add_to_cart" })).ToHaveCountAsync(1);
        await Assertions.Expect(rows.Filter(new LocatorFilterOptions { HasTextString = "page_get_info" })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Leaving_the_Blazor_playground_unregisters_the_store_tools()
    {
        var page = await OpenPlaygroundAsync(AppInstance.Blazor);

        // Blazor navigates without reloading, so the page must abort its registrations itself.
        await page.Locator("a.nav-link[href='/chat-interactions']").First.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/webmcp/playground", StringComparison.OrdinalIgnoreCase));

        // The circuit disposes the page after the navigation, so wait for the registrations to go.
        await page.WaitForFunctionAsync(
            "async () => !(await document.modelContext.getTools()).some((tool) => tool.name.startsWith('store.'))",
            null,
            new PageWaitForFunctionOptions { PollingInterval = 100, Timeout = 10_000 });

        var names = await page.EvaluateAsync<string[]>("async () => (await document.modelContext.getTools()).map((tool) => tool.name)");

        Assert.Contains("page_get_info", names);
    }

    private async Task<IPage> OpenPlaygroundAsync(AppInstance app)
    {
        await EnsureAppIsReachableOrSkipAsync(app);

        var page = await Fixture.CreatePageAsync();
        var baseUrl = app == AppInstance.Mvc ? TestConstants.MvcBaseUrl : TestConstants.BlazorBaseUrl;

        await LoginAsync(page, app);
        await page.GotoAsync(baseUrl + "/webmcp/playground");
        await page.WaitForFunctionAsync("() => !!document.modelContext");
        await Assertions.Expect(page.Locator("#webmcp-playground-tools td", new PageLocatorOptions { HasTextString = "store.get_order" }))
            .ToBeVisibleAsync();

        return page;
    }

    private static async Task<JsonElement> RunToolAsync(IPage page, string name, object input)
    {
        var serialized = await page.EvaluateAsync<string>("""
            async ([name, input]) => {
                const tool = (await document.modelContext.getTools()).find((candidate) => candidate.name === name);

                return document.modelContext.executeTool(tool, input);
            }
            """, new object[] { name, input });

        return JsonDocument.Parse(serialized).RootElement.Clone();
    }
}
