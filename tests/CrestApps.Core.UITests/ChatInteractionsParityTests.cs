namespace CrestApps.Core.UITests;

/// <summary>
/// Verifies Chat Interactions page behavior is identical between MVC and Blazor.
/// The "New Chat" button should exist on the Index page and redirect to a Chat page.
/// </summary>
public class ChatInteractionsParityTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public ChatInteractionsParityTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    private static string MvcIndexUrl => $"{TestConfiguration.MvcBaseUrl}/ChatInteractions/ChatInteraction";
    private static string BlazorIndexUrl => $"{TestConfiguration.BlazorBaseUrl}/ChatInteractions";

    [Fact]
    public async Task Mvc_Has_NewChat_Button_On_Index()
    {
        await VerifyNewChatButton(MvcIndexUrl);
    }

    [Fact]
    public async Task Blazor_Has_NewChat_Button_On_Index()
    {
        await VerifyNewChatButton(BlazorIndexUrl);
    }

    [Fact]
    public async Task Mvc_Index_Has_Expected_Table_Columns()
    {
        await VerifyTableColumns(MvcIndexUrl);
    }

    [Fact]
    public async Task Blazor_Index_Has_Expected_Table_Columns()
    {
        await VerifyTableColumns(BlazorIndexUrl);
    }

    [Fact]
    public async Task Mvc_Rows_Have_Chat_And_Delete_Actions()
    {
        await VerifyRowActions(MvcIndexUrl);
    }

    [Fact]
    public async Task Blazor_Rows_Have_Chat_And_Delete_Actions()
    {
        await VerifyRowActions(BlazorIndexUrl);
    }

    [Fact]
    public async Task Mvc_NewChat_Navigates_To_Chat_Page()
    {
        await VerifyNewChatNavigation(MvcIndexUrl, "/Chat");
    }

    [Fact]
    public async Task Blazor_NewChat_Navigates_To_Chat_Page()
    {
        await VerifyNewChatNavigation(BlazorIndexUrl, "/ChatInteractions/Chat/");
    }

    private async Task VerifyNewChatButton(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);

        var newChatButton = await page.QuerySelectorAsync("a.btn-success, button.btn-success");
        Assert.NotNull(newChatButton);

        var text = (await newChatButton.TextContentAsync())?.Trim();
        Assert.Equal("New Chat", text);
    }

    private async Task VerifyTableColumns(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("table");

        var headers = await page.QuerySelectorAllAsync("table thead th");
        var texts = new List<string>();

        foreach (var h in headers)
        {
            var t = (await h.TextContentAsync())?.Trim();
            if (!string.IsNullOrEmpty(t))
            {
                texts.Add(t);
            }
        }

        Assert.Contains("Title", texts);
        Assert.Contains("Created", texts);
        Assert.Contains("Actions", texts);
    }

    private async Task VerifyRowActions(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("table");

        var rows = await page.QuerySelectorAllAsync("table tbody tr");
        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            var buttons = await row.QuerySelectorAllAsync("a.btn, button.btn");
            var buttonTexts = new List<string>();

            foreach (var btn in buttons)
            {
                var text = (await btn.TextContentAsync())?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    buttonTexts.Add(text);
                }
            }

            Assert.Contains("Chat", buttonTexts);
            Assert.Contains("Delete", buttonTexts);
        }
    }

    private async Task VerifyNewChatNavigation(string indexUrl, string expectedUrlFragment)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(indexUrl);

        var newChatButton = await page.QuerySelectorAsync("a.btn-success, button.btn-success");
        Assert.NotNull(newChatButton);

        await newChatButton.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Contains(expectedUrlFragment, page.Url, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IBrowserContext> CreateContext()
    {
        return await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
    }
}
