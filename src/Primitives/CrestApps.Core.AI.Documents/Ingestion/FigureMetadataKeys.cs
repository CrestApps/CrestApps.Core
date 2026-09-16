namespace CrestApps.Core.AI.Documents.Ingestion;

/// <summary>
/// The metadata keys readers and processors use on an <see cref="Microsoft.Extensions.DataIngestion.IngestionDocumentImage"/>.
/// Every key is written and read through these constants so no string literal can drift.
/// </summary>
public static class FigureMetadataKeys
{
    /// <summary>
    /// The figure identifier, shaped <c>{documentIdentifier}-p{page}-{imageOrdinal}</c> so it is stable
    /// across repeated reads of the same file.
    /// </summary>
    public const string Id = "crestapps.figure.id";

    /// <summary>
    /// The lowercase hexadecimal SHA-256 hash of the figure bytes, used to detect repeats and to key the
    /// description cache.
    /// </summary>
    public const string ContentHash = "crestapps.figure.contentHash";

    /// <summary>
    /// The one-based position of the figure among the images on its page.
    /// </summary>
    public const string ImageOrdinal = "crestapps.figure.imageOrdinal";

    /// <summary>
    /// The figure width, in image samples.
    /// </summary>
    public const string PixelWidth = "crestapps.figure.pixelWidth";

    /// <summary>
    /// The figure height, in image samples.
    /// </summary>
    public const string PixelHeight = "crestapps.figure.pixelHeight";

    /// <summary>
    /// Whether the figure was drawn inline in the content stream rather than referenced as an object.
    /// </summary>
    public const string IsInlineImage = "crestapps.figure.isInlineImage";

    /// <summary>
    /// The identifier of the figure this one repeats, when the same bytes appear more than once.
    /// </summary>
    public const string DuplicateOf = "crestapps.figure.duplicateOf";

    /// <summary>
    /// The caption assigned to the figure.
    /// </summary>
    public const string Caption = "crestapps.figure.caption";

    /// <summary>
    /// How the caption was found: <c>pattern</c>, <c>typography</c>, <c>inTextReference</c> or <c>none</c>.
    /// </summary>
    public const string CaptionSource = "crestapps.figure.captionSource";

    /// <summary>
    /// The surrounding body text that gives the figure its meaning.
    /// </summary>
    public const string Context = "crestapps.figure.context";

    /// <summary>
    /// The caption family the figure belongs to: <c>figure</c>, <c>table</c> or <c>unknown</c>.
    /// </summary>
    public const string Bucket = "crestapps.figure.bucket";

    /// <summary>
    /// The number parsed out of the caption, when the caption is numbered.
    /// </summary>
    public const string Ordinal = "crestapps.figure.ordinal";

    /// <summary>
    /// The salience outcome for the figure: <c>Skip</c>, <c>CaptionOnly</c> or <c>Describe</c>.
    /// </summary>
    public const string Tier = "crestapps.figure.tier";

    /// <summary>
    /// The score the salience signals added up to.
    /// </summary>
    public const string SalienceScore = "crestapps.figure.salienceScore";

    /// <summary>
    /// Where the figure description came from, for example <c>vision</c>.
    /// </summary>
    public const string DescriptionSource = "crestapps.figure.descriptionSource";

    /// <summary>
    /// The deployment that produced the figure description.
    /// </summary>
    public const string DescriptionModel = "crestapps.figure.descriptionModel";

    /// <summary>
    /// The version of the transcription prompt that produced the description. It is part of the cache key, so
    /// a changed prompt re-describes rather than serving a stale description.
    /// </summary>
    public const string DescriptionPromptVersion = "crestapps.figure.descriptionPromptVersion";

    /// <summary>
    /// How much the values read off a chart can be trusted: <c>Exact</c>, <c>AxesOnly</c> or <c>Descriptive</c>.
    /// </summary>
    public const string ValueConfidence = "crestapps.figure.valueConfidence";

    /// <summary>
    /// Where the figure bytes were stored, so a client can fetch the image back.
    /// </summary>
    public const string StoragePath = "crestapps.figure.storagePath";

    /// <summary>
    /// Marks a figure the page drew rather than placed, so a reader knows the picture was rendered from
    /// geometry rather than extracted.
    /// </summary>
    public const string IsVectorFigure = "crestapps.figure.isVectorFigure";

    /// <summary>
    /// The chart series read off the figure's own geometry, as JSON. Present only when the values came from
    /// the document rather than from anyone's reading of a picture.
    /// </summary>
    public const string ChartSeries = "crestapps.figure.chartSeries";

    /// <summary>
    /// The kind of chart, when the way it was drawn says what kind it is. Absent when it does not: a chart
    /// typed by guesswork tells a reader something the document never said.
    /// </summary>
    public const string ChartType = "crestapps.figure.chartType";

    /// <summary>
    /// The horizontal axis title, as the chart prints it.
    /// </summary>
    public const string ChartAxisX = "crestapps.figure.chartAxisX";

    /// <summary>
    /// The vertical axis title, as the chart prints it.
    /// </summary>
    public const string ChartAxisY = "crestapps.figure.chartAxisY";
}
