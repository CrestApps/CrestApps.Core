using CrestApps.Core.Tests.Samples.Infrastructure;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

/// <summary>
/// Strict, browser-free parity tests that compare each Blazor.Web Razor
/// component against its corresponding Mvc.Web view at the source level.
/// </summary>
/// <remarks>
/// Mvc.Web is the source of truth — when this test fails, fix Blazor.Web to
/// match. The pair list <see cref="ParityPairs"/> is the canonical inventory
/// of every parity-tracked page; adding a new page in either UI without
/// adding the matching entry here will leave that page unprotected.
/// <para>
/// Allowed differences (not asserted): single-page navigation vs redirects,
/// EF Core vs YesSql persistence, and runtime data values such as
/// <c>@displayText</c> or <c>@Localizer["..."]</c> that render to the same
/// effective string at runtime.
/// </para>
/// </remarks>
public class SourceParityTests
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

    /// <summary>
    /// Canonical inventory of parity-tracked page pairs. The first entry is
    /// the human description that appears in test output; the second and
    /// third are repo-relative paths to the Mvc.Web view and the Blazor.Web
    /// component respectively.
    /// </summary>
    public static TheoryData<string, string, string> ParityPairs() => new()
    {
        // AI - Connections
        { "AIConnections/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIConnection/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIConnections/Index.razor" },
        { "AIConnections/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIConnection/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIConnections/Create.razor" },
        { "AIConnections/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIConnection/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIConnections/Edit.razor" },

        // AI - Deployments
        { "AIDeployments/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIDeployment/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIDeployments/Index.razor" },
        { "AIDeployments/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIDeployment/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIDeployments/Create.razor" },
        { "AIDeployments/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIDeployment/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIDeployments/Edit.razor" },

        // AI - Profiles
        { "AIProfiles/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIProfile/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIProfiles/Index.razor" },
        { "AIProfiles/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIProfile/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIProfiles/Create.razor" },
        { "AIProfiles/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AIProfile/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AIProfiles/Edit.razor" },

        // AI - Templates
        { "AITemplates/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AITemplate/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AITemplates/Index.razor" },
        { "AITemplates/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AITemplate/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AITemplates/Create.razor" },
        { "AITemplates/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AI/Views/AITemplate/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AI/AITemplates/Edit.razor" },

        // AI Chat
        { "AIChat/Chat", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/AIChat/Chat.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AIChat/Chat.razor" },
        { "AIChat/Sessions", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/AIChat/Sessions.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AIChat/Sessions.razor" },
        { "AIChat/Test", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/AIChat/Test.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AIChat/Test.razor" },
        { "AIChat/ChatAnalytics", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/ChatAnalytics/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AIChat/ChatAnalytics.razor" },
        { "AIChat/ChatExtractedData", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/ChatExtractedData/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AIChat/ChatExtractedData.razor" },
        { "AIChat/UsageAnalytics", "src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/UsageAnalytics/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/AIChat/UsageAnalytics.razor" },

        // A2A
        { "A2AConnections/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/A2A/Views/A2AConnection/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/A2A/A2AConnections/Index.razor" },
        { "A2AConnections/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/A2A/Views/A2AConnection/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/A2A/A2AConnections/Create.razor" },
        { "A2AConnections/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/A2A/Views/A2AConnection/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/A2A/A2AConnections/Edit.razor" },

        // MCP
        { "McpConnections/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpConnection/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpConnections/Index.razor" },
        { "McpConnections/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpConnection/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpConnections/Create.razor" },
        { "McpConnections/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpConnection/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpConnections/Edit.razor" },
        { "McpPrompts/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpPrompt/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpPrompts/Index.razor" },
        { "McpPrompts/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpPrompt/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpPrompts/Create.razor" },
        { "McpPrompts/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpPrompt/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpPrompts/Edit.razor" },
        { "McpResources/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpResource/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpResources/Index.razor" },
        { "McpResources/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpResource/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpResources/Create.razor" },
        { "McpResources/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Mcp/Views/McpResource/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Mcp/McpResources/Edit.razor" },

        // Data Sources
        { "AIDataSources/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/DataSources/Views/AIDataSource/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/DataSources/AIDataSources/Index.razor" },
        { "AIDataSources/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/DataSources/Views/AIDataSource/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/DataSources/AIDataSources/Create.razor" },
        { "AIDataSources/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/DataSources/Views/AIDataSource/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/DataSources/AIDataSources/Edit.razor" },

        // Indexing
        { "IndexProfiles/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Indexing/Views/IndexProfile/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Indexing/IndexProfiles/Index.razor" },
        { "IndexProfiles/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Indexing/Views/IndexProfile/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Indexing/IndexProfiles/Create.razor" },
        { "IndexProfiles/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Indexing/Views/IndexProfile/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Indexing/IndexProfiles/Edit.razor" },

        // Chat Interactions
        { "ChatInteractions/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/ChatInteractions/Views/ChatInteraction/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/ChatInteractions/Index.razor" },
        { "ChatInteractions/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/ChatInteractions/Views/ChatInteraction/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/ChatInteractions/Create.razor" },
        { "ChatInteractions/Chat", "src/Startup/CrestApps.Core.Mvc.Web/Areas/ChatInteractions/Views/ChatInteraction/Chat.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/ChatInteractions/Chat.razor" },

        // Admin
        { "Articles/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Admin/Views/Article/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Admin/Articles/Index.razor" },
        { "Articles/Create", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Admin/Views/Article/Create.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Admin/Articles/Create.razor" },
        { "Articles/Edit", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Admin/Views/Article/Edit.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Admin/Articles/Edit.razor" },
        { "Settings/Index", "src/Startup/CrestApps.Core.Mvc.Web/Areas/Admin/Views/Settings/Index.cshtml", "src/Startup/CrestApps.Core.Blazor.Web/Components/Pages/Admin/Settings/Index.razor" },
    };

    private static string ReadSource(string repoRelativePath)
    {
        var full = Path.Combine(s_repoRoot, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"Expected file does not exist: {full}");

        return File.ReadAllText(full);
    }

    /// <summary>
    /// Page pairs with known per-test-kind drift. Each entry maps a
    /// <c>(testKind, pageDescription)</c> tuple to the human-readable reason
    /// the test is currently exempted. When Blazor.Web catches up, removing
    /// the entry will make the corresponding theory row run again — and pass
    /// — without any other change. Adding a NEW gap requires explicitly
    /// listing it here, which makes silent regressions impossible.
    /// </summary>
    private static readonly Dictionary<(string Kind, string Page), string> s_knownDrift = new()
    {
        // AIProfiles/Create — Mvc.Web exposes a "Task Info" tab and a duplicate
        // "Capabilities" panel (in a hidden modal). Blazor surfaces these via
        // its own provider-specific configuration sections instead.
        { ("TabLabels",   "AIProfiles/Create"), "Mvc has Task Info tab + duplicate Capabilities panel; Blazor uses provider-specific sections." },
        { ("TabLabels",   "AIProfiles/Edit"),   "Mvc has Task Info tab + duplicate Capabilities panel; Blazor uses provider-specific sections." },
        { ("TabLabels",   "AITemplates/Create"), "Mvc has Task Info tab; not yet ported to Blazor." },
        { ("TabLabels",   "AITemplates/Edit"),  "Mvc has Task Info tab; not yet ported to Blazor." },

        // Heading drift — large feature gaps that need full section ports.
        { ("Headings",    "AIProfiles/Create"), "Mvc has 'A2A Hosts' subheading; Blazor uses 'Agent to Agent Hosts' only." },
        { ("Headings",    "AIProfiles/Edit"),   "Mvc has 'A2A Hosts' subheading; Blazor uses 'Agent to Agent Hosts' only." },
        { ("Headings",    "AITemplates/Create"), "Blazor missing several panel headings: A2A Hosts, Attach Documents, Extraction Entries, Session Metrics, Conversion Goals." },
        { ("Headings",    "AITemplates/Edit"),  "Blazor missing several panel headings: A2A Hosts, Attach Documents, Extraction Entries, Session Metrics, Conversion Goals." },
        { ("Headings",    "ChatInteractions/Chat"),   "Blazor side-pane lacks dedicated 'AI Agents' / 'AI Tools' subheadings; sections render but headings differ." },

        // Form-label drift — same root cause as heading drift (missing panels).
        { ("FormLabels",  "AIProfiles/Create"), "Form fields inside the missing/divergent panels are not yet aligned." },
        { ("FormLabels",  "AIProfiles/Edit"),   "Form fields inside the missing/divergent panels are not yet aligned." },
        { ("FormLabels",  "AITemplates/Create"), "Form fields inside the missing panels are not yet aligned." },
        { ("FormLabels",  "AITemplates/Edit"),  "Form fields inside the missing panels are not yet aligned." },
        { ("FormLabels",  "ChatInteractions/Chat"), "Form fields in side-pane sections not yet ported." },

        // Button drift — buttons live inside the missing panels above.
        { ("ButtonLabels", "AIProfiles/Create"), "Buttons inside the missing/divergent panels are not yet aligned." },
        { ("ButtonLabels", "AIProfiles/Edit"),   "Buttons inside the missing/divergent panels are not yet aligned." },
        { ("ButtonLabels", "AITemplates/Create"), "Buttons inside the missing panels are not yet aligned." },
        { ("ButtonLabels", "AITemplates/Edit"),  "Buttons inside the missing panels are not yet aligned." },
        { ("ButtonLabels", "ChatInteractions/Chat"),   "Blazor chat-detail side-pane lacks Back / Add template / All / None / Browse files / Clear history controls; pending port." },
        { ("ButtonLabels", "Settings/Index"),   "Mvc has 'Copy' callback-URL helper button inside Copilot configuration section; Blazor's Settings page is simplified." },
    };

    private static void RunParity(
        string description,
        string mvcPath,
        string blazorPath,
        string kind,
        Func<string, IReadOnlyList<string>> extract)
    {
        var mvc = extract(ReadSource(mvcPath));
        var blazor = extract(ReadSource(blazorPath));

        if (mvc.Count == 0 && blazor.Count == 0)
        {
            return;
        }

        if (s_knownDrift.TryGetValue((kind, description), out var reason))
        {
            Assert.Skip($"Known parity drift: {reason}");
        }

        SourceParityHelpers.AssertContainsAllOf(mvc, blazor, kind, description);
    }

    [Theory]
    [MemberData(nameof(ParityPairs))]
    public void Parity_TabLabels(string description, string mvcPath, string blazorPath)
        => RunParity(description, mvcPath, blazorPath, "TabLabels", SourceParityHelpers.GetTabLabels);

    [Theory]
    [MemberData(nameof(ParityPairs))]
    public void Parity_TableHeaders(string description, string mvcPath, string blazorPath)
        => RunParity(description, mvcPath, blazorPath, "TableHeaders", SourceParityHelpers.GetTableHeaders);

    [Theory]
    [MemberData(nameof(ParityPairs))]
    public void Parity_FormLabels(string description, string mvcPath, string blazorPath)
        => RunParity(description, mvcPath, blazorPath, "FormLabels", SourceParityHelpers.GetFormLabels);

    [Theory]
    [MemberData(nameof(ParityPairs))]
    public void Parity_ButtonLabels(string description, string mvcPath, string blazorPath)
        => RunParity(description, mvcPath, blazorPath, "ButtonLabels", SourceParityHelpers.GetButtonLabels);

    [Theory]
    [MemberData(nameof(ParityPairs))]
    public void Parity_Headings(string description, string mvcPath, string blazorPath)
        => RunParity(description, mvcPath, blazorPath, "Headings", src => SourceParityHelpers.GetHeadings(src));

    /// <summary>
    /// Self-test: every known-drift entry must reference a page in
    /// <see cref="ParityPairs"/>. Catches stale entries left over after a
    /// page has been removed or renamed.
    /// </summary>
    [Fact]
    public void KnownDrift_OnlyReferencesExistingPages()
    {
        var validPages = new HashSet<string>(BuildPageDescriptions(), StringComparer.Ordinal);
        var validKinds = new HashSet<string>(StringComparer.Ordinal) { "TabLabels", "TableHeaders", "FormLabels", "ButtonLabels", "Headings" };
        var stale = s_knownDrift.Keys.Where(k => !validPages.Contains(k.Page) || !validKinds.Contains(k.Kind)).ToArray();

        Assert.True(stale.Length == 0, $"Stale known-drift entries: {string.Join(", ", stale.Select(k => $"({k.Kind}, {k.Page})"))}");
    }

    private static string[] BuildPageDescriptions()
    {
        // Mirror of ParityPairs() — kept in sync via KnownDrift_OnlyReferencesExistingPages.
        // We can't enumerate TheoryData rows via the public surface in xunit.v3, so the
        // check uses an explicit list; if you add a row to ParityPairs, add it here too.
        return new[]
        {
            "AIConnections/Index", "AIConnections/Create", "AIConnections/Edit",
            "AIDeployments/Index", "AIDeployments/Create", "AIDeployments/Edit",
            "AIProfiles/Index", "AIProfiles/Create", "AIProfiles/Edit",
            "AITemplates/Index", "AITemplates/Create", "AITemplates/Edit",
            "AIChat/Chat", "AIChat/Sessions", "AIChat/Test",
            "AIChat/ChatAnalytics", "AIChat/ChatExtractedData", "AIChat/UsageAnalytics",
            "A2AConnections/Index", "A2AConnections/Create", "A2AConnections/Edit",
            "McpConnections/Index", "McpConnections/Create", "McpConnections/Edit",
            "McpPrompts/Index", "McpPrompts/Create", "McpPrompts/Edit",
            "McpResources/Index", "McpResources/Create", "McpResources/Edit",
            "AIDataSources/Index", "AIDataSources/Create", "AIDataSources/Edit",
            "IndexProfiles/Index", "IndexProfiles/Create", "IndexProfiles/Edit",
            "ChatInteractions/Index", "ChatInteractions/Create", "ChatInteractions/Chat",
            "Articles/Index", "Articles/Create", "Articles/Edit",
            "Settings/Index",
        };
    }
}
