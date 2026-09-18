using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Decides which figures are worth keeping and which are worth showing to a model. Calls no model itself.
/// </summary>
/// <remarks>
/// Most images in a publication carry nothing: logos, advertising artwork, decoration. A handful carry the
/// entire answer to a question the text cannot answer. The signals here are all cheap — a caption, a mention
/// in the prose, how many colours the picture uses, whether it repeats on every page — and they sort figures
/// into three tiers so the expensive step runs only where it pays.
/// </remarks>
public sealed class FigureSalienceProcessor : AIDocumentIngestionProcessor
{
    private readonly IOptions<FigureSalienceOptions> _options;
    private readonly IOptions<CaptionPatternOptions> _captionOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="FigureSalienceProcessor"/> class.
    /// </summary>
    /// <param name="options">The salience options.</param>
    /// <param name="captionOptions">The caption options, used to spot references in the prose.</param>
    public FigureSalienceProcessor(
        IOptions<FigureSalienceOptions> options,
        IOptions<CaptionPatternOptions> captionOptions)
    {
        _options = options;
        _captionOptions = captionOptions;
    }

    /// <summary>
    /// Scores every figure, assigns it a tier, and removes the ones not worth keeping.
    /// </summary>
    /// <param name="document">The document to process.</param>
    /// <param name="context">The per-run options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The processed document.</returns>
    public override Task<IngestionDocument> ProcessAsync(
        IngestionDocument document,
        DocumentIngestionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        context ??= DocumentIngestionContext.Default;

        var options = _options.Value;
        var pagesByHash = CountPagesByHash(document);
        var isScanned = IsScannedDocument(document, options);
        var scored = new List<ScoredFigure>();

        foreach (var section in document.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var images = section.Elements.OfType<IngestionDocumentImage>().ToList();

            if (images.Count == 0)
            {
                continue;
            }

            var pageCharacters = CountPageCharacters(section);

            foreach (var image in images)
            {
                var score = context.FigureMode == FigureProcessingMode.Off
                    ? int.MinValue
                    : GetScore(document, image, section, pageCharacters, pagesByHash, isScanned, options);

                scored.Add(new ScoredFigure(section, image, score));
            }
        }

        if (scored.Count == 0)
        {
            return Task.FromResult(document);
        }

        AssignTiers(scored, context, options);
        ApplyBudget(scored, context);
        Remove(document, scored);

        return Task.FromResult(document);
    }

    private int GetScore(
        IngestionDocument document,
        IngestionDocumentImage image,
        IngestionDocumentSection section,
        int pageCharacters,
        Dictionary<string, int> pagesByHash,
        bool isScanned,
        FigureSalienceOptions options)
    {
        var score = 0;

        // On a scanned document a full-page image is the page. There is no text layer to caption it or cite
        // it, so the signals below would drop it; reading it is the only way the document is ever answerable.
        if (isScanned && pageCharacters <= options.ScannedPageMaxCharacters && CoversPage(image, section, options))
        {
            image.Metadata[FigureMetadataKeys.SalienceScore] = options.ScannedPageScore;

            return options.ScannedPageScore;
        }

        // A printed, numbered caption is much stronger evidence than "this block was in smaller type", so
        // the two are not worth the same. Weighting them equally promoted every stretch of fine print beside
        // a photograph to the same tier as a labelled chart.
        if (image.GetMetadataString(FigureMetadataKeys.Caption) != null)
        {
            var source = image.GetMetadataString(FigureMetadataKeys.CaptionSource);

            score += source is CaptionSources.Pattern or CaptionSources.Provider
                ? options.PatternCaptionScore
                : options.TypographyCaptionScore;
        }

        if (IsCitedInProse(document, image))
        {
            score += options.CitedInProseScore;
        }

        var hash = image.GetMetadataString(FigureMetadataKeys.ContentHash);

        if (hash != null && pagesByHash.TryGetValue(hash, out var pages) && pages >= options.RepeatPageThreshold)
        {
            score -= options.RepeatedArtworkPenalty;
        }

        if (IsSmallOrExtreme(image, options))
        {
            score -= options.SmallOrExtremePenalty;
        }

        if (IsFullBleedAdvert(image, section, pageCharacters, options))
        {
            score -= options.FullBleedPenalty;
        }

        image.Metadata[FigureMetadataKeys.SalienceScore] = score;

        return score;
    }

    private bool IsCitedInProse(IngestionDocument document, IngestionDocumentImage image)
    {
        if (!image.HasMetadata ||
            !image.Metadata.TryGetValue(FigureMetadataKeys.Ordinal, out var raw) ||
            raw is not int ordinal)
        {
            return false;
        }

        var bucket = image.GetMetadataString(FigureMetadataKeys.Bucket) ?? CaptionBuckets.Figure;

        if (string.Equals(bucket, CaptionBuckets.Unknown, StringComparison.Ordinal))
        {
            bucket = CaptionBuckets.Figure;
        }

        var captionOptions = _captionOptions.Value;

        foreach (var element in document.EnumerateContent())
        {
            if (element is IngestionDocumentImage)
            {
                continue;
            }

            // The caption itself does not count as a citation: what matters is whether the prose leans on
            // the figure, not whether the figure was labelled. Nor does page furniture.
            if (element.GetMetadataString(ElementMetadataKeys.IsCaptionFor) != null || element.IsDecoration())
            {
                continue;
            }

            if (CaptionReferenceScanner.FindSentence(element.Text, ordinal, bucket, captionOptions) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSmallOrExtreme(IngestionDocumentImage image, FigureSalienceOptions options)
    {
        var width = GetInt(image, FigureMetadataKeys.PixelWidth);
        var height = GetInt(image, FigureMetadataKeys.PixelHeight);

        if (width == null || height == null || width <= 0 || height <= 0)
        {
            return false;
        }

        if (width < options.MinDescribableSamples || height < options.MinDescribableSamples)
        {
            return true;
        }

        var aspect = (double)Math.Max(width.Value, height.Value) / Math.Min(width.Value, height.Value);

        return aspect > options.MaxAspectRatio;
    }

    /// <summary>
    /// Determines whether a figure covers most of a page that says almost nothing. That is the shape of a
    /// full page advertisement, which is never worth describing.
    /// </summary>
    /// <param name="image">The figure.</param>
    /// <param name="section">The page the figure sits on.</param>
    /// <param name="pageCharacters">How many characters of text the page carries.</param>
    /// <param name="options">The salience options.</param>
    /// <returns><see langword="true"/> when the page reads as an advertisement.</returns>
    private static bool IsFullBleedAdvert(
        IngestionDocumentImage image,
        IngestionDocumentSection section,
        int pageCharacters,
        FigureSalienceOptions options)
    {
        return pageCharacters < options.FullBleedMaxPageCharacters && CoversPage(image, section, options);
    }

    /// <summary>
    /// Counts the characters of body text on a page. Page furniture is left out: a running head on an
    /// otherwise empty page does not make it a page of text.
    /// </summary>
    /// <param name="section">The page.</param>
    /// <returns>The character count.</returns>
    private static int CountPageCharacters(IngestionDocumentSection section)
    {
        return section.Elements
            .Where(element => element is not IngestionDocumentImage && !element.IsDecoration())
            .Sum(element => element.Text?.Length ?? 0);
    }

    /// <summary>
    /// Determines whether a document is a scan: most of its pages carry no text layer at all.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The salience options.</param>
    /// <returns><see langword="true"/> when the document reads as scanned.</returns>
    private static bool IsScannedDocument(IngestionDocument document, FigureSalienceOptions options)
    {
        if (document.Sections.Count == 0 || options.ScannedDocumentPageRatio <= 0)
        {
            return false;
        }

        var textless = document.Sections.Count(section =>
            section.Elements.OfType<IngestionDocumentImage>().Any() &&
            CountPageCharacters(section) <= options.ScannedPageMaxCharacters);

        return (double)textless / document.Sections.Count >= options.ScannedDocumentPageRatio;
    }

    /// <summary>
    /// Determines whether a figure covers most of its page.
    /// </summary>
    /// <param name="image">The figure.</param>
    /// <param name="section">The page the figure sits on.</param>
    /// <param name="options">The salience options.</param>
    /// <returns><see langword="true"/> when the figure is full bleed.</returns>
    private static bool CoversPage(IngestionDocumentImage image, IngestionDocumentSection section, FigureSalienceOptions options)
    {
        var pageWidth = GetDouble(section, ElementMetadataKeys.PageWidth);
        var pageHeight = GetDouble(section, ElementMetadataKeys.PageHeight);
        var bounds = FigureGeometry.GetBounds(image);

        if (pageWidth == null || pageHeight == null || bounds == null)
        {
            return false;
        }

        var pageArea = pageWidth.Value * pageHeight.Value;

        if (pageArea <= 0)
        {
            return false;
        }

        var imageArea = Math.Max(0, bounds[2] - bounds[0]) * Math.Max(0, bounds[3] - bounds[1]);

        return imageArea / pageArea >= options.FullBleedPageAreaRatio;
    }

    private static void AssignTiers(List<ScoredFigure> scored, DocumentIngestionContext context, FigureSalienceOptions options)
    {
        foreach (var figure in scored)
        {
            string tier;

            if (figure.Score < options.CaptionOnlyThreshold)
            {
                tier = FigureTiers.Skip;
            }
            else if (figure.Score < options.DescribeThreshold)
            {
                tier = context.FigureMode == FigureProcessingMode.All
                    ? FigureTiers.Describe
                    : FigureTiers.CaptionOnly;
            }
            else
            {
                tier = FigureTiers.Describe;
            }

            figure.Tier = tier;
            figure.Image.Metadata[FigureMetadataKeys.Tier] = tier;
        }
    }

    /// <summary>
    /// Caps how many figures one document may have described. The surplus is demoted to caption only, never
    /// dropped: a figure that lost a budget race is still a figure.
    /// </summary>
    /// <param name="scored">The scored figures.</param>
    /// <param name="context">The per-run options.</param>
    private static void ApplyBudget(List<ScoredFigure> scored, DocumentIngestionContext context)
    {
        var budget = context.MaxFigureDescriptionsPerDocument;

        if (budget < 0)
        {
            return;
        }

        var describing = scored
            .Where(figure => figure.Tier == FigureTiers.Describe)
            .OrderByDescending(figure => figure.Score)
            .ToList();

        for (var index = budget; index < describing.Count; index++)
        {
            describing[index].Tier = FigureTiers.CaptionOnly;
            describing[index].Image.Metadata[FigureMetadataKeys.Tier] = FigureTiers.CaptionOnly;
        }
    }

    /// <summary>
    /// Takes the skipped figures out of the document, and gives their captions back to the prose.
    /// </summary>
    /// <param name="document">The document being processed.</param>
    /// <param name="scored">Every figure and the tier it landed in.</param>
    /// <remarks>
    /// A caption is marked as belonging to a figure so that it is emitted with that figure rather than
    /// twice. Dropping the figure and leaving the mark behind means the caption is emitted with nothing, so
    /// its words leave the document altogether — and a caption is often the only text on the page that names
    /// what the picture showed. Clearing the mark turns it back into an ordinary paragraph.
    /// </remarks>
    private static void Remove(IngestionDocument document, List<ScoredFigure> scored)
    {
        var removed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var figure in scored)
        {
            if (figure.Tier != FigureTiers.Skip)
            {
                continue;
            }

            var figureId = figure.Image.GetFigureId();

            if (!string.IsNullOrEmpty(figureId))
            {
                removed.Add(figureId);
            }

            figure.Section.Elements.Remove(figure.Image);
        }

        if (removed.Count == 0)
        {
            return;
        }

        foreach (var element in document.EnumerateContent())
        {
            var captionFor = element.GetMetadataString(ElementMetadataKeys.IsCaptionFor);

            if (captionFor != null && removed.Contains(captionFor))
            {
                element.Metadata.Remove(ElementMetadataKeys.IsCaptionFor);
            }
        }
    }

    private static Dictionary<string, int> CountPagesByHash(IngestionDocument document)
    {
        var pagesByHash = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (var element in document.EnumerateContent())
        {
            if (element is not IngestionDocumentImage image)
            {
                continue;
            }

            var hash = image.GetMetadataString(FigureMetadataKeys.ContentHash);

            if (hash == null)
            {
                continue;
            }

            if (!pagesByHash.TryGetValue(hash, out var pages))
            {
                pages = [];
                pagesByHash[hash] = pages;
            }

            pages.Add(image.PageNumber ?? 0);
        }

        return pagesByHash.ToDictionary(entry => entry.Key, entry => entry.Value.Count, StringComparer.Ordinal);
    }

    private static int? GetInt(IngestionDocumentElement element, string key)
    {
        if (!element.HasMetadata || !element.Metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value is int number ? number : null;
    }

    private static double? GetDouble(IngestionDocumentElement element, string key)
    {
        if (!element.HasMetadata || !element.Metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value is double number ? number : null;
    }

    private sealed class ScoredFigure
    {
        public ScoredFigure(IngestionDocumentSection section, IngestionDocumentImage image, int score)
        {
            Section = section;
            Image = image;
            Score = score;
        }

        public IngestionDocumentSection Section { get; }

        public IngestionDocumentImage Image { get; }

        public int Score { get; }

        public string Tier { get; set; }
    }
}
