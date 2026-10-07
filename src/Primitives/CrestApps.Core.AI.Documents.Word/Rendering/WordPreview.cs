using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Workspace;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// What the page-drawing tools share: a document laid out the way it will export, pages drawn as pictures and
/// shown in the conversation, and the text of a page for hosts that cannot show pictures.
/// </summary>
internal static class WordPreview
{
    private const string CacheKey = nameof(WordPreview) + ".Layouts";

    /// <summary>
    /// Opens a document, refreshes its table of contents, captions and references the way an export does, and
    /// lays it out. A document laid out earlier in the same turn is not laid out again.
    /// </summary>
    /// <param name="context">The Word context.</param>
    /// <param name="source">The document.</param>
    /// <param name="showMarkup">Whether tracked changes are drawn as markup.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The layout.</returns>
    public static async Task<WordLayout> LayoutAsync(WordToolContext context, WordSource source, bool showMarkup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);

        var key = source.Key + (showMarkup ? "|markup" : string.Empty);
        var cache = Cache(AIInvocationScope.Current);

        if (cache is not null && cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var package = await context.OpenAsync(source, cancellationToken);

        var layout = Layout(package, context.Services, showMarkup);

        if (cache is not null)
        {
            cache[key] = layout;
        }

        return layout;
    }

    // The layouts of this invocation. Tool calls of one invocation may run side by side, so the cache is safe to
    // share and is added to the invocation's items under a lock.
    private static ConcurrentDictionary<string, WordLayout> Cache(AIInvocationContext invocation)
    {
        if (invocation is null)
        {
            return null;
        }

        lock (invocation.Items)
        {
            if (invocation.Items.TryGetValue(CacheKey, out var value) && value is ConcurrentDictionary<string, WordLayout> cache)
            {
                return cache;
            }

            cache = new ConcurrentDictionary<string, WordLayout>(StringComparer.Ordinal);
            invocation.Items[CacheKey] = cache;

            return cache;
        }
    }

    /// <summary>
    /// Refreshes and lays out an open document.
    /// </summary>
    /// <param name="package">The document; it is refreshed in place.</param>
    /// <param name="services">The request services.</param>
    /// <param name="showMarkup">Whether tracked changes are drawn as markup.</param>
    /// <returns>The layout.</returns>
    public static WordLayout Layout(WordPackage package, IServiceProvider services, bool showMarkup = false)
    {
        WordDocumentRefresher.Refresh(package, services);

        var options = WordLayoutOptions.From(Options(services));

        options.ShowMarkup = showMarkup;

        return WordLayoutEngine.Layout(package, options);
    }

    /// <summary>
    /// Gets the preview options.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <returns>The options.</returns>
    public static WordPreviewOptions Options(IServiceProvider services)
    {
        return services?.GetService<IOptions<WordPreviewOptions>>()?.Value ?? new WordPreviewOptions();
    }

    /// <summary>
    /// Draws pages and shows them in the conversation.
    /// </summary>
    /// <param name="context">The Word context.</param>
    /// <param name="source">The document.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="pages">The one-based pages to draw.</param>
    /// <param name="pixelWidth">The width each page is drawn at.</param>
    /// <param name="highlight">An element to outline, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One <c>[fig:N]</c> marker per page, or <see langword="null"/> when this host cannot show pictures here.</returns>
    public static Task<List<string>> ShowAsync(
        WordToolContext context,
        WordSource source,
        WordLayout layout,
        IReadOnlyList<int> pages,
        int pixelWidth,
        DocumentFormat.OpenXml.OpenXmlElement highlight,
        CancellationToken cancellationToken)
    {
        var safeName = WordToolContext.SanitizeName(source.Name).Replace(' ', '_');
        var bounds = highlight is null ? [] : layout.BoundsOf(highlight);
        var figures = pages
            .Select(number => layout.Pages[number - 1])
            .Select(page => new WordFigure(
                string.Create(CultureInfo.InvariantCulture, $"{source.Name} — page {page.Index} of {layout.PageCount}"),
                string.Create(CultureInfo.InvariantCulture, $"{safeName}-page-{page.Index}.svg"),
                Encoding.UTF8.GetBytes(WordSvgWriter.Write(page, pixelWidth, bounds.Where(box => box.Page == page.Index).Select(box => (box.X, box.Y, box.Width, box.Height))))))
            .ToList();

        return context.ShowFiguresAsync(figures, cancellationToken);
    }

    /// <summary>
    /// Writes the text of a page, line by line, for a host that cannot show the picture.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The text.</returns>
    public static string PageText(WordLayoutPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var lines = page.Items.OfType<WordTextItem>()
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => Math.Round(item.Baseline / 3))
            .OrderBy(group => group.Key)
            .Select(group => string.Join(' ', group.OrderBy(item => item.X).Select(item => item.Text.Trim())));

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Writes the instruction that makes the model show the pictures, and what each one shows.
    /// </summary>
    /// <param name="markers">The figure markers.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="pages">The pages drawn.</param>
    /// <param name="description">What document the pages are of.</param>
    /// <returns>The answer.</returns>
    public static StringBuilder DescribeShown(IReadOnlyList<string> markers, WordLayout layout, IReadOnlyList<int> pages, string description)
    {
        var builder = new StringBuilder();

        builder
            .AppendLine("WRITE THE FOLLOWING LINE IN YOUR ANSWER, EXACTLY AS SHOWN, ON A LINE OF ITS OWN:")
            .AppendLine()
            .AppendLine(string.Join(' ', markers))
            .AppendLine()
            .AppendLine("The markers are placeholders the host replaces with the page pictures; the user sees nothing unless they appear in your answer character for character. Do not describe them as \"shown above\" instead of writing them.")
            .AppendLine()
            .Append("What each picture shows (").Append(description).AppendLine("), for your own wording only:");

        for (var index = 0; index < pages.Count; index++)
        {
            var page = layout.Pages[pages[index] - 1];

            builder.Append(index + 1).Append(". Page ").Append(page.Index).Append(" of ").Append(layout.PageCount);

            if (!string.Equals(page.DisplayNumber, page.Index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                builder.Append(" (numbered ").Append(page.DisplayNumber).Append(')');
            }

            builder.Append(", ").Append(WordPageSizes.Describe(page.Width, page.Height)).AppendLine(".");
        }

        return builder;
    }
}
