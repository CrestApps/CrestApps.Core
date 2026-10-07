using CrestApps.Core.AI.Documents.OpenXml.Word;

namespace CrestApps.Core.AI.Documents.Word.Fields;

/// <summary>
/// Brings a document's computed content up to date before it is shown or exported: caption numbers, the table
/// of contents and index with their page numbers, and the text of cross-references.
/// </summary>
/// <remarks>
/// The results are filled in from this host's own layout, so a preview, another reader or a printout made before
/// Word opens the file shows current numbers. Word is asked to update the fields when it opens the file only when
/// a result could not be filled in here — a page past the end of the layout, a table of figures, a reference to a
/// paragraph's number, a page count — because that request makes Word ask the reader whether to update the fields
/// every time the file is opened.
/// </remarks>
internal static class WordDocumentRefresher
{
    // Fields whose results only Word computes, so a document that has them still asks Word to update it.
    private static readonly HashSet<string> _wordOnlyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "NUMPAGES", "SECTIONPAGES", "NOTEREF",
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

        var complete = WordReferenceUpdater.Update(package, services);

        complete &= Structure.WordIndex.RefreshAll(package, services);

        if (!complete || WordFieldScanner.Scan(package.Body).Any(field => _wordOnlyFields.Contains(field.Type)))
        {
            WordFieldWriter.RequestUpdateOnOpen(package);
        }
    }
}
