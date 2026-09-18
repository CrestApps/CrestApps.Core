using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Ingestion.Processors;

/// <summary>
/// Finds caption candidates two ways: a paragraph that matches a configured caption pattern, and a short
/// paragraph set in noticeably different type from the body around it.
/// </summary>
public sealed class DefaultFigureCaptionCandidateDetector : IFigureCaptionCandidateDetector
{
    private static readonly Regex _ordinalRegex = new(@"\d+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly IOptions<CaptionPatternOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultFigureCaptionCandidateDetector"/> class.
    /// </summary>
    /// <param name="options">The caption options.</param>
    public DefaultFigureCaptionCandidateDetector(IOptions<CaptionPatternOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Finds the caption candidates on one page.
    /// </summary>
    /// <param name="page">The measured page.</param>
    /// <returns>The candidates, in page order.</returns>
    public IReadOnlyList<CaptionCandidate> GetCandidates(FigureCaptionPageContext page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var options = _options.Value;
        var candidates = new List<CaptionCandidate>();

        foreach (var element in page.Paragraphs)
        {
            var text = element.Text?.Trim();

            if (string.IsNullOrEmpty(text) || text.Length > options.MaxCaptionCharacters)
            {
                continue;
            }

            var bounds = GetBounds(element);

            if (bounds == null)
            {
                continue;
            }

            var pattern = Match(options, text);

            if (pattern != null)
            {
                candidates.Add(new CaptionCandidate
                {
                    Element = element,
                    Text = text,
                    Bucket = pattern.Bucket,
                    MatchedPattern = true,
                    Ordinal = ParseOrdinal(text),
                    Bounds = bounds,
                });

                continue;
            }

            if (IsTypographicallyDistinct(page, element))
            {
                candidates.Add(new CaptionCandidate
                {
                    Element = element,
                    Text = text,
                    Bucket = CaptionBuckets.Unknown,
                    MatchedPattern = false,
                    Ordinal = null,
                    Bounds = bounds,
                });
            }
        }

        return candidates;
    }

    private static CaptionPattern Match(CaptionPatternOptions options, string text)
    {
        foreach (var pattern in options.Patterns)
        {
            if (pattern?.Expression == null)
            {
                continue;
            }

            try
            {
                if (pattern.Expression.IsMatch(text))
                {
                    return pattern;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // A host-supplied pattern that cannot keep up is skipped rather than allowed to stall an
                // ingest. Captions are an enrichment; nothing downstream requires them.
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a paragraph is set apart from the page's ordinary body text, which is how an
    /// unnumbered caption announces itself.
    /// </summary>
    /// <param name="page">The measured page.</param>
    /// <param name="element">The paragraph.</param>
    /// <returns><see langword="true"/> when the paragraph is smaller than, or set in a different face from, the body.</returns>
    private static bool IsTypographicallyDistinct(FigureCaptionPageContext page, Microsoft.Extensions.DataIngestion.IngestionDocumentElement element)
    {
        if (page.ModalPointSize > 0 &&
            element.HasMetadata &&
            element.Metadata.TryGetValue(ElementMetadataKeys.ModalPointSize, out var rawSize) &&
            rawSize is double size &&
            size <= page.ModalPointSize * 0.9)
        {
            return true;
        }

        var fontName = element.GetMetadataString(ElementMetadataKeys.ModalFontName);

        return !string.IsNullOrEmpty(fontName) &&
            !string.IsNullOrEmpty(page.ModalFontName) &&
            !string.Equals(fontName, page.ModalFontName, StringComparison.Ordinal);
    }

    private static int? ParseOrdinal(string text)
    {
        var match = _ordinalRegex.Match(text);

        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
            ? ordinal
            : null;
    }

    private static double[] GetBounds(Microsoft.Extensions.DataIngestion.IngestionDocumentElement element)
    {
        if (!element.HasMetadata || !element.Metadata.TryGetValue(ElementMetadataKeys.BoundingBox, out var value))
        {
            return null;
        }

        return value is double[] bounds && bounds.Length == 4 ? bounds : null;
    }
}
