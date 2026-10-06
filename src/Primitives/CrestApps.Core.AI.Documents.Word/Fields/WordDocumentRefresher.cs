using CrestApps.Core.AI.Documents.OpenXml.Word;

namespace CrestApps.Core.AI.Documents.Word.Fields;

/// <summary>
/// Brings a document's computed content up to date before it is shown or exported: caption numbers, the table
/// of contents and index with their page numbers, and the text of cross-references.
/// </summary>
/// <remarks>
/// Word recomputes all of this from its own pagination when it opens a file whose fields are marked for update,
/// but a preview, another reader, or a printout made before that would show stale numbers — so the results are
/// filled in from this host's own layout, and Word is asked to update them as well.
/// </remarks>
internal static class WordDocumentRefresher
{
    private static readonly HashSet<string> _pageDependentFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "TOC", "INDEX", "PAGEREF", "REF", "NUMPAGES", "SECTIONPAGES", "NOTEREF",
    };

    /// <summary>
    /// Refreshes a document in place.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="services">The request services, for the layout options.</param>
    public static void Refresh(WordPackage package, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(package);

        WordCaptions.Renumber(package);
        WordReferenceUpdater.Update(package, services);
        Structure.WordIndex.RefreshAll(package, services);

        if (WordFieldScanner.Scan(package.Body).Any(field => _pageDependentFields.Contains(field.Type)))
        {
            WordFieldWriter.RequestUpdateOnOpen(package);
        }
    }
}
