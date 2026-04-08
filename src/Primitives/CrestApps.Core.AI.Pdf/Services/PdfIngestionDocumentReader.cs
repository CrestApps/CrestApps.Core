using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;
namespace CrestApps.Core.AI.Pdf.Services;

/// <summary>
/// Reads PDF files into an <see cref="IngestionDocument"/> using PdfPig.
/// </summary>
public sealed class PdfIngestionDocumentReader : IngestionDocumentReader
{
    private const string PdfMediaType = "application/pdf";

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

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var pdf = PdfDocument.Open(buffer);
        var document = new IngestionDocument(identifier);

        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var section = GetPageSection(page);

            if (section.Elements.Count > 0)
            {
                document.Sections.Add(section);
            }
        }

        return document;
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
