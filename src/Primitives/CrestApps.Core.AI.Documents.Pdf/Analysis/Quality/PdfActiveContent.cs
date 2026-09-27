using System.Globalization;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One action a PDF can run: a script, a program launch, or another action archival PDFs do not allow.
/// </summary>
/// <param name="Type">The action type without its slash, for example <c>JavaScript</c>.</param>
/// <param name="Location">Where the action is attached, for example <c>the open action</c> or <c>a link on page 3</c>.</param>
internal sealed record PdfActionFinding(string Type, string Location);

/// <summary>
/// What a PDF can do by itself when it is opened or used.
/// </summary>
internal sealed class PdfActiveContentReport
{
    /// <summary>
    /// Gets the actions of interest, each with where it is attached.
    /// </summary>
    public List<PdfActionFinding> Actions { get; } = [];

    /// <summary>
    /// Gets where additional-actions dictionaries (<c>/AA</c>, actions run on events such as opening a page) are attached.
    /// </summary>
    public List<string> AdditionalActions { get; } = [];

    /// <summary>
    /// Gets the names of the document-level scripts.
    /// </summary>
    public List<string> DocumentScripts { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the file carries JavaScript.
    /// </summary>
    public bool HasJavaScript => DocumentScripts.Count > 0 || Actions.Any(action => action.Type == "JavaScript");

    /// <summary>
    /// Gets a value indicating whether the file can launch a program or open a file.
    /// </summary>
    public bool HasLaunch => Actions.Any(action => action.Type == "Launch");
}

/// <summary>
/// Finds the scripts and actions a PDF carries.
/// </summary>
internal static class PdfActiveContent
{
    /// <summary>
    /// The action types PDF/A does not allow.
    /// </summary>
    public static readonly HashSet<string> ArchiveForbiddenActions = new(StringComparer.Ordinal)
    {
        "JavaScript",
        "Launch",
        "Sound",
        "Movie",
        "ResetForm",
        "ImportData",
        "Hide",
        "SetOCGState",
        "Rendition",
        "Trans",
        "GoTo3DView",
    };

    /// <summary>
    /// Scans a document.
    /// </summary>
    /// <param name="document">The document, opened with PDFsharp.</param>
    /// <returns>What was found.</returns>
    public static PdfActiveContentReport Scan(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var report = new PdfActiveContentReport();
        var catalog = document.Internals.Catalog;
        var locations = new Dictionary<PdfDictionary, string>(ReferenceEqualityComparer.Instance);

        foreach (var (name, _) in PdfObjectReader.ReadNameTree(PdfObjectReader.GetDictionary(PdfObjectReader.GetDictionary(catalog, "/Names"), "/JavaScript"), 1_000))
        {
            report.DocumentScripts.Add(name ?? "(unnamed)");
        }

        Label(locations, PdfObjectReader.Get(catalog, "/OpenAction"), "the open action (runs when the file opens)");
        LabelAdditional(report, locations, catalog, "the document");

        for (var index = 0; index < document.PageCount; index++)
        {
            var page = document.Pages[index];
            var pageName = string.Create(CultureInfo.InvariantCulture, $"page {index + 1}");

            LabelAdditional(report, locations, page, pageName);

            foreach (var item in PdfObjectReader.Items(PdfObjectReader.GetArray(page, "/Annots")))
            {
                if (item is not PdfDictionary annotation)
                {
                    continue;
                }

                var kind = PdfObjectReader.GetName(annotation, "/Subtype") switch
                {
                    "/Link" => "a link",
                    "/Widget" => "a form field",
                    _ => "an annotation",
                };

                Label(locations, PdfObjectReader.Get(annotation, "/A"), $"{kind} on {pageName}");
                LabelAdditional(report, locations, annotation, $"{kind} on {pageName}");
            }
        }

        foreach (var dictionary in PdfObjectReader.EnumerateDictionaries(document))
        {
            var type = PdfObjectReader.GetName(dictionary, "/S")?.TrimStart('/');

            if (type is null && dictionary.Elements.ContainsKey("/JS"))
            {
                type = "JavaScript";
            }

            if (type is null || !ArchiveForbiddenActions.Contains(type))
            {
                continue;
            }

            // A structure element also carries /S (its type); only action dictionaries are of interest.
            if (PdfObjectReader.IsName(dictionary, "/Type", "/StructElem") || (dictionary.Elements.ContainsKey("/P") && dictionary.Elements.ContainsKey("/K")))
            {
                continue;
            }

            if (!locations.TryGetValue(dictionary, out var location))
            {
                location = "elsewhere in the file";
            }

            report.Actions.Add(new PdfActionFinding(type, location));
        }

        // Form fields carry their own additional actions, such as keystroke and format scripts.
        var form = PdfObjectReader.GetDictionary(catalog, "/AcroForm");

        foreach (var field in PdfFormFieldList.Read(document))
        {
            if (field.Dictionary.Elements.ContainsKey("/AA") && !report.AdditionalActions.Contains($"the field \"{field.FullName}\""))
            {
                report.AdditionalActions.Add($"the field \"{field.FullName}\"");
            }
        }

        if (form is not null && PdfObjectReader.Get(form, "/XFA") is not null)
        {
            report.Actions.Add(new PdfActionFinding("XFA", "the form (an XFA form, which can carry its own scripts)"));
        }

        return report;
    }

    private static void Label(Dictionary<PdfDictionary, string> locations, PdfItem action, string location)
    {
        var current = PdfObjectReader.Resolve(action) as PdfDictionary;

        // An action may chain further actions through /Next.
        for (var step = 0; step < 16 && current is not null; step++)
        {
            locations.TryAdd(current, location);

            var next = PdfObjectReader.Get(current, "/Next");

            if (next is PdfArray array)
            {
                foreach (var item in PdfObjectReader.Items(array))
                {
                    Label(locations, item, location);
                }

                break;
            }

            current = next as PdfDictionary;
        }
    }

    private static void LabelAdditional(
        PdfActiveContentReport report,
        Dictionary<PdfDictionary, string> locations,
        PdfDictionary owner,
        string location)
    {
        var additional = PdfObjectReader.GetDictionary(owner, "/AA");

        if (additional is null)
        {
            return;
        }

        report.AdditionalActions.Add(location);

        foreach (var pair in additional.Elements)
        {
            Label(locations, pair.Value, $"an event action of {location}");
        }
    }
}
