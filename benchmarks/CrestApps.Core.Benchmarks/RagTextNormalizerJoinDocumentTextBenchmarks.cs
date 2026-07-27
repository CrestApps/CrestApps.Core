using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the production LINQ-based document text join with collection and builder candidates.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class RagTextNormalizerJoinDocumentTextBenchmarks
{
    private const string ShortParagraphs = "ShortParagraphs";
    private const string TwoKilobyteSections = "TwoKilobyteSections";
    private const string NullEmptyWhitespaceMix = "NullEmptyWhitespaceMix";
    private const string RealisticMarkdownNormalized = "RealisticMarkdownNormalized";

    private const string Separator = "\n";
    private static readonly string[] _realisticMarkdownNormalizedContent =
    [
        "Document processing overview",
        "CrestApps.Core reads source documents, normalizes their content, and creates token-aware chunks for retrieval.",
        "Register Markdown normalization explicitly when Markdig-backed parsing is required.",
        "Add Open XML and PDF readers separately because those document formats are opt-in integrations.",
        "Configuration\nUse the document-processing builder to register stores, readers, and reference downloads.",
        "The normalized content preserves meaningful inline text while removing Markdown formatting.",
        "Search results can include downloadable references when reference downloads and the endpoint are enabled.",
        "Validate provider settings in the host UI so optional integrations remain unavailable instead of failing startup.",
    ];

    private IngestionDocument _document;

    /// <summary>
    /// Gets or sets the number of document elements.
    /// </summary>
    [Params(10, 100, 1_000, 10_000)]
    public int ElementCount { get; set; }

    /// <summary>
    /// Gets or sets the document content scenario.
    /// </summary>
    [Params(ShortParagraphs, TwoKilobyteSections, NullEmptyWhitespaceMix, RealisticMarkdownNormalized)]
    public string Scenario { get; set; }

    /// <summary>
    /// Creates the parser-free ingestion document and verifies candidate equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _document = CreateDocument(ElementCount, Scenario);

        var legacy = JoinLegacy();
        var materializedList = JoinMaterializedListCandidate();
        var manualBuilder = JoinManualBuilder();

        if (!string.Equals(legacy, materializedList, StringComparison.Ordinal)
            || !string.Equals(legacy, manualBuilder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The document text joins produced different results for '{Scenario}' with {ElementCount} elements.");
        }
    }

    /// <summary>
    /// Joins document text using the captured LINQ projection and <see cref="string.Join(string?, IEnumerable{string?})"/>.
    /// </summary>
    /// <returns>The joined document text.</returns>
    [Benchmark(Baseline = true)]
    public string JoinLegacy()
    {
        return string.Join(
            Separator,
            _document.EnumerateContent()
                .Select(element => element.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>
    /// Joins document text by materializing filtered values for the optimized <see cref="string.Join(string?, IEnumerable{string?})"/> list path.
    /// </summary>
    /// <returns>The joined document text.</returns>
    [Benchmark]
    public string JoinMaterializedListCandidate()
    {
        var values = new List<string>();

        foreach (var element in _document.EnumerateContent())
        {
            var text = element.Text;

            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text);
            }
        }

        return string.Join(Separator, values);
    }

    /// <summary>
    /// Joins document text with a single-pass <see cref="StringBuilder"/>.
    /// </summary>
    /// <returns>The joined document text.</returns>
    [Benchmark]
    public string JoinManualBuilder()
    {
        var builder = new StringBuilder();
        var hasText = false;

        foreach (var element in _document.EnumerateContent())
        {
            var text = element.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (hasText)
            {
                builder.Append('\n');
            }

            builder.Append(text);
            hasText = true;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates an ingestion document for the requested benchmark scenario.
    /// </summary>
    /// <param name="elementCount">The number of content elements.</param>
    /// <param name="scenario">The content scenario.</param>
    /// <returns>The populated ingestion document.</returns>
    private static IngestionDocument CreateDocument(int elementCount, string scenario)
    {
        var document = new IngestionDocument($"benchmark-{scenario}-{elementCount}");
        var section = new IngestionDocumentSection();
        var twoKilobyteSection = scenario == TwoKilobyteSections
            ? RepeatToLength(
                "This normalized section contains deployment guidance, configuration details, examples, and operational notes for document retrieval. ",
                2 * 1024)
            : null;

        for (var index = 0; index < elementCount; index++)
        {
            var text = scenario switch
            {
                ShortParagraphs =>
                    $"Paragraph {index}: Configure document processing, normalize the source text, and index the resulting chunks.",
                TwoKilobyteSections => twoKilobyteSection,
                NullEmptyWhitespaceMix => (index % 8) switch
                {
                    0 => null,
                    1 => string.Empty,
                    2 => "   ",
                    3 => "\t",
                    4 => "\r\n",
                    _ => $"Section {index}: Retained normalized content for the mixed-value ingestion document.",
                },
                RealisticMarkdownNormalized =>
                    _realisticMarkdownNormalizedContent[index % _realisticMarkdownNormalizedContent.Length],
                _ => throw new InvalidOperationException($"Unknown scenario '{scenario}'."),
            };

            section.Elements.Add(new IngestionDocumentParagraph($"element-{index}")
            {
                Text = text,
            });
        }

        document.Sections.Add(section);

        return document;
    }

    /// <summary>
    /// Repeats and truncates text to an exact UTF-16 length.
    /// </summary>
    /// <param name="value">The text to repeat.</param>
    /// <param name="length">The required UTF-16 length.</param>
    /// <returns>The exact-length repeated text.</returns>
    private static string RepeatToLength(string value, int length)
    {
        var builder = new StringBuilder(length);

        while (builder.Length < length)
        {
            var remaining = length - builder.Length;
            builder.Append(value.AsSpan(0, Math.Min(value.Length, remaining)));
        }

        return builder.ToString();
    }
}
