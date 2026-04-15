namespace CrestApps.Core.UITests;

/// <summary>
/// Verifies that the AI Profiles Index page has the same action buttons
/// in both MVC and Blazor (New Chat, Chat History, Test, Edit, Delete).
/// </summary>
public class AIProfileActionsParityTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public AIProfileActionsParityTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    private static string MvcProfilesUrl => $"{TestConfiguration.MvcBaseUrl}/AI/AIProfile";
    private static string BlazorProfilesUrl => $"{TestConfiguration.BlazorBaseUrl}/AI/Profiles";

    [Fact]
    public async Task Mvc_Profiles_Page_Has_Expected_Table_Columns()
    {
        await VerifyTableColumns(MvcProfilesUrl);
    }

    [Fact]
    public async Task Blazor_Profiles_Page_Has_Expected_Table_Columns()
    {
        await VerifyTableColumns(BlazorProfilesUrl);
    }

    [Fact]
    public async Task Mvc_Chat_Profile_Has_NewChat_And_ChatHistory_Buttons()
    {
        await VerifyChatProfileButtons(MvcProfilesUrl);
    }

    [Fact]
    public async Task Blazor_Chat_Profile_Has_NewChat_And_ChatHistory_Buttons()
    {
        await VerifyChatProfileButtons(BlazorProfilesUrl);
    }

    [Fact]
    public async Task Mvc_NonChat_Profile_Has_Test_Button()
    {
        await VerifyNonChatProfileButtons(MvcProfilesUrl);
    }

    [Fact]
    public async Task Blazor_NonChat_Profile_Has_Test_Button()
    {
        await VerifyNonChatProfileButtons(BlazorProfilesUrl);
    }

    [Fact]
    public async Task Both_Have_Same_Button_Labels_Per_Profile_Type()
    {
        var mvcButtons = await GetAllButtonLabelsGrouped(MvcProfilesUrl);
        var blazorButtons = await GetAllButtonLabelsGrouped(BlazorProfilesUrl);

        Assert.Equal(mvcButtons.Count, blazorButtons.Count);

        foreach (var key in mvcButtons.Keys)
        {
            Assert.True(blazorButtons.ContainsKey(key), $"Blazor missing profile type group: {key}");
            Assert.Equal(mvcButtons[key], blazorButtons[key]);
        }
    }

    private async Task VerifyTableColumns(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("table");

        var headers = await page.QuerySelectorAllAsync("table thead th");
        var headerTexts = new List<string>();

        foreach (var header in headers)
        {
            var text = (await header.TextContentAsync())?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                headerTexts.Add(text);
            }
        }

        Assert.Contains("Title", headerTexts);
        Assert.Contains("Technical Name", headerTexts);
        Assert.Contains("Type", headerTexts);
        Assert.Contains("Actions", headerTexts);
    }

    private async Task VerifyChatProfileButtons(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("table");

        // Find rows with "Chat" type badge
        var chatRows = await FindRowsByTypeBadge(page, "Chat");

        if (chatRows.Count == 0)
        {
            // No Chat profiles seeded; skip without failure
            return;
        }

        foreach (var row in chatRows)
        {
            var buttonsText = await GetButtonTextsInRow(row);
            Assert.Contains("New Chat", buttonsText);
            Assert.Contains("Chat History", buttonsText);
            Assert.Contains("Edit", buttonsText);
            Assert.Contains("Delete", buttonsText);
        }
    }

    private async Task VerifyNonChatProfileButtons(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("table");

        var utilityRows = await FindRowsByTypeBadge(page, "Utility");
        var agentRows = await FindRowsByTypeBadge(page, "Agent");
        var nonChatRows = utilityRows.Concat(agentRows).ToList();

        if (nonChatRows.Count == 0)
        {
            return;
        }

        foreach (var row in nonChatRows)
        {
            var buttonsText = await GetButtonTextsInRow(row);
            Assert.Contains("Test", buttonsText);
            Assert.Contains("Edit", buttonsText);
            Assert.Contains("Delete", buttonsText);
            Assert.DoesNotContain("New Chat", buttonsText);
            Assert.DoesNotContain("Chat History", buttonsText);
        }
    }

    private async Task<Dictionary<string, List<string>>> GetAllButtonLabelsGrouped(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("table");

        var result = new Dictionary<string, List<string>>();
        var rows = await page.QuerySelectorAllAsync("table tbody tr");

        foreach (var row in rows)
        {
            var typeBadge = await row.QuerySelectorAsync("span.badge");
            var type = typeBadge != null ? (await typeBadge.TextContentAsync())?.Trim() ?? "Unknown" : "Unknown";
            var buttons = await GetButtonTextsInRow(row);

            if (!result.ContainsKey(type))
            {
                result[type] = buttons;
            }
        }

        return result;
    }

    private static async Task<List<IElementHandle>> FindRowsByTypeBadge(IPage page, string type)
    {
        var rows = await page.QuerySelectorAllAsync("table tbody tr");
        var matching = new List<IElementHandle>();

        foreach (var row in rows)
        {
            var badge = await row.QuerySelectorAsync("span.badge");
            if (badge != null)
            {
                var text = (await badge.TextContentAsync())?.Trim();
                if (string.Equals(text, type, StringComparison.OrdinalIgnoreCase))
                {
                    matching.Add(row);
                }
            }
        }

        return matching;
    }

    private static async Task<List<string>> GetButtonTextsInRow(IElementHandle row)
    {
        var buttons = await row.QuerySelectorAllAsync("a.btn, button.btn");
        var texts = new List<string>();

        foreach (var btn in buttons)
        {
            var text = (await btn.TextContentAsync())?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                texts.Add(text);
            }
        }

        return texts;
    }

    private async Task<IBrowserContext> CreateContext()
    {
        return await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
    }
}
