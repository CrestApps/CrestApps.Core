using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Composition.Layout;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Objects;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Renders a <see cref="PdfDocumentDefinition"/> into a PDF file.
/// </summary>
/// <remarks>
/// The one renderer every generated PDF goes through: the agent's documents, <c>generate_file</c> and tabular
/// exports. Pictures are resolved first, the document is laid out by <see cref="PdfLayoutRenderer"/>, and the
/// finishing touches that work at page level — the watermark, the language, the PDF/A flag — are applied to
/// the laid-out file.
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
        var builder = new PdfLayoutBuilder(definition, images, _options, dateText, forceDefaultFont, warnings);

        using var pdf = new PdfLayoutRenderer(builder.Build()).Render();

        if (!PdfFontLibrary.HasInstalledFonts)
        {
            warnings.Add("This server has no fonts installed, so the document was set in the standard PDF fonts, which write Western European text only.");
        }

        if (!string.IsNullOrWhiteSpace(definition.Language))
        {
            pdf.Language = definition.Language.Trim();
        }

        var preferences = new PdfDictionary(pdf);

        preferences.Elements.SetBoolean("/DisplayDocTitle", !string.IsNullOrWhiteSpace(definition.Title));
        pdf.Catalog.Elements["/ViewerPreferences"] = preferences;

        if (definition.TableOfContents?.Enabled == true)
        {
            pdf.Catalog.Elements.SetName("/PageMode", "/UseOutlines");
        }

        ApplyWatermark(pdf, definition, images, forceDefaultFont, warnings);

        var pageCount = pdf.PageCount;

        // The sync writes the XMP packet, and the PDF/A identification when it was asked for.
        var metadata = PdfMetadataSync.Capture(pdf);

        if (definition.PdfA == true)
        {
            if (!PdfFontLibrary.HasInstalledFonts)
            {
                // PDF/A needs every font embedded, and the standard fonts are not.
                warnings.Add("PDF/A needs its fonts embedded, and this server has no fonts to embed, so the file was not written as PDF/A.");
            }
            else if (PdfArchiveProfile.TryApply(pdf))
            {
                metadata.ClaimPdfA(2, "B");
            }
            else
            {
                warnings.Add("The sRGB colour profile PDF/A needs is not available on this server, so the file was not written as PDF/A.");
            }
        }

        var bytes = metadata.Save(pdf, _timeProvider.GetUtcNow());

        if (definition.PdfA == true && !metadata.Rewritten)
        {
            warnings.Add("The PDF/A identification could not be written into this file's metadata, so it does not claim PDF/A.");
        }

        return new PdfCompositionResult(bytes, pageCount, warnings);
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
