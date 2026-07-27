using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Reads PDF files into an <see cref="IngestionDocument"/> using PdfPig.
/// </summary>
public sealed class PdfIngestionDocumentReader : IngestionDocumentReader
{
    private const string PdfMediaType = "application/pdf";

    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="identifier">The identifier.</param>
    /// <param name="mediaType">The media type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(mediaType, PdfMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Media type '{mediaType}' is not supported. Only '{PdfMediaType}' is accepted.");
        }

        ArgumentNullException.ThrowIfNull(source);

        MemoryStream buffer = null;
        var workingStream = source;

        if (source.CanSeek)
        {
            source.Position = 0;
        }
        else
        {
            buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            workingStream = buffer;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var pdf = PdfDocument.Open(workingStream);
            var document = new IngestionDocument(identifier);

            for (var pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = pdf.GetPage(pageNumber);
                var section = GetPageSection(page);

                if (section.Elements.Count > 0)
                {
                    document.Sections.Add(section);
                }
            }

            return document;
        }
        finally
        {
            if (buffer != null)
            {
                await buffer.DisposeAsync();
            }
        }
    }

    private static IngestionDocumentSection GetPageSection(UglyToad.PdfPig.Content.Page pdfPage)
    {
        var section = new IngestionDocumentSection
        {
            PageNumber = pdfPage.Number,
        };

        var pageText = pdfPage.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(pageText))
        {
            section.Elements.Add(new IngestionDocumentParagraph(pageText)
            {
                Text = pageText,
            });
        }

        return section;
    }
}
