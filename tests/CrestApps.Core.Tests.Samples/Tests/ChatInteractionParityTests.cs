using CrestApps.Core.Tests.Samples.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

/// <summary>
/// Strict structural parity tests across **all** Mvc.Web ↔ Blazor.Web page pairs.
/// Mvc.Web is the source of truth — Blazor.Web must match.
/// Every test loads the same logical page in both Mvc.Web and Blazor.Web,
/// extracts a structural fingerprint (tabs, headings, field labels, buttons)
/// and asserts the two are identical. Mvc.Web is the source of truth.
///
/// These tests require both apps to be running:
///   - Mvc.Web at <see cref="TestConstants.MvcBaseUrl"/>
///   - Blazor.Web at <see cref="TestConstants.BlazorBaseUrl"/>
/// </summary>
[Collection("Playwright")]
public class ChatInteractionParityTests : BothAppsTestBase
{
    public ChatInteractionParityTests(PlaywrightFixture fixture)
        : base(fixture)
    {
    }

    public static IEnumerable<object[]> ParityPages => new[]
    {
        // AI Chat batch
        new object[] { "/AI/AIProfile/Create",       "/ai/profiles/create",        "AI Profile Create" },
        new object[] { "/AI/AIProfile",              "/ai/profiles",               "AI Profile Index"  },
        new object[] { "/AIChat/AIChat/Test",        "/ai-chat/test",              "AI Chat Test"      },
        new object[] { "/AIChat/ChatExtractedData",  "/ai-chat/extracted-data",    "Chat Extracted Data" },
        new object[] { "/AIChat/ChatAnalytics",      "/ai-chat/analytics",         "Chat Analytics" },
        new object[] { "/AIChat/UsageAnalytics",     "/ai-chat/usage-analytics",   "Usage Analytics" },

        // AI configuration batches
        new object[] { "/AI/AIConnection/Create",    "/ai/connections/create",     "AI Connection Create" },
        new object[] { "/AI/AIConnection",           "/ai/connections",            "AI Connection Index" },
        new object[] { "/AI/AIDeployment/Create",    "/ai/deployments/create",     "AI Deployment Create" },
        new object[] { "/AI/AIDeployment",           "/ai/deployments",            "AI Deployment Index" },
        new object[] { "/AI/AITemplate/Create",      "/ai/templates/create",       "AI Template Create" },
        new object[] { "/AI/AITemplate",             "/ai/templates",              "AI Template Index" },

        // MCP batch
        new object[] { "/Mcp/McpConnection/Create",  "/mcp/connections/create",    "MCP Connection Create" },
        new object[] { "/Mcp/McpConnection",         "/mcp/connections",           "MCP Connection Index" },
        new object[] { "/Mcp/McpPrompt/Create",      "/mcp/prompts/create",        "MCP Prompt Create" },
        new object[] { "/Mcp/McpPrompt",             "/mcp/prompts",               "MCP Prompt Index" },
        new object[] { "/Mcp/McpResource/Create",    "/mcp/resources/create",      "MCP Resource Create" },
        new object[] { "/Mcp/McpResource",           "/mcp/resources",             "MCP Resource Index" },

        // A2A
        new object[] { "/A2A/A2AConnection/Create",  "/a2a/connections/create",    "A2A Connection Create" },
        new object[] { "/A2A/A2AConnection",         "/a2a/connections",           "A2A Connection Index" },

        // DataSources
        new object[] { "/DataSources/AIDataSource/Create", "/datasources/create", "AI DataSource Create" },
        new object[] { "/DataSources/AIDataSource",        "/datasources",        "AI DataSource Index" },

        // Admin
        new object[] { "/Admin/Article/Create",      "/admin/articles/create",     "Admin Article Create" },
        new object[] { "/Admin/Article",             "/admin/articles",            "Admin Article Index" },
        new object[] { "/Admin/Settings",            "/admin/settings",            "Admin Settings" },
    };

    [Theory]
    [MemberData(nameof(ParityPages))]
    public async Task Page_TabLabels_ShouldMatch(string mvcPath, string blazorPath, string description)
    {
        var (mvcPage, blazorPage) = await LoadBothAsync(mvcPath, blazorPath);

        var mvcTabs = await ParityHelpers.GetTabLabelsAsync(mvcPage);
        var blazorTabs = await ParityHelpers.GetTabLabelsAsync(blazorPage);

        ParityHelpers.AssertSameOrdered(mvcTabs, blazorTabs, $"{description}: tab labels");
    }

    [Theory]
    [MemberData(nameof(ParityPages))]
    public async Task Page_FieldLabels_ShouldMatchSet(string mvcPath, string blazorPath, string description)
    {
        var (mvcPage, blazorPage) = await LoadBothAsync(mvcPath, blazorPath);

        var mvcLabels = await ParityHelpers.GetFieldLabelsAsync(mvcPage);
        var blazorLabels = await ParityHelpers.GetFieldLabelsAsync(blazorPage);

        // Labels can be re-ordered for layout reasons inside a tab — match as set.
        ParityHelpers.AssertSameSet(mvcLabels, blazorLabels, $"{description}: form field labels");
    }

    [Theory]
    [MemberData(nameof(ParityPages))]
    public async Task Page_ButtonLabels_ShouldMatchSet(string mvcPath, string blazorPath, string description)
    {
        var (mvcPage, blazorPage) = await LoadBothAsync(mvcPath, blazorPath);

        var mvcButtons = await ParityHelpers.GetButtonLabelsAsync(mvcPage, "main, .container, body");
        var blazorButtons = await ParityHelpers.GetButtonLabelsAsync(blazorPage, "main, .container, body");

        ParityHelpers.AssertSameSet(mvcButtons, blazorButtons, $"{description}: button labels");
    }
}
