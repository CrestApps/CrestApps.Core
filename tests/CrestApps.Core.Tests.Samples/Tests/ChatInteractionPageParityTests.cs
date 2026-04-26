using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

/// <summary>
/// Focused tests that lock in the parity behavior of the Chat Interaction
/// detail page (settings autosave + Knowledge tab). These complement the
/// generic <see cref="SourceParityTests"/> by asserting concrete IDs, data
/// attributes, and JS bootstrap calls that both UIs must agree on.
/// </summary>
public class ChatInteractionPageParityTests
{
    private static readonly string s_repoRoot = LocateRepoRoot();

    private const string MvcChatViewPath = "src/Startup/CrestApps.Core.Mvc.Web/Areas/ChatInteractions/Views/ChatInteraction/Chat.cshtml";
    private const string BlazorChatPagePath = "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/ChatInteractions/Chat.razor";
    private const string SharedSettingsScriptPath = "src/Resources/CrestApps.AI.Resources/Assets/js/chat-interaction-settings.js";

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

    public static TheoryData<string> ParameterSettingKeys() =>
    [
        "pastMessagesCount",
        "temperature",
        "topP",
        "frequencyPenalty",
        "presencePenalty",
        "maxTokens",
        "systemMessage",
        "deploymentName",
        "orchestratorName",
    ];

    [Theory]
    [MemberData(nameof(ParameterSettingKeys))]
    public void Both_uis_expose_the_same_parameter_setting_keys(string settingKey)
    {
        var mvc = ReadAll(MvcChatViewPath);
        var blazor = ReadAll(BlazorChatPagePath);

        var marker = $"data-setting=\"{settingKey}\"";

        Assert.Contains(marker, mvc);
        Assert.Contains(marker, blazor);
    }

    public static TheoryData<string> KnowledgeSettingKeys() =>
    [
        "dataSourceId",
        "filter",
        "isInScope",
        "strictness",
        "topNDocuments",
    ];

    [Theory]
    [MemberData(nameof(KnowledgeSettingKeys))]
    public void Knowledge_tab_exposes_the_same_setting_keys(string settingKey)
    {
        var mvc = ReadAll(MvcChatViewPath);
        var blazor = ReadAll(BlazorChatPagePath);

        var marker = $"data-setting=\"{settingKey}\"";

        Assert.Contains(marker, mvc);
        Assert.Contains(marker, blazor);
    }

    public static TheoryData<string> CapabilityGroups() =>
    [
        "toolNames",
        "agentNames",
        "a2aConnectionIds",
        "mcpConnectionIds",
    ];

    [Theory]
    [MemberData(nameof(CapabilityGroups))]
    public void Capability_checkboxes_use_the_same_data_group_in_both_uis(string group)
    {
        var mvc = ReadAll(MvcChatViewPath);
        var blazor = ReadAll(BlazorChatPagePath);

        var marker = $"data-group=\"{group}\"";

        Assert.Contains(marker, mvc);
        Assert.Contains(marker, blazor);
    }

    [Fact]
    public void Filter_field_in_blazor_is_wrapped_in_rag_data_source_filter_dependent_so_it_hides_when_no_source_is_selected()
    {
        var blazor = ReadAll(BlazorChatPagePath);

        // Same CSS hook MVC uses to toggle visibility from JavaScript.
        Assert.Contains("rag-data-source-filter-dependent", blazor);
        // And the filter input itself must be inside that wrapper.
        Assert.Matches(
            new System.Text.RegularExpressions.Regex(
                "rag-data-source-filter-dependent[\\s\\S]{0,500}data-setting=\"filter\"",
                System.Text.RegularExpressions.RegexOptions.Multiline),
            blazor);
    }

    [Fact]
    public void Blazor_knowledge_tab_includes_document_upload_drop_zone()
    {
        var blazor = ReadAll(BlazorChatPagePath);

        Assert.Contains("chat-doc-drop-zone", blazor);
        Assert.Contains("chat-doc-upload", blazor);
        Assert.Contains("chat-doc-browse", blazor);
        Assert.Contains("chat-documents-list", blazor);
    }

    [Fact]
    public void Blazor_knowledge_tab_warns_when_document_indexing_is_not_configured()
    {
        var blazor = ReadAll(BlazorChatPagePath);

        Assert.Contains("HasDocumentIndexConfiguration", blazor);
        Assert.Contains("Document indexing is not configured", blazor);
    }

    [Fact]
    public void Blazor_chat_page_bootstraps_shared_autosave_module()
    {
        var blazor = ReadAll(BlazorChatPagePath);

        Assert.Contains("initializeChatInteractionSettings", blazor);
        // The save-status badge id the shared module updates.
        Assert.Contains("chat-settings-save-status", blazor);
    }

    [Fact]
    public void Shared_autosave_module_exists_and_exports_init_function()
    {
        var script = ReadAll(SharedSettingsScriptPath);

        Assert.Contains("window.initializeChatInteractionSettings", script);
        Assert.Contains("SaveSettings", script); // hub method invoked
        Assert.Contains("Saved", script);        // success label
        Assert.Contains("Saving", script);       // pending label
        Assert.Contains("rag-data-source-filter-dependent", script);
    }

    [Fact]
    public void Shared_autosave_module_listens_to_input_change_and_blur_so_values_save_on_focus_loss()
    {
        var script = ReadAll(SharedSettingsScriptPath);

        Assert.Contains("addEventListener('input'", script);
        Assert.Contains("addEventListener('change'", script);
        Assert.Contains("addEventListener('blur'", script);
    }
}
