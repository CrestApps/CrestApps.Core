namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Controls which figures are kept and which are worth describing.
/// </summary>
/// <remarks>
/// Most images in a printed publication are logos, advertising artwork or decoration. Describing all of them
/// costs real money and buys nothing, so figures are scored from signals that are cheap to compute and then
/// sorted into three tiers.
/// </remarks>
public sealed class FigureSalienceOptions
{
    /// <summary>
    /// Gets or sets the score below which a figure is dropped outright. The default of <c>1</c> means a
    /// figure nothing said anything about is not kept.
    /// </summary>
    public int CaptionOnlyThreshold { get; set; } = 1;

    /// <summary>
    /// Gets or sets what a caption that matched a numbered pattern is worth. A printed label is the
    /// strongest evidence available that the artwork beside it is a real figure.
    /// </summary>
    public int PatternCaptionScore { get; set; } = 3;

    /// <summary>
    /// Gets or sets what a caption recognized only by its typography is worth. Small type near a picture is
    /// suggestive, not conclusive, so it keeps the figure without paying for a model call.
    /// </summary>
    public int TypographyCaptionScore { get; set; } = 2;

    /// <summary>
    /// Gets or sets what a mention of the figure by number in the body text is worth.
    /// </summary>
    public int CitedInProseScore { get; set; } = 2;

    /// <summary>
    /// Gets or sets the score at which a figure becomes worth transcribing.
    /// </summary>
    public int DescribeThreshold { get; set; } = 3;

    /// <summary>
    /// Gets or sets the smallest a figure can be, on either axis, without being penalized as decoration.
    /// </summary>
    public int MinDescribableSamples { get; set; } = 150;

    /// <summary>
    /// Gets or sets the aspect ratio above which a figure reads as a rule or a banner rather than artwork.
    /// </summary>
    public double MaxAspectRatio { get; set; } = 6;

    /// <summary>
    /// Gets or sets how many pages the same artwork has to appear on before it reads as page furniture.
    /// </summary>
    public int RepeatPageThreshold { get; set; } = 3;

    /// <summary>
    /// Gets or sets the share of the page a figure must cover to count as full bleed.
    /// </summary>
    public double FullBleedPageAreaRatio { get; set; } = 0.8;

    /// <summary>
    /// Gets or sets how little text a full-bleed page must carry before it reads as an advertisement.
    /// </summary>
    public int FullBleedMaxPageCharacters { get; set; } = 200;

    /// <summary>
    /// Gets or sets how much is taken off the score of artwork that repeats across pages.
    /// </summary>
    /// <remarks>
    /// The penalties are separate settings from the signals above because they are what a corpus most often
    /// needs to soften. A deck whose every page carries the same brand mark wants this high; a catalogue
    /// that legitimately reprints the same product photo wants it low.
    /// </remarks>
    public int RepeatedArtworkPenalty { get; set; } = 4;

    /// <summary>
    /// Gets or sets how much is taken off the score of a figure too small or too elongated to be artwork.
    /// </summary>
    public int SmallOrExtremePenalty { get; set; } = 2;

    /// <summary>
    /// Gets or sets how much is taken off the score of a full-bleed image on a page with almost no text.
    /// </summary>
    public int FullBleedPenalty { get; set; } = 3;

    /// <summary>
    /// Gets or sets the share of a document's pages that have to carry no text at all before the document
    /// reads as a scan, whose pages are pictures of text rather than pictures on a page.
    /// </summary>
    /// <remarks>
    /// A scanned document has no text layer: every page is one image and nothing captions it, so every
    /// signal above would drop it and nothing would be indexed at all. When most pages look like that, a
    /// full-page image is the page, and reading it is the only way the document becomes searchable.
    /// </remarks>
    public double ScannedDocumentPageRatio { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets how few characters a page may carry and still count as having no text layer.
    /// </summary>
    public int ScannedPageMaxCharacters { get; set; } = 40;

    /// <summary>
    /// Gets or sets what a full-page image on a scanned page is worth. The default lands it in the describe
    /// tier, because transcribing the page is the point.
    /// </summary>
    public int ScannedPageScore { get; set; } = 3;
}
