namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Finds the bytes of a picture a composed document refers to by name.
/// </summary>
/// <remarks>
/// A document stores where a picture comes from, not the picture, so the workspace stays small and a logo is
/// read once however many pages print it. The source decides what a name may reach — only files the current
/// conversation can see.
/// </remarks>
internal interface IPdfImageSource
{
    /// <summary>
    /// Resolves a picture.
    /// </summary>
    /// <param name="source">The name the document uses for the picture.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The picture, or <see langword="null"/> when nothing by that name is available.</returns>
    Task<PdfImageData> ResolveAsync(string source, CancellationToken cancellationToken = default);
}
