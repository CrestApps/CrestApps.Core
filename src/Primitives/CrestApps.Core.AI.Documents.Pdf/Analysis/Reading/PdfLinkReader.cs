using CrestApps.Core.AI.Documents.Pdf.Rendering;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Reads the links a PDF's pages carry — web addresses, jumps to other pages, named destinations and
/// actions — and checks them without ever following one.
/// </summary>
/// <remarks>
/// Nothing here makes a network request. A web address is checked for being well formed and for using a
/// scheme that is safe to click; whether the page behind it answers is not something a reading tool finds
/// out.
/// </remarks>
internal sealed class PdfLinkReader
{
    private readonly PdfDocument _pdf;
    private Dictionary<IndirectReference, int> _pageNumbers;
    private Dictionary<string, IToken> _namedDestinations;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLinkReader"/> class.
    /// </summary>
    /// <param name="pdf">The document.</param>
    public PdfLinkReader(PdfDocument pdf)
    {
        _pdf = pdf;
    }

    /// <summary>
    /// Gets the destinations the document names, with the page each one points at.
    /// </summary>
    /// <returns>The page of each named destination, or <see langword="null"/> for one that does not resolve.</returns>
    public Dictionary<string, int?> ReadNamedDestinations()
    {
        var destinations = new Dictionary<string, int?>(StringComparer.Ordinal);

        foreach (var (name, token) in NamedDestinations)
        {
            destinations[name] = PageOfDestination(token, 0);
        }

        return destinations;
    }

    /// <summary>
    /// Reads the links on a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="words">The page's words, which give each link its text.</param>
    /// <returns>The links, in the order the page lists them.</returns>
    public List<PdfLinkEntry> Read(Page page, List<Word> words)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(words);

        var links = new List<PdfLinkEntry>();
        List<Annotation> annotations;

        try
        {
            annotations = [.. page.GetAnnotations()];
        }
        catch (Exception)
        {
            annotations = [];
        }

        foreach (var annotation in annotations)
        {
            if (annotation.Type != AnnotationType.Link)
            {
                continue;
            }

            var box = PdfBox.From(annotation.Rectangle);

            links.Add(Describe(page, annotation, box, TextUnder(box, words)));
        }

        List<Hyperlink> hyperlinks;

        try
        {
            hyperlinks = [.. page.GetHyperlinks()];
        }
        catch (Exception)
        {
            hyperlinks = [];
        }

        // PdfPig's own reading of the page's web links, for any the annotations above did not yield.
        foreach (var hyperlink in hyperlinks)
        {
            var box = PdfBox.From(hyperlink.Bounds);

            if (links.Exists(link => string.Equals(link.Target, hyperlink.Uri, StringComparison.Ordinal) && link.Box.Intersects(box.Inflate(1))))
            {
                continue;
            }

            links.Add(new PdfLinkEntry
            {
                Page = page.Number,
                Kind = KindOfUri(hyperlink.Uri),
                Target = hyperlink.Uri,
                Text = TextUnder(box, words),
                Box = box,
            });
        }

        return links;
    }

    /// <summary>
    /// Checks a link without following it.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="pageCount">The number of pages the document has.</param>
    /// <param name="visible">The visible area of the link's page.</param>
    public static void Validate(PdfLinkEntry link, int pageCount, PdfBox visible)
    {
        ArgumentNullException.ThrowIfNull(link);

        switch (link.Kind)
        {
            case PdfLinkEntry.WebKind:
            case PdfLinkEntry.EmailKind:
                (link.Status, link.Note) = ValidateUri(link.Target, link.Text);

                break;

            case PdfLinkEntry.InternalKind:
            case PdfLinkEntry.NamedActionKind:
                if (link.TargetPage is int target && target >= 1 && target <= pageCount)
                {
                    (link.Status, link.Note) = ("ok", null);
                }
                else if (link.Kind == PdfLinkEntry.NamedActionKind && link.TargetPage is null)
                {
                    (link.Status, link.Note) = ("ok", "a viewer command");
                }
                else
                {
                    (link.Status, link.Note) = link.TargetPage is int missing
                        ? ("broken", $"page {missing} does not exist; the document has {pageCount} page(s)")
                        : ("broken", "the destination does not lead to any page of this document");
                }

                break;

            case PdfLinkEntry.ScriptKind:
                (link.Status, link.Note) = ("risky", "runs a script when clicked");

                break;

            case PdfLinkEntry.LaunchKind:
                (link.Status, link.Note) = ("risky", "opens a file or runs a program when clicked");

                break;

            case PdfLinkEntry.OtherFileKind:
                (link.Status, link.Note) = ("unusual", "opens another file, which cannot be checked from here");

                break;

            case PdfLinkEntry.EmbeddedFileKind:
                (link.Status, link.Note) = ("ok", "opens a file embedded in this document");

                break;

            default:
                (link.Status, link.Note) = ("unusual", "an action other than a link");

                break;
        }

        if (!link.Box.Intersects(visible))
        {
            link.Note = string.IsNullOrEmpty(link.Note)
                ? "the link area lies outside the visible page"
                : link.Note + "; the link area lies outside the visible page";
        }
    }

    /// <summary>
    /// Checks that an address is well formed and uses a scheme that is safe to click.
    /// </summary>
    /// <param name="target">The address.</param>
    /// <param name="text">The text the link shows, or <see langword="null"/>.</param>
    /// <returns>The status — <c>ok</c>, <c>risky</c>, <c>invalid</c> or <c>unusual</c> — and why.</returns>
    public static (string Status, string Note) ValidateUri(string target, string text)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return ("invalid", "the address is empty");
        }

        var trimmed = target.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? ("invalid", "the address has no scheme; it needs http:// or https://")
                : ("invalid", "not a complete address");
        }

        switch (uri.Scheme.ToLowerInvariant())
        {
            case "http":
            case "https":
                if (string.IsNullOrEmpty(uri.Host))
                {
                    return ("invalid", "the address names no host");
                }

                if (TryReadShownHost(text, out var shown) && !SameHost(shown, uri.Host))
                {
                    return ("risky", $"the text shows {shown} but the link goes to {uri.Host}");
                }

                return uri.Scheme == Uri.UriSchemeHttp
                    ? ("ok", "not encrypted (http)")
                    : ("ok", null);

            case "mailto":
                return trimmed.Contains('@', StringComparison.Ordinal)
                    ? ("ok", null)
                    : ("invalid", "the email address has no @");

            case "ftp":
            case "ftps":
            case "tel":
            case "sms":
            case "news":
                return ("ok", null);

            case "javascript":
            case "vbscript":
                return ("risky", "runs a script when clicked");

            case "file":
                return ("risky", "opens a file on the reader's computer");

            case "data":
                return ("risky", "carries its content in the link itself");

            default:
                return ("unusual", $"an uncommon scheme ({uri.Scheme}:)");
        }
    }

    private Dictionary<IndirectReference, int> PageNumbers => _pageNumbers ??= BuildPageNumbers();

    private Dictionary<string, IToken> NamedDestinations => _namedDestinations ??= BuildNamedDestinations();

    private PdfLinkEntry Describe(Page page, Annotation annotation, PdfBox box, string text)
    {
        string kind;
        string target = null;
        int? targetPage = null;

        switch (annotation.Action)
        {
            case UriAction uri:
                kind = KindOfUri(uri.Uri);
                target = uri.Uri;

                break;

            case GoToAction goTo:
                kind = PdfLinkEntry.InternalKind;
                targetPage = goTo.Destination?.PageNumber > 0
                    ? goTo.Destination.PageNumber
                    : PageOfAction(annotation.AnnotationDictionary);
                target = NameOfDestination(annotation.AnnotationDictionary);

                break;

            case GoToRAction remote:
                kind = PdfLinkEntry.OtherFileKind;
                target = remote.Filename;

                break;

            case GoToEAction embedded:
                kind = PdfLinkEntry.EmbeddedFileKind;
                target = embedded.FileSpecification;

                break;

            case LaunchAction launch:
                kind = PdfLinkEntry.LaunchKind;
                target = launch.FileName;

                break;

            case JavaScriptAction script:
                kind = PdfLinkEntry.ScriptKind;
                target = PdfTextPatterns.Truncate(PdfTextPatterns.OneLine(script.JavaScript), 120);

                break;

            case NamedAction named:
                kind = PdfLinkEntry.NamedActionKind;
                target = named.Name;
                targetPage = named.Name switch
                {
                    "NextPage" => page.Number + 1,
                    "PrevPage" => page.Number - 1,
                    "FirstPage" => 1,
                    "LastPage" => _pdf.NumberOfPages,
                    _ => null,
                };

                break;

            case null:
                {
                    // A link with a destination and no action: /Dest, which PdfPig may not have resolved.
                    var dictionary = annotation.AnnotationDictionary;

                    if (dictionary is not null && dictionary.TryGet(NameToken.Create("Dest"), out var destination))
                    {
                        kind = PdfLinkEntry.InternalKind;
                        targetPage = PageOfDestination(destination, 0);
                        target = NameOfDestination(dictionary);
                    }
                    else
                    {
                        kind = PdfLinkEntry.OtherKind;
                    }

                    break;
                }

            default:
                kind = PdfLinkEntry.OtherKind;
                target = annotation.Action.Type.ToString();

                break;
        }

        return new PdfLinkEntry
        {
            Page = page.Number,
            Kind = kind,
            Text = text,
            Target = target,
            TargetPage = targetPage,
            Box = box,
        };
    }

    private static string KindOfUri(string uri)
    {
        if (uri is null)
        {
            return PdfLinkEntry.WebKind;
        }

        var trimmed = uri.TrimStart();

        if (trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return PdfLinkEntry.EmailKind;
        }

        return trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            ? PdfLinkEntry.ScriptKind
            : PdfLinkEntry.WebKind;
    }

    private static string TextUnder(PdfBox box, List<Word> words)
    {
        var area = box.Inflate(1.5);

        // Words of one line share a baseline, not a top: a word without ascenders stands lower.
        var inside = words
            .Where(word => !string.IsNullOrWhiteSpace(word.Text) && word.Letters.Count > 0 && PdfBoxes.CenterInside(PdfBox.From(word.BoundingBox), area))
            .OrderByDescending(word => Math.Round(word.Letters[0].StartBaseLine.Y / 2))
            .ThenBy(word => word.BoundingBox.Left)
            .Select(word => word.Text);

        return PdfTextPatterns.Truncate(string.Join(' ', inside), 150);
    }

    private static bool TryReadShownHost(string text, out string host)
    {
        host = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim().Split(' ')[0].TrimEnd('.', ',', ';', ')');

        if (candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "https://" + candidate;
        }

        if (!candidate.Contains("://", StringComparison.Ordinal) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var shown) ||
            string.IsNullOrEmpty(shown.Host))
        {
            return false;
        }

        host = shown.Host;

        return true;
    }

    private static bool SameHost(string first, string second)
    {
        static string Bare(string host)
        {
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? host[4..]
                : host;
        }

        return string.Equals(Bare(first), Bare(second), StringComparison.OrdinalIgnoreCase);
    }

    private int? PageOfAction(DictionaryToken annotation)
    {
        var action = PdfPigTokens.GetDictionary(_pdf, annotation, "A");

        return action is not null && action.TryGet(NameToken.Create("D"), out var destination)
            ? PageOfDestination(destination, 0)
            : null;
    }

    private string NameOfDestination(DictionaryToken annotation)
    {
        if (annotation is null)
        {
            return null;
        }

        var name = PdfPigTokens.GetText(_pdf, annotation, "Dest");

        if (name is null)
        {
            var action = PdfPigTokens.GetDictionary(_pdf, annotation, "A");

            name = PdfPigTokens.GetText(_pdf, action, "D");
        }

        return string.IsNullOrEmpty(name)
            ? null
            : "#" + name;
    }

    private int? PageOfDestination(IToken token, int depth)
    {
        if (depth > 4)
        {
            return null;
        }

        switch (PdfPigTokens.Resolve(_pdf, token))
        {
            case ArrayToken { Data.Count: > 0 } array:
                if (array.Data[0] is IndirectReferenceToken reference && PageNumbers.TryGetValue(reference.Data, out var number))
                {
                    return number;
                }

                // A destination in another file counts its pages from zero.
                return array.Data[0] is NumericToken index
                    ? index.Int + 1
                    : null;

            case DictionaryToken dictionary when dictionary.TryGet(NameToken.Create("D"), out var inner):
                return PageOfDestination(inner, depth + 1);

            case NameToken name:
                return PageOfNamed(name.Data, depth);

            case StringToken text:
                return PageOfNamed(text.Data, depth);

            case HexToken hex:
                return PageOfNamed(hex.Data, depth);

            default:
                return null;
        }
    }

    private int? PageOfNamed(string name, int depth)
    {
        return name is not null && NamedDestinations.TryGetValue(name, out var destination)
            ? PageOfDestination(destination, depth + 1)
            : null;
    }

    private Dictionary<IndirectReference, int> BuildPageNumbers()
    {
        var numbers = new Dictionary<IndirectReference, int>();

        try
        {
            var catalog = _pdf.Structure.Catalog.CatalogDictionary;

            if (catalog.TryGet(NameToken.Create("Pages"), out var root))
            {
                WalkPages(root, numbers, [], 0);
            }
        }
        catch (Exception)
        {
            // A page tree that cannot be walked leaves internal links unresolved, not the document unread.
        }

        return numbers;
    }

    private void WalkPages(IToken token, Dictionary<IndirectReference, int> numbers, HashSet<IndirectReference> visited, int depth)
    {
        if (depth > 64 || numbers.Count >= _pdf.NumberOfPages)
        {
            return;
        }

        IndirectReference? reference = null;

        if (token is IndirectReferenceToken indirect)
        {
            reference = indirect.Data;
        }

        if (reference is { } key && !visited.Add(key))
        {
            return;
        }

        if (PdfPigTokens.Resolve(_pdf, token) is not DictionaryToken node)
        {
            return;
        }

        var type = PdfPigTokens.GetText(_pdf, node, "Type");

        if (!string.Equals(type, "Page", StringComparison.Ordinal) &&
            node.TryGet(NameToken.Create("Kids"), out var kidsToken) &&
            PdfPigTokens.Resolve(_pdf, kidsToken) is ArrayToken kids)
        {
            foreach (var kid in kids.Data)
            {
                WalkPages(kid, numbers, visited, depth + 1);
            }

            return;
        }

        if (reference is { } page)
        {
            numbers[page] = numbers.Count + 1;
        }
    }

    private Dictionary<string, IToken> BuildNamedDestinations()
    {
        var names = new Dictionary<string, IToken>(StringComparer.Ordinal);

        try
        {
            var catalog = _pdf.Structure.Catalog.CatalogDictionary;

            // PDF 1.1 kept named destinations in a dictionary; later versions in a name tree.
            var legacy = PdfPigTokens.GetDictionary(_pdf, catalog, "Dests");

            if (legacy is not null)
            {
                foreach (var (name, value) in legacy.Data)
                {
                    names.TryAdd(name, value);
                }
            }

            var tree = PdfPigTokens.GetDictionary(_pdf, PdfPigTokens.GetDictionary(_pdf, catalog, "Names"), "Dests");

            if (tree is not null)
            {
                WalkNameTree(tree, names, 0);
            }
        }
        catch (Exception)
        {
            // Destinations that cannot be read are destinations that do not resolve.
        }

        return names;
    }

    private void WalkNameTree(DictionaryToken node, Dictionary<string, IToken> names, int depth)
    {
        if (depth > 32 || names.Count > 10_000)
        {
            return;
        }

        if (node.TryGet(NameToken.Create("Names"), out var entriesToken) && PdfPigTokens.Resolve(_pdf, entriesToken) is ArrayToken entries)
        {
            for (var index = 0; index + 1 < entries.Data.Count; index += 2)
            {
                var key = PdfPigTokens.Resolve(_pdf, entries.Data[index]) switch
                {
                    StringToken text => text.Data,
                    HexToken hex => hex.Data,
                    NameToken name => name.Data,
                    _ => null,
                };

                if (key is not null)
                {
                    names.TryAdd(key, entries.Data[index + 1]);
                }
            }
        }

        if (node.TryGet(NameToken.Create("Kids"), out var kidsToken) && PdfPigTokens.Resolve(_pdf, kidsToken) is ArrayToken kids)
        {
            foreach (var kid in kids.Data)
            {
                if (PdfPigTokens.Resolve(_pdf, kid) is DictionaryToken child)
                {
                    WalkNameTree(child, names, depth + 1);
                }
            }
        }
    }
}
