using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Builds the structure analyzer the way the service registration does.
/// </summary>
/// <remarks>
/// The analyzer is only as good as the strategies it is given, so a test that built it with none would pass
/// while proving nothing. This assembles the same ladder a host gets, in the same order, and is the only
/// place a test should construct one.
/// </remarks>
internal static class StructureAnalyzers
{
    /// <summary>
    /// Creates an analyzer carrying the built-in ladder.
    /// </summary>
    /// <param name="extra">Strategies to add to the built-in ones, for tests that contribute a rung.</param>
    /// <returns>The analyzer.</returns>
    public static DocumentStructureAnalyzer Default(params IDocumentStructureStrategy[] extra)
    {
        IDocumentStructureStrategy[] builtIn =
        [
            new OutlineStructureStrategy(),
            new StatedHeadingStructureStrategy(),
            new TableOfContentsStructureStrategy(),
            new InferredHeadingStructureStrategy(),
        ];

        return new DocumentStructureAnalyzer(
            [.. builtIn, .. extra],
            NullLogger<DocumentStructureAnalyzer>.Instance);
    }
}
