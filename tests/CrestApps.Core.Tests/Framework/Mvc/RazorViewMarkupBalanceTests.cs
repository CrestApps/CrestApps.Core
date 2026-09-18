using System.Text.RegularExpressions;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Checks that every Razor view closes as many elements as it opens.
/// </summary>
/// <remarks>
/// <para>
/// An unclosed element in a tabbed form does not look like a markup fault when it happens. Bootstrap hides
/// an inactive pane with <c>.tab-content &gt; .tab-pane</c>, a child selector, so a pane that ends up nested
/// inside its neighbour rather than beside it stops matching and renders at full height. The AI profile
/// editor lost one closing div this way and silently drew two hidden tabs -- about 1,100 pixels of them --
/// inside the Knowledge tab, between the upload controls and the Save button.
/// </para>
/// <para>
/// Nothing else catches it. The view compiles, the page returns 200, every test passes, and the only symptom
/// is empty space that looks like a styling choice.
/// </para>
/// </remarks>
public sealed class RazorViewMarkupBalanceTests
{
    // Void and self-closing elements never carry a closing tag, so they are not counted.
    private static readonly Regex _divOpen = new(@"<div\b", RegexOptions.Compiled);
    private static readonly Regex _divClose = new(@"</div>", RegexOptions.Compiled);
    private static readonly Regex _sectionOpen = new(@"<section\b", RegexOptions.Compiled);
    private static readonly Regex _sectionClose = new(@"</section>", RegexOptions.Compiled);

    [Fact]
    public void EveryRazorView_ClosesEveryDivItOpens()
    {
        var root = GetRepositoryRoot();
        var unbalanced = new List<string>();

        foreach (var view in EnumerateViews(root))
        {
            var content = File.ReadAllText(view);
            var opened = _divOpen.Count(content);
            var closed = _divClose.Count(content);

            if (opened != closed)
            {
                unbalanced.Add($"{Path.GetRelativePath(root, view)}: {opened} <div> vs {closed} </div>");
            }
        }

        Assert.True(unbalanced.Count == 0, "Unbalanced div markup:" + Environment.NewLine + string.Join(Environment.NewLine, unbalanced));
    }

    [Fact]
    public void EveryRazorView_ClosesEverySectionItOpens()
    {
        var root = GetRepositoryRoot();
        var unbalanced = new List<string>();

        foreach (var view in EnumerateViews(root))
        {
            var content = File.ReadAllText(view);

            // @section in Razor is not an element and never appears as "<section".
            var opened = _sectionOpen.Count(content);
            var closed = _sectionClose.Count(content);

            if (opened != closed)
            {
                unbalanced.Add($"{Path.GetRelativePath(root, view)}: {opened} <section> vs {closed} </section>");
            }
        }

        Assert.True(unbalanced.Count == 0, "Unbalanced section markup:" + Environment.NewLine + string.Join(Environment.NewLine, unbalanced));
    }

    private static IEnumerable<string> EnumerateViews(string root)
    {
        foreach (var directory in new[] { "src" })
        {
            var path = Path.Combine(root, directory);

            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.cshtml", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(path, "*.razor", SearchOption.AllDirectories)))
            {
                // Build output holds copies of the same views; counting them reports one fault twice.
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CrestApps.Core.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
