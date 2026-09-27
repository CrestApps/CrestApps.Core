using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Removes what a PDF carries besides what its pages show: metadata, scripts and actions, attachments,
/// thumbnails, invisible text and hidden layers — and, when asked, comments, form data and external links.
/// </summary>
/// <remarks>
/// This is what a file is put through before it leaves an organization: the author's name in the metadata,
/// an embedded spreadsheet, a script that runs when the file opens, or a layer nobody switched on can each
/// reveal more than the visible pages do.
/// </remarks>
internal static class PdfSanitizer
{
    /// <summary>
    /// Document information, XMP packets and applications' private data.
    /// </summary>
    public const string Metadata = "metadata";

    /// <summary>
    /// JavaScript, launch and other actions that run by themselves, and XFA forms.
    /// </summary>
    public const string Scripts = "scripts";

    /// <summary>
    /// Embedded files and file attachment annotations.
    /// </summary>
    public const string Attachments = "attachments";

    /// <summary>
    /// Page thumbnails.
    /// </summary>
    public const string Thumbnails = "thumbnails";

    /// <summary>
    /// Text drawn invisibly, which is also how OCR text layers are written.
    /// </summary>
    public const string HiddenText = "hidden_text";

    /// <summary>
    /// Layers that are hidden when the document opens, with their content.
    /// </summary>
    public const string HiddenLayers = "hidden_layers";

    /// <summary>
    /// Comments, highlights, stamps and every other annotation except links and form fields.
    /// </summary>
    public const string Comments = "comments";

    /// <summary>
    /// The values of form fields.
    /// </summary>
    public const string FormData = "form_data";

    /// <summary>
    /// Links to web addresses and other files.
    /// </summary>
    public const string ExternalLinks = "external_links";

    /// <summary>
    /// What a sanitize removes when nothing is chosen: everything that is not visible on the pages.
    /// </summary>
    public static readonly string[] Defaults = [Metadata, Scripts, Attachments, Thumbnails, HiddenText, HiddenLayers];

    /// <summary>
    /// Everything a sanitize can remove.
    /// </summary>
    public static readonly string[] All = [.. Defaults, Comments, FormData, ExternalLinks];

    private static readonly HashSet<string> _activeActions = new(StringComparer.Ordinal)
    {
        "JavaScript",
        "Launch",
        "ImportData",
        "SubmitForm",
        "ResetForm",
        "Rendition",
        "Sound",
        "Movie",
        "Hide",
        "SetOCGState",
        "GoTo3DView",
        "Trans",
    };

    /// <summary>
    /// Removes the chosen kinds of content.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <param name="kinds">The kinds to remove.</param>
    /// <returns>How many items of each kind were found and removed.</returns>
    public static Dictionary<string, int> Apply(PdfDocument document, IReadOnlySet<string> kinds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(kinds);

        var removed = new Dictionary<string, int>(StringComparer.Ordinal);

        if (kinds.Contains(Scripts))
        {
            removed[Scripts] = RemoveScripts(document);
        }

        if (kinds.Contains(Attachments))
        {
            removed[Attachments] = PdfAttachments.List(document)
                .Select(attachment => attachment.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList()
                .Sum(name => PdfAttachments.Remove(document, name));
        }

        if (kinds.Contains(Thumbnails))
        {
            removed[Thumbnails] = PdfStreamOptimizer.RemoveThumbnails(document);
        }

        if (kinds.Contains(HiddenLayers))
        {
            removed[HiddenLayers] = RemoveHiddenLayers(document);
        }

        if (kinds.Contains(HiddenText))
        {
            removed[HiddenText] = document.Pages
                .Cast<PdfPage>()
                .Sum(page => PdfContentRewriter.Rewrite(page, new PdfContentRewriteOptions { RemoveInvisibleText = true }).GlyphsRemoved);
        }

        if (kinds.Contains(Comments) || kinds.Contains(ExternalLinks))
        {
            var (comments, links) = RemoveAnnotations(document, kinds.Contains(Comments), kinds.Contains(ExternalLinks));

            if (kinds.Contains(Comments))
            {
                removed[Comments] = comments;
            }

            if (kinds.Contains(ExternalLinks))
            {
                removed[ExternalLinks] = links;
            }
        }

        if (kinds.Contains(FormData))
        {
            var cleared = 0;

            foreach (var field in PdfFormFields.Read(document))
            {
                if (field.Type is "signature" || string.IsNullOrEmpty(field.Value) || (field.Type is "checkbox" or "radio" && field.Value == "Off"))
                {
                    continue;
                }

                PdfFormFields.SetValue(document, field, field.Type is "checkbox" ? "false" : string.Empty);
                cleared++;
            }

            removed[FormData] = cleared;
        }

        if (kinds.Contains(Metadata))
        {
            // Only the PDF/A and PDF/UA identification of the XMP packet survives the save.
            PdfMetadataSync.Capture(document, keepOtherProperties: false);
            removed[Metadata] = PdfStreamOptimizer.RemoveMetadata(document);
        }

        return removed;
    }

    private static int RemoveScripts(PdfDocument document)
    {
        var removed = 0;
        var catalog = document.Internals.Catalog;

        if (PdfObjects.GetDictionary(catalog, "/Names") is { } names && names.Elements.Remove("/JavaScript"))
        {
            removed++;
        }

        if (IsActive(PdfObjects.Resolve(catalog.Elements["/OpenAction"])))
        {
            catalog.Elements.Remove("/OpenAction");
            removed++;
        }

        removed += catalog.Elements.Remove("/AA") ? 1 : 0;

        if (PdfObjects.GetDictionary(catalog, "/AcroForm") is { } form)
        {
            removed += form.Elements.Remove("/XFA") ? 1 : 0;

            foreach (var field in Descendants(PdfObjects.GetArray(form, "/Fields")))
            {
                removed += RemoveActions(field);
            }
        }

        foreach (var page in document.Pages)
        {
            removed += page.Elements.Remove("/AA") ? 1 : 0;

            foreach (var item in PdfObjects.Items(PdfObjects.GetArray(page, "/Annots")))
            {
                if (PdfObjects.Resolve(item) is PdfDictionary annotation)
                {
                    removed += RemoveActions(annotation);
                }
            }
        }

        return removed;
    }

    private static int RemoveActions(PdfDictionary dictionary)
    {
        var removed = dictionary.Elements.Remove("/AA") ? 1 : 0;

        if (IsActive(PdfObjects.Resolve(dictionary.Elements["/A"])))
        {
            dictionary.Elements.Remove("/A");
            removed++;
        }

        return removed;
    }

    private static bool IsActive(PdfItem action)
    {
        if (action is not PdfDictionary dictionary)
        {
            return false;
        }

        var type = PdfObjects.GetName(dictionary, "/S")?.TrimStart('/');

        if (type is not null && _activeActions.Contains(type))
        {
            return true;
        }

        // An action chains the ones in /Next; a harmless first step can lead to a script.
        var next = PdfObjects.Resolve(dictionary.Elements["/Next"]);

        return next is PdfArray array
            ? PdfObjects.Items(array).Any(item => IsActive(PdfObjects.Resolve(item)))
            : IsActive(next);
    }

    private static IEnumerable<PdfDictionary> Descendants(PdfArray fields, int depth = 0)
    {
        if (fields is null || depth > 32)
        {
            yield break;
        }

        foreach (var item in PdfObjects.Items(fields))
        {
            if (PdfObjects.Resolve(item) is not PdfDictionary field)
            {
                continue;
            }

            yield return field;

            foreach (var child in Descendants(PdfObjects.GetArray(field, "/Kids"), depth + 1))
            {
                yield return child;
            }
        }
    }

    private static int RemoveHiddenLayers(PdfDocument document)
    {
        var hidden = PdfLayers.List(document).Where(layer => !layer.Visible && layer.Group is not null).ToList();

        if (hidden.Count == 0)
        {
            return 0;
        }

        var groups = new HashSet<PdfObject>(hidden.Select(layer => (PdfObject)layer.Group), ReferenceEqualityComparer.Instance);

        foreach (var page in document.Pages)
        {
            PdfContentRewriter.Rewrite(page, new PdfContentRewriteOptions { RemoveOptionalContent = groups });

            // An annotation that belongs to a hidden layer goes with it.
            if (PdfObjects.GetArray(page, "/Annots") is { } annotations)
            {
                for (var index = annotations.Elements.Count - 1; index >= 0; index--)
                {
                    if (PdfObjects.Resolve(annotations.Elements[index]) is PdfDictionary annotation &&
                        PdfObjects.Resolve(annotation.Elements["/OC"]) is PdfDictionary membership &&
                        groups.Contains(membership))
                    {
                        annotations.Elements.RemoveAt(index);
                    }
                }
            }
        }

        // The layers themselves are then declared no more.
        var properties = PdfObjects.GetDictionary(document.Internals.Catalog, "/OCProperties");

        Prune(PdfObjects.GetArray(properties, "/OCGs"), groups);

        foreach (var configuration in new[] { PdfObjects.GetDictionary(properties, "/D") }.Concat(PdfObjects.Items(PdfObjects.GetArray(properties, "/Configs")).Select(item => PdfObjects.Resolve(item) as PdfDictionary)))
        {
            if (configuration is null)
            {
                continue;
            }

            foreach (var key in new[] { "/ON", "/OFF", "/Order", "/Locked", "/RBGroups" })
            {
                Prune(PdfObjects.GetArray(configuration, key), groups);
            }
        }

        if (PdfObjects.GetArray(properties, "/OCGs") is { Elements.Count: 0 })
        {
            document.Internals.Catalog.Elements.Remove("/OCProperties");
        }

        return hidden.Count;
    }

    private static void Prune(PdfArray array, HashSet<PdfObject> groups, int depth = 0)
    {
        if (array is null || depth > 16)
        {
            return;
        }

        for (var index = array.Elements.Count - 1; index >= 0; index--)
        {
            var item = PdfObjects.Resolve(array.Elements[index]);

            if (item is PdfObject value && groups.Contains(value) && item is not PdfArray)
            {
                array.Elements.RemoveAt(index);
            }
            else if (item is PdfArray nested)
            {
                Prune(nested, groups, depth + 1);
            }
        }
    }

    private static (int Comments, int Links) RemoveAnnotations(PdfDocument document, bool comments, bool links)
    {
        var commentCount = 0;
        var linkCount = 0;

        foreach (var page in document.Pages)
        {
            if (PdfObjects.GetArray(page, "/Annots") is not { } annotations)
            {
                continue;
            }

            for (var index = annotations.Elements.Count - 1; index >= 0; index--)
            {
                if (PdfObjects.Resolve(annotations.Elements[index]) is not PdfDictionary annotation)
                {
                    continue;
                }

                var subtype = PdfObjects.GetName(annotation, "/Subtype")?.TrimStart('/');

                if (subtype == "Widget")
                {
                    continue;
                }

                if (subtype == "Link")
                {
                    if (links && IsExternal(PdfObjects.GetDictionary(annotation, "/A")))
                    {
                        annotations.Elements.RemoveAt(index);
                        linkCount++;
                    }

                    continue;
                }

                if (comments)
                {
                    annotations.Elements.RemoveAt(index);

                    if (subtype != "Popup")
                    {
                        commentCount++;
                    }
                }
            }
        }

        return (commentCount, linkCount);
    }

    private static bool IsExternal(PdfDictionary action)
    {
        return PdfObjects.GetName(action, "/S")?.TrimStart('/') is "URI" or "Launch" or "GoToR" or "GoToE" or "SubmitForm" or "ImportData";
    }
}
