using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Applies the document formatting a tool call carries — page setup, theme, running heads, page numbers,
/// cover, contents, watermark and metadata — to a composed document.
/// </summary>
internal static class PdfFormattingArguments
{
    /// <summary>
    /// The formatting areas a call can clear.
    /// </summary>
    public static readonly string[] ClearableAreas =
    [
        "theme",
        "page_setup",
        "header",
        "footer",
        "page_numbers",
        "cover_page",
        "table_of_contents",
        "watermark",
    ];

    /// <summary>
    /// Applies the formatting in a call to a document.
    /// </summary>
    /// <param name="definition">The document.</param>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="replace">Whether each area given replaces the stored one instead of being merged into it.</param>
    /// <param name="includePageSetup">Whether the call's page setup applies to the document, rather than having been applied to one section already.</param>
    /// <returns>The areas that changed.</returns>
    public static List<string> Apply(PdfDocumentDefinition definition, PdfToolArguments arguments, bool replace, bool includePageSetup = true)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(arguments);

        var changed = new List<string>();

        foreach (var area in arguments.GetStrings("clear"))
        {
            if (Clear(definition, area.Trim().ToLowerInvariant()))
            {
                changed.Add("cleared " + area);
            }
        }

        if (includePageSetup)
        {
            definition.PageSetup = Merge(definition.PageSetup, arguments, "page_setup", replace, changed);
        }

        definition.Theme = Merge(definition.Theme, arguments, "theme", replace, changed);
        definition.Header = Merge(definition.Header, arguments, "header", replace, changed);
        definition.Footer = Merge(definition.Footer, arguments, "footer", replace, changed);
        definition.PageNumbers = Merge(definition.PageNumbers, arguments, "page_numbers", replace, changed);
        definition.CoverPage = Merge(definition.CoverPage, arguments, "cover_page", replace, changed);
        definition.TableOfContents = Merge(definition.TableOfContents, arguments, "table_of_contents", replace, changed);
        definition.Watermark = Merge(definition.Watermark, arguments, "watermark", replace, changed);

        // Asking for page numbers, a cover or contents by describing them means turning them on.
        if (definition.PageNumbers is not null && definition.PageNumbers.Enabled is null)
        {
            definition.PageNumbers.Enabled = true;
        }

        if (definition.TableOfContents is not null && definition.TableOfContents.Enabled is null)
        {
            definition.TableOfContents.Enabled = true;
        }

        if (definition.CoverPage is not null && definition.CoverPage.Enabled is null)
        {
            definition.CoverPage.Enabled = true;
        }

        SetText(arguments, "title", value => definition.Title = value, changed);
        SetText(arguments, "author", value => definition.Author = value, changed);
        SetText(arguments, "subject", value => definition.Subject = value, changed);
        SetText(arguments, "keywords", value => definition.Keywords = value, changed);
        SetText(arguments, "language", value => definition.Language = value, changed);

        if (arguments.GetBoolean("pdf_a") is { } pdfA)
        {
            definition.PdfA = pdfA;
            changed.Add("pdf_a");
        }

        return changed;
    }

    private static T Merge<T>(T current, PdfToolArguments arguments, string name, bool replace, List<string> changed)
        where T : class, new()
    {
        if (!arguments.TryGetElement(name, out var element))
        {
            return current;
        }

        if (element.ValueKind == JsonValueKind.String && element.GetString() is { } text && text.TrimStart().StartsWith('{'))
        {
            using var parsed = JsonDocument.Parse(text);

            element = parsed.RootElement.Clone();
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return current;
        }

        changed.Add(name);

        return replace
            ? PdfDefinitionJson.FromElement<T>(element) ?? new T()
            : PdfDefinitionJson.Merge(current, element);
    }

    private static bool Clear(PdfDocumentDefinition definition, string area)
    {
        switch (area)
        {
            case "theme":
                definition.Theme = null;

                return true;
            case "page_setup":
                definition.PageSetup = null;

                return true;
            case "header":
                definition.Header = null;

                return true;
            case "footer":
                definition.Footer = null;

                return true;
            case "page_numbers":
                definition.PageNumbers = null;

                return true;
            case "cover_page" or "cover":
                definition.CoverPage = null;

                return true;
            case "table_of_contents" or "toc":
                definition.TableOfContents = null;

                return true;
            case "watermark":
                definition.Watermark = null;

                return true;
            default:
                return false;
        }
    }

    private static void SetText(PdfToolArguments arguments, string name, Action<string> set, List<string> changed)
    {
        var value = arguments.GetString(name);

        if (value is null)
        {
            return;
        }

        set(value.Trim());
        changed.Add(name);
    }
}
