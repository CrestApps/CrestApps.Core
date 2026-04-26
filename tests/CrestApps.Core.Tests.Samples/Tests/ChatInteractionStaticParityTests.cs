using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

/// <summary>
/// Source-level parity assertions that do not require a running browser.
/// These run as part of any unit-test pass and catch regressions like the
/// Blazor Chat page drifting away from the MVC element-id contract that
/// <c>ai-chat.js</c> depends on.
/// </summary>
public class ChatInteractionStaticParityTests
{
    private static readonly string s_repoRoot = LocateRepoRoot();

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CrestApps.Core.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

return dir!.FullName;
    }

    [Fact]
    public async Task BlazorChatRazor_ShouldUseMvcCanonicalElementIds()
    {
        var blazorPath = Path.Combine(
            s_repoRoot,
            "src", "Startup", "CrestApps.Core.Blazor.Web",
            "Components", "Pages", "AIChat", "Chat.razor");

        var blazorRazor = await File.ReadAllTextAsync(blazorPath, TestContext.Current.CancellationToken);

        foreach (var requiredId in new[] { "chat-app", "chat-container", "chat-input", "chat-placeholder", "send-btn", "chat-document-bar", "chat-mic-btn" })
        {
            Assert.True(
                blazorRazor.Contains($"id=\"{requiredId}\"", StringComparison.Ordinal) ||
                blazorRazor.Contains($"id='{requiredId}'", StringComparison.Ordinal),
                $"Blazor Chat.razor must use the MVC-canonical element id '{requiredId}' (parity with Mvc.Web Chat.cshtml).");
        }

        foreach (var bannedId in new[] { "aichat-app", "aichat-container", "aichat-input", "aichat-placeholder", "aichat-send-btn", "aichat-document-bar" })
        {
            Assert.DoesNotContain($"id=\"{bannedId}\"", blazorRazor);
            Assert.DoesNotContain($"id='{bannedId}'", blazorRazor);
        }
    }

    [Fact]
    public async Task BlazorChatRazor_ShouldInitializeOpenAIChatManager_WithMvcSelectorContract()
    {
        var blazorPath = Path.Combine(
            s_repoRoot,
            "src", "Startup", "CrestApps.Core.Blazor.Web",
            "Components", "Pages", "AIChat", "Chat.razor");

        var blazorRazor = await File.ReadAllTextAsync(blazorPath, TestContext.Current.CancellationToken);

        // The init payload sent to window.openAIChatManager.initialize must include
        // the same keys the MVC view passes — otherwise the shared ai-chat.js
        // behaves differently across the two UIs.
        var requiredKeys = new[]
        {
            "appElementSelector", "chatContainerElementSelector", "inputElementSelector",
            "sendButtonElementSelector", "placeholderElementSelector",
            "micButtonElementSelector", "conversationButtonElementSelector",
            "documentBarSelector", "uploadDocumentUrl", "removeDocumentUrl",
            "allowedExtensions", "supportedExtensionsText",
            "existingDocuments", "sessionDocumentsEnabled",
            "metricsEnabled", "chatMode", "textToSpeechEnabled", "ttsVoiceName",
        };

        foreach (var key in requiredKeys)
        {
            Assert.Contains(key, blazorRazor);
        }
    }
}
