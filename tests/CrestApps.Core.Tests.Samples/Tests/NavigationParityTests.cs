using System.Text.RegularExpressions;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Tests;

/// <summary>
/// Compares the admin sidebar of the two sample hosts, entry by entry.
/// </summary>
/// <remarks>
/// The sidebars are hand-written and fully duplicated: no shared partial, no nav model, no registry. They
/// agree today only because both were edited together, and nothing noticed that they had to be. A regrouping
/// applied to one host and not the other would ship silently — the pages themselves are covered by
/// <see cref="SourceParityTests"/>, but the way anybody reaches them is not.
/// <para>
/// The two files cannot be diffed against each other: the MVC layout also carries the top navbar, the
/// validation alert and the chat-widget include, and the two use different routing syntax. So this names what
/// it compares — the ordered sequence of section headings, section labels and navigation entries inside the
/// sidebar, each with its icon — rather than comparing the files. Where an entry points is deliberately not
/// compared, because an Mvc area and a Blazor route are different by design.
/// </para>
/// <para>
/// Mvc.Web is the source of truth, as in the rest of this suite: when this fails, fix Blazor.Web to match.
/// </para>
/// </remarks>
public class NavigationParityTests
{
    private const string MvcLayout = "src/Startup/CrestApps.Core.Mvc.Web/Views/Shared/_Layout.cshtml";
    private const string BlazorNavMenu = "src/Startup/CrestApps.Core.Blazor.Web/Components/Layout/NavMenu.razor";

    private static readonly string s_repoRoot = LocateRepoRoot();

    [Fact]
    public void Sidebars_ListTheSameEntriesInTheSameOrder()
    {
        var mvc = ReadSidebarEntries(MvcLayout);
        var blazor = ReadSidebarEntries(BlazorNavMenu);

        // Asserted as one sequence rather than entry by entry, so a failure prints both lists and the
        // difference is read rather than deduced.
        Assert.Equal(mvc, blazor);
    }

    [Fact]
    public void Sidebars_AreNotEmpty()
    {
        // The comparison above passes trivially if the extraction silently matches nothing, which is exactly
        // what a markup change would cause.
        var mvc = ReadSidebarEntries(MvcLayout);

        Assert.True(mvc.Count > 10, $"Only {mvc.Count} sidebar entries were found in {MvcLayout}; the extraction is probably no longer reading the markup.");
        Assert.Contains("section:RAG", mvc);
        Assert.Contains("link:fa-solid fa-folder-tree:File Sources", mvc);
    }

    /// <summary>
    /// Reads the sidebar's ordered entries: its headings, its section labels and its links with their icons.
    /// </summary>
    /// <param name="relativePath">The repo-relative path of the layout.</param>
    /// <returns>One string per entry, in document order.</returns>
    private static List<string> ReadSidebarEntries(string relativePath)
    {
        var path = Path.Combine(s_repoRoot, relativePath);

        Assert.True(File.Exists(path), $"{relativePath} does not exist.");

        var markup = File.ReadAllText(path);
        var sidebar = ExtractSidebar(markup, relativePath);
        var entries = new List<string>();

        // One pass in document order, so ordering differences are caught rather than sorted away. A link is
        // matched for either host's syntax, since <a asp-area> and <NavLink href> are the same entry to a
        // reader and differ only in how the host routes.
        var pattern = new Regex(
            """
            <h6\b[^>]*>(?<heading>[^<]*)</h6>
            | <span\b[^>]*class="nav-link[^"]*disabled[^"]*"[^>]*>(?<section>[^<]*)</span>
            | <(?:a|NavLink)\b[^>]*class="nav-link"[^>]*>\s*<i\b[^>]*class="(?<icon>[^"]*)"[^>]*></i>\s*(?<label>[^<]*?)\s*</(?:a|NavLink)>
            """,
            RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline);

        foreach (Match match in pattern.Matches(sidebar))
        {
            if (match.Groups["heading"].Success)
            {
                entries.Add($"heading:{Collapse(match.Groups["heading"].Value)}");
            }
            else if (match.Groups["section"].Success)
            {
                entries.Add($"section:{Collapse(match.Groups["section"].Value)}");
            }
            else if (match.Groups["label"].Success)
            {
                entries.Add($"link:{Collapse(match.Groups["icon"].Value)}:{Collapse(match.Groups["label"].Value)}");
            }
        }

        return entries;
    }

    /// <summary>
    /// Returns the sidebar element's markup, so the MVC layout's navbar and widgets are not read as
    /// navigation.
    /// </summary>
    /// <param name="markup">The whole file.</param>
    /// <param name="relativePath">The repo-relative path, for the failure message.</param>
    /// <returns>The markup between the sidebar's opening tag and the next closing nav tag.</returns>
    private static string ExtractSidebar(string markup, string relativePath)
    {
        var start = markup.IndexOf("<nav id=\"sidebar\"", StringComparison.Ordinal);

        Assert.True(start >= 0, $"{relativePath} has no element with id \"sidebar\"; this test can no longer find the navigation.");

        var end = markup.IndexOf("</nav>", start, StringComparison.Ordinal);

        Assert.True(end > start, $"{relativePath} has an unclosed sidebar element.");

        return markup[start..end];
    }

    private static string Collapse(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    }

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
}
