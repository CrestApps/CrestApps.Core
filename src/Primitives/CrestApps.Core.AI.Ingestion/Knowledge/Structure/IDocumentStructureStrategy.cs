using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// One way of working out what a document is made of.
/// </summary>
/// <remarks>
/// Strategies are tried in <see cref="Order"/>, and the first that has something to say answers. The order
/// is one of authority rather than preference: a document stating its own structure is never passed over for
/// this library inferring one, however confident the inference.
/// <para>
/// A host adds a strategy by registering one. A corpus with a convention of its own — a form series whose
/// first line names the section, a ledger split by a rule nobody outside the business knows — is a strategy
/// that belongs to that host and to nothing else, and it can be given an <see cref="Order"/> above or below
/// the built-in ones as the corpus demands.
/// </para>
/// </remarks>
public interface IDocumentStructureStrategy
{
    /// <summary>
    /// Gets what this strategy divides a document by, recorded on the answer it produces.
    /// See <see cref="DocumentStructureSources"/>.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Gets where this strategy sits in the order of authority. Lower runs first.
    /// </summary>
    /// <remarks>
    /// The built-in strategies leave gaps between their orders so a host can place one between them without
    /// renumbering anything.
    /// </remarks>
    int Order { get; }

    /// <summary>
    /// Divides one document.
    /// </summary>
    /// <param name="context">What is known about the document so far.</param>
    /// <returns>
    /// The divisions in document order, or an empty list when this strategy has nothing to say about this
    /// document.
    /// </returns>
    /// <remarks>
    /// Returning nothing is how a strategy declines. Nothing here may fail an ingest: a strategy that throws
    /// is logged and treated as having declined, because a document that could not be divided is still a
    /// document.
    /// </remarks>
    IReadOnlyList<DocumentArticle> Divide(DocumentStructureContext context);
}

/// <summary>
/// What is known about a document when a strategy is asked to divide it.
/// </summary>
/// <param name="Document">The ingested document.</param>
/// <param name="PageCount">How many pages it has.</param>
/// <param name="Folios">The page number printed on each page, keyed by position in the file.</param>
/// <param name="SectionLabels">The running-head label on each page, keyed by position in the file.</param>
/// <remarks>
/// Folios and labels are read once, before any strategy runs, because every strategy would otherwise read
/// them again and they say the same thing whoever asks.
/// </remarks>
public sealed record DocumentStructureContext(
    IngestionDocument Document,
    int PageCount,
    IReadOnlyDictionary<int, string> Folios,
    IReadOnlyDictionary<int, string> SectionLabels);
