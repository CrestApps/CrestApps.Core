namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// Where a link goes: a web address, or a bookmark in the document.
/// </summary>
/// <param name="Target">The address, or the bookmark's name.</param>
/// <param name="IsWeb">Whether the target is a web address rather than a bookmark.</param>
internal sealed record PdfLayoutLink(string Target, bool IsWeb);
