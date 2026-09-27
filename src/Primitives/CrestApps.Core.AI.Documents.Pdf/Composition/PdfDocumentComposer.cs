using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using Microsoft.Extensions.Options;
using MigraDoc.Rendering;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Renders a <see cref="PdfDocumentDefinition"/> into a PDF file.
/// </summary>
/// <remarks>
/// The one renderer every generated PDF goes through: the agent's documents, <c>generate_file</c> and tabular
/// exports. Pictures are resolved first, the document is laid out by MigraDoc, and the finishing touches that
/// work at page level — the watermark, the language, the PDF/A flag — are applied to the laid-out file.
/// </remarks>
internal sealed class PdfDocumentComposer
{
    private readonly PdfCompositionOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDocumentComposer"/> class.
    /// </summary>
    /// <param name="options">The host defaults.</param>
    /// <param name="timeProvider">The time provider the <c>{date}</c> token reads.</param>
    public PdfDocumentComposer(
        IOptions<PdfCompositionOptions> options,
        TimeProvider timeProvider)
    {
        _options = options?.Value ?? new PdfCompositionOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Renders a document.
    /// </summary>
    /// <param name="definition">The document.</param>
    /// <param name="images">Where the pictures the document names are read from, or <see langword="null"/> when it has none.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered file and what could not be rendered as asked.</returns>
    public async Task<PdfCompositionResult> ComposeAsync(
        PdfDocumentDefinition definition,
        IPdfImageSource images,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        PdfFontConfiguration.Ensure();

        var resolved = await ResolveImagesAsync(definition, images, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var dateText = _timeProvider.GetLocalNow().ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        var warnings = new List<string>();

        try
        {
            return Render(definition, resolved, dateText, forceDefaultFont: false, warnings);
        }
        catch (Exception ex) when (IsFontFailure(ex))
        {
            // A font the host cannot resolve fails the whole layout. The document is still worth having in
            // the default font, and the reader is told which font was not available.
            var retryWarnings = new List<string>
            {
                $"A requested font is not installed on this server, so the document was set in {PdfFontFamilies.Default}.",
            };

            return Render(definition, resolved, dateText, forceDefaultFont: true, retryWarnings);
        }
    }

    private PdfCompositionResult Render(
        PdfDocumentDefinition definition,
        Dictionary<string, PdfImageData> images,
        string dateText,
        bool forceDefaultFont,
        List<string> warnings)
    {
        var builder = new PdfMigraDocBuilder(definition, images, _options, dateText, forceDefaultFont, warnings);
        var document = builder.Build();
        var renderer = new PdfDocumentRenderer
        {
            Document = document,
        };

        if (definition.PdfA == true)
        {
            renderer.PdfDocument = new PdfDocument();
            renderer.PdfDocument.SetPdfA();
        }

        renderer.RenderDocument();

        var pdf = renderer.PdfDocument;

        if (!string.IsNullOrWhiteSpace(definition.Language))
        {
            pdf.Language = definition.Language.Trim();
        }

        pdf.ViewerPreferences.DisplayDocTitle = !string.IsNullOrWhiteSpace(definition.Title);

        if (definition.TableOfContents?.Enabled == true)
        {
            pdf.PageMode = PdfPageMode.UseOutlines;
        }

        ApplyWatermark(pdf, definition, images, forceDefaultFont, warnings);

        var pageCount = pdf.PageCount;

        using var buffer = new MemoryStream();
        pdf.Save(buffer, closeStream: false);

        return new PdfCompositionResult(buffer.ToArray(), pageCount, warnings);
    }

    private static void ApplyWatermark(
        PdfDocument pdf,
        PdfDocumentDefinition definition,
        Dictionary<string, PdfImageData> images,
        bool forceDefaultFont,
        List<string> warnings)
    {
        var watermark = definition.Watermark;

        if (watermark is null || (string.IsNullOrWhiteSpace(watermark.Text) && string.IsNullOrWhiteSpace(watermark.Image)))
        {
            return;
        }

        PdfImageData image = null;

        if (!string.IsNullOrWhiteSpace(watermark.Image) &&
            (!images.TryGetValue(watermark.Image.Trim(), out image) || image is null || !image.IsSupported))
        {
            warnings.Add($"The watermark image \"{watermark.Image}\" could not be found or is not a JPEG, PNG, GIF or BMP.");
            image = null;

            if (string.IsNullOrWhiteSpace(watermark.Text))
            {
                return;
            }
        }

        var family = forceDefaultFont
            ? PdfFontFamilies.Default
            : PdfFontFamilies.Resolve(definition.Theme?.FontFamily, PdfFontFamilies.Default, null);

        foreach (var page in pdf.Pages)
        {
            PdfPageDecorator.DrawWatermark(page, watermark, image, family);
        }
    }

    private static async Task<Dictionary<string, PdfImageData>> ResolveImagesAsync(
        PdfDocumentDefinition definition,
        IPdfImageSource images,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, PdfImageData>(StringComparer.Ordinal);

        if (images is null)
        {
            return resolved;
        }

        foreach (var source in CollectImageSources(definition))
        {
            if (resolved.ContainsKey(source))
            {
                continue;
            }

            resolved[source] = await images.ResolveAsync(source, cancellationToken);
        }

        return resolved;
    }

    /// <summary>
    /// Lists every picture a document refers to.
    /// </summary>
    /// <param name="definition">The document.</param>
    /// <returns>The picture sources, trimmed and without duplicates.</returns>
    public static HashSet<string> CollectImageSources(PdfDocumentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var sources = new HashSet<string>(StringComparer.Ordinal);

        void Add(string source)
        {
            if (!string.IsNullOrWhiteSpace(source))
            {
                sources.Add(source.Trim());
            }
        }

        Add(definition.Theme?.Logo);
        Add(definition.CoverPage?.Logo);
        Add(definition.Watermark?.Image);

        foreach (var section in definition.Sections ?? [])
        {
            foreach (var block in section.Blocks ?? [])
            {
                Add(block?.Image?.Source);
            }
        }

        return sources;
    }

    private static bool IsFontFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("font", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("typeface", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
