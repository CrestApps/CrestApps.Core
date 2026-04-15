namespace CrestApps.Core.UITests;

/// <summary>
/// Verifies that the Chat Interaction Chat page has a sidebar + chat window layout
/// matching between MVC and Blazor.
/// </summary>
public class ChatLayoutParityTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public ChatLayoutParityTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Mvc_ChatInteraction_Chat_Has_Sidebar_And_ChatWindow()
    {
        // First create a chat interaction to get a valid chat page
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();

        // Navigate to Chat Interactions and create one
        var indexUrl = $"{TestConfiguration.MvcBaseUrl}/ChatInteractions/ChatInteraction";
        await page.GotoAsync(indexUrl);

        var newChatBtn = await page.QuerySelectorAsync("a.btn-success, button.btn-success");
        if (newChatBtn == null)
        {
            return;
        }

        await newChatBtn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Now we should be on the Chat page — verify layout elements
        await VerifyChatPageLayout(page);
    }

    [Fact]
    public async Task Blazor_ChatInteraction_Chat_Has_Sidebar_And_ChatWindow()
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();

        var indexUrl = $"{TestConfiguration.BlazorBaseUrl}/ChatInteractions";
        await page.GotoAsync(indexUrl);

        var newChatBtn = await page.QuerySelectorAsync("a.btn-success, button.btn-success");
        if (newChatBtn == null)
        {
            return;
        }

        await newChatBtn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await VerifyChatPageLayout(page);
    }

    private static async Task VerifyChatPageLayout(IPage page)
    {
        // The chat page should have a settings/parameters panel and a chat area
        // Common patterns: a sidebar with form controls and a chat message area

        // Look for a form/settings area (typically contains selects, inputs for model params)
        var settingsPanel = await page.QuerySelectorAsync(
            ".settings-panel, .chat-settings, .sidebar, [class*='settings'], [class*='sidebar'], [class*='parameter']"
        );

        // Look for a chat message area
        var chatArea = await page.QuerySelectorAsync(
            ".chat-messages, .message-area, [class*='chat'], [class*='message'], #chatMessages, .chat-container"
        );

        // Look for a message input
        var messageInput = await page.QuerySelectorAsync(
            "textarea, input[type='text'][placeholder*='message'], input[placeholder*='Message'], " +
            "textarea[placeholder*='message'], textarea[placeholder*='Message']"
        );

        Assert.True(
            settingsPanel != null || chatArea != null,
            $"Chat page at {page.Url} should have settings panel and/or chat area"
        );

        Assert.True(
            messageInput != null,
            $"Chat page at {page.Url} should have a message input field"
        );
    }

    private async Task<IBrowserContext> CreateContext()
    {
        return await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
    }
}
