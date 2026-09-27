using System.Globalization;
using PdfSharp.Pdf;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Checks the links and bookmarks of a PDF: web addresses are checked for syntax and scheme only — nothing is
/// ever fetched — and internal links and bookmarks for a destination that exists.
/// </summary>
internal static class PdfLinkChecks
{
    private const int MaxListed = 6;

    private static readonly HashSet<string> _dangerousSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript",
        "vbscript",
        "file",
        "data",
    };

    private static readonly HashSet<string> _commonSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
        "mailto",
        "tel",
    };

    /// <summary>
    /// Runs the link checks.
    /// </summary>
    /// <param name="checks">The report to add to.</param>
    /// <param name="objects">The document, opened with PDFsharp.</param>
    /// <param name="content">The document, opened with PdfPig, used to quote the text under a link; or <see langword="null"/>.</param>
    /// <param name="pages">The one-based pages checked.</param>
    public static void Run(PdfCheckList checks, PdfDocument objects, PigDocument content, IReadOnlyList<int> pages)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(pages);

        var links = PdfAnnotationList.Read(objects, pages).Where(entry => entry.IsLink).ToList();

        CheckBookmarks(checks, objects);

        if (links.Count == 0)
        {
            checks.Info("Links", "The checked pages have no link annotations.");

            return;
        }

        var failures = new List<string>();
        var warnings = new List<string>();
        var plainHttp = 0;
        var web = 0;
        var internalLinks = 0;
        var other = new List<string>();

        foreach (var link in links)
        {
            var label = Label(link, content);

            if (link.Rect is not { } rect || rect.Width < 1 || rect.Height < 1)
            {
                warnings.Add($"{label} has a zero-size area, so it cannot be clicked");
            }

            switch (link.Action)
            {
                case "URI":
                    web++;
                    CheckUri(link.Uri, label, failures, warnings, ref plainHttp);

                    break;
                case "GoTo" or "Dest":
                    internalLinks++;

                    if (link.TargetPage is null)
                    {
                        failures.Add($"{label} leads nowhere: {link.TargetProblem ?? "its destination is missing"}");
                    }
                    else if (link.TargetProblem is not null)
                    {
                        warnings.Add($"{label} goes to page {link.TargetPage}, but {link.TargetProblem}");
                    }

                    break;
                case "JavaScript":
                    failures.Add($"{label} runs JavaScript instead of opening an address");

                    break;
                case "Launch":
                    failures.Add($"{label} launches a program or opens a file ({PdfCheckList.Quote(link.RemoteFile ?? "unnamed")})");

                    break;
                case "GoToR" or "GoToE":
                    other.Add($"{label} opens another file, {PdfCheckList.Quote(link.RemoteFile ?? "unnamed")}, which was not checked");

                    break;
                case null:
                    warnings.Add($"{label} has no action or destination, so clicking it does nothing");

                    break;
                default:
                    other.Add($"{label} runs a {link.Action} action");

                    break;
            }
        }

        var summary = string.Create(CultureInfo.InvariantCulture, $"{links.Count} link(s): {web} to web or mail addresses, {internalLinks} to pages of this document");

        if (failures.Count > 0)
        {
            checks.Fail("Broken or unsafe links", PdfCheckList.List(failures, MaxListed) + ".", "Fix or remove these links; add_pdf_links adds correct ones.");
        }

        if (warnings.Count > 0)
        {
            checks.Warn("Questionable links", PdfCheckList.List(warnings, MaxListed) + ".");
        }

        if (failures.Count == 0 && warnings.Count == 0)
        {
            checks.Pass("Links", summary + "; every address is well formed and every internal link reaches a page.");
        }
        else
        {
            checks.Info("Links", summary + ".");
        }

        if (plainHttp > 0)
        {
            checks.Info("Unencrypted links", string.Create(CultureInfo.InvariantCulture, $"{plainHttp} link(s) use http:// rather than https://; browsers may warn about them."));
        }

        if (other.Count > 0)
        {
            checks.Info("Other links", PdfCheckList.List(other, MaxListed) + ".");
        }
    }

    private static void CheckUri(string uri, string label, List<string> failures, List<string> warnings, ref int plainHttp)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            failures.Add($"{label} has an empty address");

            return;
        }

        var trimmed = uri.Trim();

        if (!trimmed.Equals(uri, StringComparison.Ordinal) || trimmed.Any(char.IsWhiteSpace))
        {
            warnings.Add($"{label} has spaces in its address {PdfCheckList.Quote(uri)}, which some viewers do not open");
        }

        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var scheme = colon > 0
            ? trimmed[..colon]
            : null;

        if (scheme is not null && _dangerousSchemes.Contains(scheme))
        {
            failures.Add($"{label} uses a {scheme.ToLowerInvariant()}: address ({PdfCheckList.Quote(trimmed)}), which runs code or opens local files instead of a web page");

            return;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            failures.Add($"{label} has an address that is not a complete URL: {PdfCheckList.Quote(trimmed)}");

            return;
        }

        if (!_commonSchemes.Contains(parsed.Scheme))
        {
            warnings.Add($"{label} uses the uncommon scheme {parsed.Scheme}: ({PdfCheckList.Quote(trimmed)}), which many viewers will not open");

            return;
        }

        if (parsed.Scheme is "http" or "https" && string.IsNullOrEmpty(parsed.Host))
        {
            failures.Add($"{label} has a web address with no host: {PdfCheckList.Quote(trimmed)}");

            return;
        }

        if (parsed.Scheme == "mailto" && !trimmed.Contains('@', StringComparison.Ordinal))
        {
            warnings.Add($"{label} is a mail link without an email address: {PdfCheckList.Quote(trimmed)}");
        }

        if (parsed.Scheme == "http")
        {
            plainHttp++;
        }
    }

    private static string Label(PdfAnnotationEntry link, PigDocument content)
    {
        var location = string.Create(CultureInfo.InvariantCulture, $"the link on page {link.Page}");

        if (content is null || link.Rect is not { } rect)
        {
            return location;
        }

        try
        {
            var text = PdfPageInspection.TextIn(content.GetPage(link.Page), rect, 40);

            return string.IsNullOrWhiteSpace(text)
                ? location
                : $"{location} ({PdfCheckList.Quote(text, 41)})";
        }
        catch (Exception)
        {
            return location;
        }
    }

    private static void CheckBookmarks(PdfCheckList checks, PdfDocument objects)
    {
        var outlines = PdfObjects.GetDictionary(objects.Internals.Catalog, "/Outlines");

        if (outlines is null)
        {
            return;
        }

        var pageIndex = new PdfPageIndex(objects);
        var broken = new List<string>();
        var count = 0;
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfDictionary>();

        if (PdfObjects.GetDictionary(outlines, "/First") is { } first)
        {
            pending.Push(first);
        }

        while (pending.Count > 0 && count < 10_000)
        {
            var item = pending.Pop();

            for (var current = item; current is not null && visited.Add(current); current = PdfObjects.GetDictionary(current, "/Next"))
            {
                count++;

                var title = PdfObjects.GetText(current, "/Title") ?? "(untitled)";
                var destination = PdfObjects.Get(current, "/Dest");
                var action = PdfObjects.GetDictionary(current, "/A");

                if (destination is null && action is not null && PdfObjects.IsName(action, "/S", "/GoTo"))
                {
                    destination = PdfObjects.Get(action, "/D");
                }

                if (destination is not null)
                {
                    var (page, problem) = PdfAnnotationList.ResolveDestination(objects, destination, pageIndex);

                    if (page is null)
                    {
                        broken.Add($"{PdfCheckList.Quote(title)}: {problem}");
                    }
                }

                if (PdfObjects.GetDictionary(current, "/First") is { } child)
                {
                    pending.Push(child);
                }
            }
        }

        if (count == 0)
        {
            return;
        }

        if (broken.Count > 0)
        {
            checks.Fail("Bookmarks", string.Create(CultureInfo.InvariantCulture, $"{broken.Count} of {count} bookmark(s) lead nowhere: ") + PdfCheckList.List(broken, MaxListed) + ".", "Rebuild the bookmarks with add_pdf_bookmarks.");
        }
        else
        {
            checks.Pass("Bookmarks", string.Create(CultureInfo.InvariantCulture, $"All {count} bookmark(s) lead to a page of this document."));
        }
    }
}
