using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// Works out what a document is made of by asking each registered strategy in turn.
/// </summary>
/// <remarks>
/// Strategies are asked in their own order, which is one of authority: a document stating its own structure
/// is never passed over for this library inferring one. The first with something to say answers, and the
/// answer records which one it was.
/// <para>
/// Nothing here may fail an ingest. A strategy that throws is logged and treated as having declined, and a
/// document no strategy could divide is one division covering all of it — which is exactly what it was
/// before any of this existed.
/// </para>
/// </remarks>
public sealed class DocumentStructureAnalyzer : IDocumentStructureAnalyzer
{
    private readonly IDocumentStructureStrategy[] _strategies;
    private readonly ILogger<DocumentStructureAnalyzer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentStructureAnalyzer"/> class.
    /// </summary>
    /// <param name="strategies">The strategies to ask, in any order.</param>
    /// <param name="logger">The logger.</param>
    public DocumentStructureAnalyzer(
        IEnumerable<IDocumentStructureStrategy> strategies,
        ILogger<DocumentStructureAnalyzer> logger)
    {
        ArgumentNullException.ThrowIfNull(strategies);

        _strategies = [.. strategies.OrderBy(strategy => strategy.Order)];
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentStructure Analyze(IngestionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pageCount = document.Sections.Count;

        if (pageCount == 0)
        {
            return DocumentStructureRungs.Single(document, 0);
        }

        Dictionary<int, string> folios;
        Dictionary<int, string> labels;

        try
        {
            folios = DocumentStructureRungs.CaptureFolios(document);
            labels = DocumentStructureRungs.CaptureSectionLabels(document);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the page furniture of '{Identifier}'. The document is treated as one article.", document.Identifier);

            return DocumentStructureRungs.Single(document, pageCount);
        }

        var context = new DocumentStructureContext(document, pageCount, folios, labels);

        foreach (var strategy in _strategies)
        {
            IReadOnlyList<DocumentArticle> articles;

            try
            {
                articles = strategy.Divide(context);
            }
            catch (Exception ex)
            {
                // One strategy failing is not the document's fault, and the rung below it may well have an
                // answer. Declining is the same outcome either way.
                _logger.LogWarning(
                    ex,
                    "The '{Source}' structure strategy failed for '{Identifier}'. The next strategy is asked instead.",
                    strategy.Source,
                    document.Identifier);

                continue;
            }

            if (articles is not { Count: > 0 })
            {
                continue;
            }

            var divided = articles as List<DocumentArticle> ?? [.. articles];

            DocumentStructureRungs.Stamp(document, divided, folios, labels);

            return new DocumentStructure
            {
                Articles = divided,
                Folios = folios,
                IsInferred = true,
                Source = strategy.Source,
            };
        }

        return DocumentStructureRungs.Single(document, pageCount, folios);
    }
}
