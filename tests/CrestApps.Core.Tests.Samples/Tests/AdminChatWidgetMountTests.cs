using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

/// <summary>
/// Asserts that both UIs mount the admin chat widget at a place where every
/// authenticated page renders it. MVC injects it via the shared layout; Blazor
/// must do the same via <c>MainLayout.razor</c>. Without this, configuring an
/// admin widget profile in Settings has no visible effect — which is exactly
/// the regression we are guarding against here.
/// </summary>
public class AdminChatWidgetMountTests
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

    private static string ReadAll(string relative) => File.ReadAllText(Path.Combine(s_repoRoot, relative));

    [Fact]
    public void Mvc_layout_includes_chat_widget_partial()
    {
        var layout = ReadAll("src/Startup/CrestApps.Core.Mvc.Web/Views/Shared/_Layout.cshtml");

        Assert.Contains("_ChatWidget", layout);
    }

    [Fact]
    public void Blazor_main_layout_renders_chat_widget_component()
    {
        var layout = ReadAll("src/Startup/CrestApps.Core.Blazor.Web/Components/Layout/MainLayout.razor");

        Assert.Contains("<ChatWidget", layout);
    }

    [Fact]
    public void Blazor_chat_widget_component_exists()
    {
        var component = ReadAll("src/Startup/CrestApps.Core.Blazor.Web/Components/Shared/ChatWidget.razor");

        // The widget shell id is what window.openAIChatWidgetManager.initialize binds to.
        Assert.Contains("ai-chat-admin-widget-shell", component);
        Assert.Contains("openAIChatWidgetManager.initialize", component);
    }

    [Fact]
    public void Both_chat_widget_hosts_forward_the_profile_realtime_voice()
    {
        var mvcWidget = ReadAll("src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/Shared/_ChatWidget.cshtml");
        var blazorWidget = ReadAll("src/Startup/CrestApps.Core.Blazor.Web/Components/Shared/ChatWidget.razor");
        var widgetScript = ReadAll("src/Resources/CrestApps.AI.Resources/Assets/js/ai-chat-widget.js");

        Assert.Contains("data-coreai-chat-realtime-voice-name", mvcWidget);
        Assert.Contains("data-coreai-chat-realtime-voice-name", blazorWidget);
        Assert.Contains("data-coreai-chat-widget-profile-realtime-voice-name", widgetScript);
        Assert.Contains("realtimeVoiceName: profileRealtimeVoiceName", widgetScript);
    }

    [Fact]
    public void Standalone_realtime_test_surface_is_not_registered()
    {
        var mvcLayout = ReadAll("src/Startup/CrestApps.Core.Mvc.Web/Views/Shared/_Layout.cshtml");
        var blazorNav = ReadAll("src/Startup/CrestApps.Core.Blazor.Web/Components/Layout/NavMenu.razor");
        var blazorApp = ReadAll("src/Startup/CrestApps.Core.Blazor.Web/Components/App.razor");
        var blazorProgram = ReadAll("src/Startup/CrestApps.Core.Blazor.Web/Program.cs");

        Assert.DoesNotContain("Realtime model test", mvcLayout);
        Assert.DoesNotContain("/realtime", blazorNav);
        Assert.DoesNotContain("realtime-test.js", blazorApp);
        Assert.DoesNotContain("/Realtime/Stream", blazorProgram);
    }
}
