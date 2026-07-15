using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures sentence-boundary detection with the legacy allocating abbreviation lookup and the
/// production implementation. This class must remain unsealed because BenchmarkDotNet generates a
/// derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[MediumRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SentenceBoundaryDetectorBenchmarks
{
    private readonly HashSet<string> _abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr.",
        "mrs.",
        "ms.",
        "dr.",
        "prof.",
        "sr.",
        "jr.",
        "etc.",
        "vs.",
    };

    private readonly string[] _streamedSequence =
    [
        "The",
        "The quick",
        "The quick brown",
        "The quick brown fox.",
        "Ask",
        "Ask Dr.",
        "Ask Dr. Smith",
        "Ask Dr. Smith now.",
        "Continue",
        "Continue with another regular sentence.",
    ];

    private readonly string _abbreviation = "Please ask Dr.";
    private readonly string _regularSentence = "This is a regular sentence.";
    private readonly string _mixedCasing = "Please ask pRoF.";
    private readonly string _longInput = $"{new string('a', 4_096)} Dr.";

    /// <summary>
    /// Detects an abbreviation with the legacy allocating implementation.
    /// </summary>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Abbreviation")]
    public bool AbbreviationLegacy()
    {
        return EndsWithSentenceBoundaryLegacy(_abbreviation);
    }

    /// <summary>
    /// Detects an abbreviation with the production implementation.
    /// </summary>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    [Benchmark]
    [BenchmarkCategory("Abbreviation")]
    public bool AbbreviationProduction()
    {
        return SentenceBoundaryDetector.EndsWithSentenceBoundary(_abbreviation);
    }

    /// <summary>
    /// Detects a regular sentence with the legacy allocating implementation.
    /// </summary>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("RegularSentence")]
    public bool RegularSentenceLegacy()
    {
        return EndsWithSentenceBoundaryLegacy(_regularSentence);
    }

    /// <summary>
    /// Detects a regular sentence with the production implementation.
    /// </summary>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    [Benchmark]
    [BenchmarkCategory("RegularSentence")]
    public bool RegularSentenceProduction()
    {
        return SentenceBoundaryDetector.EndsWithSentenceBoundary(_regularSentence);
    }

    /// <summary>
    /// Processes a streamed sequence with the legacy allocating implementation.
    /// </summary>
    /// <returns>The number of detected sentence boundaries.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("StreamedSequence")]
    public int StreamedSequenceLegacy()
    {
        var boundaries = 0;

        foreach (var text in _streamedSequence)
        {
            if (EndsWithSentenceBoundaryLegacy(text))
            {
                boundaries++;
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Processes a streamed sequence with the production implementation.
    /// </summary>
    /// <returns>The number of detected sentence boundaries.</returns>
    [Benchmark]
    [BenchmarkCategory("StreamedSequence")]
    public int StreamedSequenceProduction()
    {
        var boundaries = 0;

        foreach (var text in _streamedSequence)
        {
            if (SentenceBoundaryDetector.EndsWithSentenceBoundary(text))
            {
                boundaries++;
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Detects a mixed-case abbreviation with the legacy allocating implementation.
    /// </summary>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("MixedCasing")]
    public bool MixedCasingLegacy()
    {
        return EndsWithSentenceBoundaryLegacy(_mixedCasing);
    }

    /// <summary>
    /// Detects a mixed-case abbreviation with the production implementation.
    /// </summary>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    [Benchmark]
    [BenchmarkCategory("MixedCasing")]
    public bool MixedCasingProduction()
    {
        return SentenceBoundaryDetector.EndsWithSentenceBoundary(_mixedCasing);
    }

    /// <summary>
    /// Detects a boundary in long input with the legacy allocating implementation.
    /// </summary>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("LongInput")]
    public bool LongInputLegacy()
    {
        return EndsWithSentenceBoundaryLegacy(_longInput);
    }

    /// <summary>
    /// Detects a boundary in long input with the production implementation.
    /// </summary>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    [Benchmark]
    [BenchmarkCategory("LongInput")]
    public bool LongInputProduction()
    {
        return SentenceBoundaryDetector.EndsWithSentenceBoundary(_longInput);
    }

    /// <summary>
    /// Preserves the original sentence-boundary implementation as the benchmark baseline.
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    private bool EndsWithSentenceBoundaryLegacy(string text)
    {
        if (text is null || text.Length == 0)
        {
            return false;
        }

        return EndsWithSentenceBoundaryLegacy(text.AsSpan());
    }

    /// <summary>
    /// Preserves the original span-based sentence-boundary implementation as the benchmark baseline.
    /// </summary>
    /// <param name="span">The text span to inspect.</param>
    /// <returns>Whether the text ends with a sentence boundary.</returns>
    private bool EndsWithSentenceBoundaryLegacy(ReadOnlySpan<char> span)
    {
        span = span.TrimEnd(" \t\r");

        if (span.IsEmpty)
        {
            return false;
        }

        if (EndsWithHardBoundary(span))
        {
            if (EndsWithAbbreviationLegacy(span))
            {
                return false;
            }

            return true;
        }

        if (span.Length >= 120 && EndsWithSoftBoundary(span))
        {
            return true;
        }

        if (span.Length >= 200)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether the span ends with a hard sentence boundary.
    /// </summary>
    /// <param name="span">The text span to inspect.</param>
    /// <returns>Whether the span ends with a hard sentence boundary.</returns>
    private static bool EndsWithHardBoundary(ReadOnlySpan<char> span)
    {
        var i = span.Length - 1;

        while (i >= 0)
        {
            var c = span[i];

            if (IsTrailingWrapper(c))
            {
                i--;
                continue;
            }

            break;
        }

        if (i < 0)
        {
            return false;
        }

        return span[i] is '.' or '!' or '?' or '…' or '\n';
    }

    /// <summary>
    /// Determines whether the span ends with a soft sentence boundary.
    /// </summary>
    /// <param name="span">The text span to inspect.</param>
    /// <returns>Whether the span ends with a soft sentence boundary.</returns>
    private static bool EndsWithSoftBoundary(ReadOnlySpan<char> span)
    {
        var last = span[^1];

        return last is ',' or ';' or ':' or '-';
    }

    /// <summary>
    /// Determines whether a character is a supported trailing wrapper.
    /// </summary>
    /// <param name="character">The character to inspect.</param>
    /// <returns>Whether the character is a trailing wrapper.</returns>
    private static bool IsTrailingWrapper(char character)
    {
        return character is '"' or '\'' or ')' or ']' or '}';
    }

    /// <summary>
    /// Performs the legacy allocating abbreviation lookup.
    /// </summary>
    /// <param name="span">The text span to inspect.</param>
    /// <returns>Whether the final space-delimited word is an abbreviation.</returns>
    private bool EndsWithAbbreviationLegacy(ReadOnlySpan<char> span)
    {
        var lastSpace = span.LastIndexOf(' ');
        var lastWord = lastSpace >= 0 ? span[(lastSpace + 1)..] : span;

        return _abbreviations.Contains(lastWord.ToString());
    }
}
