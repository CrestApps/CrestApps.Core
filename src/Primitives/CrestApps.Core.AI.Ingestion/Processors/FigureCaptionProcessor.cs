using System.Text;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Works out which caption belongs to which figure, and what body text gives the figure its meaning.
/// Calls no model.
/// </summary>
/// <remarks>
/// Caption direction is not a fixed rule. The same publication prints figure captions below the artwork and
/// table captions above it, so the processor reads the document's own habit first — from the captions it is
/// sure about — and only falls back to a configured default when the document has not shown enough of them.
/// </remarks>
public sealed class FigureCaptionProcessor : AIDocumentIngestionProcessor
{
    private const double DefaultLineHeight = 12;
    private const double ConfidentSampleLineHeights = 3;

    private readonly IFigureCaptionCandidateDetector _detector;
    private readonly IFigureCaptionResolver _resolver;
    private readonly IOptions<CaptionPatternOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="FigureCaptionProcessor"/> class.
    /// </summary>
    /// <param name="detector">The caption candidate detector.</param>
    /// <param name="resolver">The caption resolver.</param>
    /// <param name="options">The caption options.</param>
    public FigureCaptionProcessor(
        IFigureCaptionCandidateDetector detector,
        IFigureCaptionResolver resolver,
        IOptions<CaptionPatternOptions> options)
    {
        _detector = detector;
        _resolver = resolver;
        _options = options;
    }

    /// <summary>
    /// Assigns captions and surrounding context to the figures in the document.
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

        if (context.FigureMode == FigureProcessingMode.Off)
        {
            return Task.FromResult(document);
        }

        var pages = BuildPages(document);

        if (pages.Count == 0)
        {
            return Task.FromResult(document);
        }

        var candidatesByPage = new Dictionary<int, IReadOnlyList<CaptionCandidate>>();

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            candidatesByPage[page.PageNumber] = _detector.GetCandidates(page) ?? [];
        }

        var prior = LearnDirections(pages, candidatesByPage);
        var captioned = new HashSet<IngestionDocumentImage>();

        // A caption a document-understanding service reported is a printed caption it located, not a
        // guess from geometry. Nothing here improves on that, so those figures are left alone.
        foreach (var page in pages)
        {
            foreach (var image in page.Images)
            {
                if (image.GetMetadataString(FigureMetadataKeys.CaptionSource) == CaptionSources.Provider)
                {
                    captioned.Add(image);
                }
            }
        }

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = candidatesByPage[page.PageNumber];
            var assignments = _resolver.Resolve(page, candidates, prior) ?? [];

            foreach (var assignment in assignments)
            {
                if (assignment?.Image == null || assignment.Candidate == null || captioned.Contains(assignment.Image))
                {
                    continue;
                }

                Apply(assignment);
                captioned.Add(assignment.Image);
            }
        }

        ApplyFallbacks(pages, captioned);
        ApplyContext(pages, candidatesByPage);

        return Task.FromResult(document);
    }

    private static void Apply(CaptionAssignment assignment)
    {
        var image = assignment.Image;
        var candidate = assignment.Candidate;

        image.Metadata[FigureMetadataKeys.Caption] = candidate.Text;
        image.Metadata[FigureMetadataKeys.CaptionSource] = candidate.MatchedPattern
            ? CaptionSources.Pattern
            : CaptionSources.Typography;
        image.Metadata[FigureMetadataKeys.Bucket] = candidate.Bucket ?? CaptionBuckets.Unknown;

        if (candidate.Ordinal.HasValue)
        {
            image.Metadata[FigureMetadataKeys.Ordinal] = candidate.Ordinal.Value;
        }

        // The caption is emitted with its figure, so flattening must not also emit it on its own.
        var figureId = image.GetFigureId();

        if (!string.IsNullOrEmpty(figureId))
        {
            candidate.Element.Metadata[ElementMetadataKeys.IsCaptionFor] = figureId;
        }
    }

    /// <summary>
    /// Reads the document's own caption habit. Only unambiguous cases count: a numbered caption with exactly
    /// one figure close to it. A family that has not shown enough of those keeps the configured default,
    /// because guessing from two samples is worse than not guessing at all.
    /// </summary>
    /// <param name="pages">The measured pages.</param>
    /// <param name="candidatesByPage">The candidates found on each page.</param>
    /// <returns>The learned direction per caption family.</returns>
    private CaptionDirectionPrior LearnDirections(
        List<FigureCaptionPageContext> pages,
        Dictionary<int, IReadOnlyList<CaptionCandidate>> candidatesByPage)
    {
        var options = _options.Value;
        var observations = new Dictionary<string, (int Above, int Below)>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            var lineHeight = Math.Max(1, page.ModalLineHeight);
            var labelled = candidatesByPage[page.PageNumber]
                .Where(candidate => candidate.MatchedPattern && candidate.Bucket != CaptionBuckets.Unknown)
                .ToList();

            foreach (var candidate in labelled)
            {
                double[] nearest = null;
                var nearbyImages = 0;

                foreach (var image in page.Images)
                {
                    var bounds = FigureGeometry.GetBounds(image);

                    if (bounds == null || !IsNearby(bounds, candidate.Bounds, lineHeight))
                    {
                        continue;
                    }

                    nearbyImages++;
                    nearest = bounds;
                }

                // Only a one-to-one pairing teaches anything. A figure sitting between a caption above and a
                // caption below is the case the prior exists to settle, so it must not vote on itself.
                if (nearbyImages != 1)
                {
                    continue;
                }

                if (labelled.Count(other => IsNearby(nearest, other.Bounds, lineHeight)) != 1)
                {
                    continue;
                }

                var direction = FigureGeometry.GetDirection(nearest, candidate.Bounds);
                observations.TryGetValue(candidate.Bucket, out var counts);

                observations[candidate.Bucket] = direction == CaptionDirection.Above
                    ? (counts.Above + 1, counts.Below)
                    : (counts.Above, counts.Below + 1);
            }
        }

        var directions = new Dictionary<string, CaptionDirection>(StringComparer.Ordinal)
        {
            [CaptionBuckets.Figure] = options.DefaultFigureDirection,
            [CaptionBuckets.Table] = options.DefaultTableDirection,
        };

        foreach (var observation in observations)
        {
            var total = observation.Value.Above + observation.Value.Below;

            if (total < options.MinSamplesForLearnedDirection)
            {
                continue;
            }

            directions[observation.Key] = observation.Value.Above > observation.Value.Below
                ? CaptionDirection.Above
                : CaptionDirection.Below;
        }

        return new CaptionDirectionPrior(directions);
    }

    private static bool IsNearby(double[] imageBounds, double[] candidateBounds, double lineHeight)
    {
        return FigureGeometry.GetVerticalGap(imageBounds, candidateBounds) / lineHeight <= ConfidentSampleLineHeights;
    }

    /// <summary>
    /// Handles the figures no caption was assigned to. A figure printed without a caption is usually still
    /// referred to by number in the prose, and that sentence is the best thing available to say what it is —
    /// but only when the document printed a number on the figure to go looking for.
    /// </summary>
    /// <param name="pages">The measured pages.</param>
    /// <param name="captioned">The figures that already have a caption.</param>
    private void ApplyFallbacks(
        List<FigureCaptionPageContext> pages,
        HashSet<IngestionDocumentImage> captioned)
    {
        var options = _options.Value;

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var page = pages[pageIndex];

            foreach (var image in page.Images)
            {
                if (captioned.Contains(image))
                {
                    continue;
                }

                image.Metadata[FigureMetadataKeys.Bucket] = CaptionBuckets.Unknown;

                // Only a number the document printed on the figure can be looked for in the prose. How far
                // down the page the picture sits among the other pictures is a different number that merely
                // looks like one: the only image on page twelve is image one there, and matching that
                // against "Figure 1 shows ..." on page one adopts a sentence about another figure entirely.
                // A context line reads as authoritative, so an unnumbered figure is left without one.
                var figureNumber = GetPrintedFigureNumber(image);
                var reference = figureNumber.HasValue
                    ? FindInTextReference(pages, pageIndex, figureNumber.Value, options)
                    : null;

                if (reference == null)
                {
                    image.Metadata[FigureMetadataKeys.CaptionSource] = CaptionSources.None;

                    continue;
                }

                image.Metadata[FigureMetadataKeys.CaptionSource] = CaptionSources.InTextReference;
                image.Metadata[FigureMetadataKeys.Context] = Truncate(reference, options.MaxContextCharacters);
            }
        }
    }

    private static string FindInTextReference(
        List<FigureCaptionPageContext> pages,
        int pageIndex,
        int figureNumber,
        CaptionPatternOptions options)
    {
        foreach (var offset in new[] { 0, -1, 1 })
        {
            var index = pageIndex + offset;

            if (index < 0 || index >= pages.Count)
            {
                continue;
            }

            foreach (var paragraph in pages[index].Paragraphs)
            {
                var text = paragraph.Text;

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var sentence = CaptionReferenceScanner.FindSentence(text, figureNumber, CaptionBuckets.Figure, options);

                if (sentence != null)
                {
                    return sentence;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Attaches the nearest body text to every figure, so a figure carries what the page said around it even
    /// when its caption says almost nothing.
    /// </summary>
    /// <param name="pages">The measured pages.</param>
    /// <param name="candidatesByPage">The candidates found on each page.</param>
    private void ApplyContext(
        List<FigureCaptionPageContext> pages,
        Dictionary<int, IReadOnlyList<CaptionCandidate>> candidatesByPage)
    {
        var options = _options.Value;

        foreach (var page in pages)
        {
            var candidateElements = new HashSet<IngestionDocumentElement>(
                candidatesByPage[page.PageNumber].Select(candidate => candidate.Element));

            foreach (var image in page.Images)
            {
                if (image.GetMetadataString(FigureMetadataKeys.Context) != null)
                {
                    continue;
                }

                var imageBounds = FigureGeometry.GetBounds(image);

                if (imageBounds == null)
                {
                    continue;
                }

                var nearest = page.Paragraphs
                    .Where(paragraph => !candidateElements.Contains(paragraph))
                    .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text))
                    .Select(paragraph => new
                    {
                        Paragraph = paragraph,
                        Bounds = FigureGeometry.GetBounds(paragraph),
                    })
                    .Where(entry => entry.Bounds != null)
                    .OrderBy(entry => FigureGeometry.GetVerticalGap(imageBounds, entry.Bounds))
                    .Select(entry => entry.Paragraph.Text)
                    .ToList();

                if (nearest.Count == 0)
                {
                    continue;
                }

                var builder = new StringBuilder();

                foreach (var text in nearest)
                {
                    if (builder.Length >= options.MaxContextCharacters)
                    {
                        break;
                    }

                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(text);
                }

                image.Metadata[FigureMetadataKeys.Context] = Truncate(builder.ToString(), options.MaxContextCharacters);
            }
        }
    }

    /// <summary>
    /// Measures each page of the document: what is on it, and what its ordinary body text looks like.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The measured pages, in order.</returns>
    private static List<FigureCaptionPageContext> BuildPages(IngestionDocument document)
    {
        var byPage = new SortedDictionary<int, (List<IngestionDocumentImage> Images, List<IngestionDocumentElement> Paragraphs)>();

        foreach (var element in document.EnumerateContent())
        {
            var pageNumber = element.PageNumber ?? 0;

            if (!byPage.TryGetValue(pageNumber, out var entry))
            {
                entry = ([], []);
                byPage[pageNumber] = entry;
            }

            if (element is IngestionDocumentImage image)
            {
                entry.Images.Add(image);

                continue;
            }

            // A running head is short and set in small type, which is exactly what a typographic caption
            // candidate looks like. Page furniture never captions anything and never blocks a pairing.
            if (element.IsDecoration())
            {
                continue;
            }

            entry.Paragraphs.Add(element);
        }

        var pages = new List<FigureCaptionPageContext>(byPage.Count);

        // A page with no figures contributes nothing to caption assignment, but its text is still needed for
        // the cross-page in-text reference search, so every page is kept.
        foreach (var entry in byPage)
        {
            pages.Add(BuildPage(entry.Key, entry.Value.Images, entry.Value.Paragraphs));
        }

        return pages;
    }

    private static FigureCaptionPageContext BuildPage(
        int pageNumber,
        List<IngestionDocumentImage> images,
        List<IngestionDocumentElement> paragraphs)
    {
        var sizeWeights = new Dictionary<double, int>();
        var fontWeights = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var paragraph in paragraphs)
        {
            var weight = paragraph.Text?.Length ?? 0;

            if (weight == 0 || !paragraph.HasMetadata)
            {
                continue;
            }

            if (paragraph.Metadata.TryGetValue(ElementMetadataKeys.ModalPointSize, out var rawSize) && rawSize is double size)
            {
                var rounded = Math.Round(size, 2);
                sizeWeights.TryGetValue(rounded, out var existing);
                sizeWeights[rounded] = existing + weight;
            }

            var fontName = paragraph.GetMetadataString(ElementMetadataKeys.ModalFontName);

            if (!string.IsNullOrEmpty(fontName))
            {
                fontWeights.TryGetValue(fontName, out var existing);
                fontWeights[fontName] = existing + weight;
            }
        }

        var modalPointSize = sizeWeights.Count == 0
            ? 0
            : sizeWeights.OrderByDescending(entry => entry.Value).ThenByDescending(entry => entry.Key).First().Key;

        var modalFontName = fontWeights.Count == 0
            ? null
            : fontWeights.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).First().Key;

        return new FigureCaptionPageContext
        {
            PageNumber = pageNumber,
            Images = images,
            Paragraphs = paragraphs,
            ModalPointSize = modalPointSize,
            ModalFontName = modalFontName,
            ModalLineHeight = GetModalLineHeight(paragraphs, modalPointSize),
        };
    }

    /// <summary>
    /// Estimates the page's typical line height from the blocks on it, so distances can be expressed in
    /// lines rather than points and mean the same thing on any page.
    /// </summary>
    /// <param name="paragraphs">The text elements on the page.</param>
    /// <param name="modalPointSize">The page's modal font size.</param>
    /// <returns>The estimated line height.</returns>
    private static double GetModalLineHeight(List<IngestionDocumentElement> paragraphs, double modalPointSize)
    {
        var nominal = modalPointSize > 0 ? modalPointSize * 1.2 : DefaultLineHeight;
        var heights = new List<double>();

        foreach (var paragraph in paragraphs)
        {
            var bounds = FigureGeometry.GetBounds(paragraph);

            if (bounds == null)
            {
                continue;
            }

            var height = bounds[3] - bounds[1];

            if (height <= 0)
            {
                continue;
            }

            var lines = Math.Max(1, (int)Math.Round(height / nominal, MidpointRounding.AwayFromZero));

            heights.Add(height / lines);
        }

        if (heights.Count == 0)
        {
            return nominal;
        }

        heights.Sort();

        return heights[heights.Count / 2];
    }

    /// <summary>
    /// Reads the number the document printed on the figure, which a reader records when the figure carries a
    /// label it could parse. This is the number the prose refers to, and it is document-wide;
    /// <see cref="FigureMetadataKeys.ImageOrdinal"/> counts images on one page and means nothing off it.
    /// </summary>
    /// <param name="image">The figure.</param>
    /// <returns>The printed figure number, or <see langword="null"/> when the figure carries none.</returns>
    private static int? GetPrintedFigureNumber(IngestionDocumentImage image)
    {
        if (!image.HasMetadata || !image.Metadata.TryGetValue(FigureMetadataKeys.Ordinal, out var value))
        {
            return null;
        }

        return value is int figureNumber ? figureNumber : null;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength];
    }
}
