using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Ingestion.Processors;

/// <summary>
/// Which direction a caption sits relative to what it captions.
/// </summary>
public enum CaptionDirection
{
    /// <summary>
    /// Not known.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The caption sits above.
    /// </summary>
    Above = 1,

    /// <summary>
    /// The caption sits below.
    /// </summary>
    Below = 2,
}

/// <summary>
/// One caption pattern and the family it identifies.
/// </summary>
public sealed class CaptionPattern
{
    /// <summary>
    /// Gets the expression a caption paragraph must match.
    /// </summary>
    public Regex Expression { get; init; }

    /// <summary>
    /// Gets the caption family the match identifies. See <see cref="CaptionBuckets"/>.
    /// </summary>
    public string Bucket { get; init; }
}

/// <summary>
/// Controls how captions are recognized and matched to what they caption.
/// </summary>
/// <remarks>
/// Patterns are data, not code, so a host can teach the processor another language without forking it.
/// </remarks>
public sealed class CaptionPatternOptions
{
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the caption patterns, seeded with defaults for the common European and East Asian spellings.
    /// </summary>
    /// <remarks>
    /// A printed, numbered caption scores higher than a block that merely sits in smaller type, and the
    /// difference is enough to decide whether a figure is transcribed at all. A language missing from this
    /// list is therefore not a cosmetic gap: its figures fall to the typography heuristic and score below
    /// the description threshold. Replace the list through <c>services.Configure</c> for a corpus in a
    /// language it does not cover.
    /// </remarks>
    public IList<CaptionPattern> Patterns { get; } =
    [
        new CaptionPattern
        {
            // English, and the Latin-script abbreviations that travel with it.
            Expression = new Regex(@"^(Figure|Fig\.|Chart|Diagram|Plate|Exhibit|Graph)\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Figure,
        },
        new CaptionPattern
        {
            Expression = new Regex(@"^Table\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Table,
        },
        new CaptionPattern
        {
            // German, Dutch, Danish/Norwegian/Swedish, Italian, Spanish, Portuguese, French, Romanian.
            Expression = new Regex(@"^(Abbildung|Abb\.|Bild|Afbeelding|Figuur|Grafiek|Figur|Figura|Grafico|Gráfico|Gráfica|Graphique|Schéma|Immagine|Imagen|Ilustração)\s*\.?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Figure,
        },
        new CaptionPattern
        {
            Expression = new Regex(@"^(Tabelle|Tabel|Tabell|Tabella|Tabla|Tabela|Tableau|Cuadro|Quadro)\s*\.?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Table,
        },
        new CaptionPattern
        {
            // Polish, Czech/Slovak, Russian and Ukrainian.
            Expression = new Regex(@"^(Rysunek|Rys\.|Wykres|Obrázek|Obr\.|Рисунок|Рис\.|Малюнок|Мал\.|График|Схема)\s*\.?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Figure,
        },
        new CaptionPattern
        {
            Expression = new Regex(@"^(Tabulka|Tab\.|Таблица|Табл\.|Таблиця)\s*\.?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Table,
        },
        new CaptionPattern
        {
            // Hungarian puts the number first: "1. abra" (figure) with an acute a.
            Expression = new Regex(@"^\d+\.\s*(ábra|kép|diagram)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Figure,
        },
        new CaptionPattern
        {
            // Hungarian: "2. tablazat" (table) with acute a characters.
            Expression = new Regex(@"^\d+\.\s*táblázat", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Table,
        },
        new CaptionPattern
        {
            // Japanese, Chinese and Korean, which take no space before the number.
            Expression = new Regex(@"^(図|圖|图|グラフ|그림)\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Figure,
        },
        new CaptionPattern
        {
            Expression = new Regex(@"^(表|表格|도표|표)\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _matchTimeout),
            Bucket = CaptionBuckets.Table,
        },
    ];

    /// <summary>
    /// Gets or sets the longest a paragraph may be and still read as a caption.
    /// </summary>
    public int MaxCaptionCharacters { get; set; } = 400;

    /// <summary>
    /// Gets or sets the cost above which a caption is not assigned at all. A wrong caption stored in an
    /// index is worse than no caption.
    /// </summary>
    public double MaxAssignmentCost { get; set; } = 6;

    /// <summary>
    /// Gets or sets how many confident observations a family needs before the document's own caption
    /// direction is trusted over the configured default.
    /// </summary>
    public int MinSamplesForLearnedDirection { get; set; } = 5;

    /// <summary>
    /// Gets or sets the direction a figure caption is assumed to sit in when the document has not shown
    /// otherwise.
    /// </summary>
    public CaptionDirection DefaultFigureDirection { get; set; } = CaptionDirection.Below;

    /// <summary>
    /// Gets or sets the direction a table caption is assumed to sit in when the document has not shown
    /// otherwise.
    /// </summary>
    public CaptionDirection DefaultTableDirection { get; set; } = CaptionDirection.Above;

    /// <summary>
    /// Gets or sets how much surrounding body text is kept with a figure.
    /// </summary>
    public int MaxContextCharacters { get; set; } = 600;
}
