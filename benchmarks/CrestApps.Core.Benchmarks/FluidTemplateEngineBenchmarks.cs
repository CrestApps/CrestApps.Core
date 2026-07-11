using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Templates.Rendering;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures rendered-template whitespace normalization.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class FluidTemplateEngineBenchmarks
{
    private string _text;

    /// <summary>
    /// Gets or sets the number of non-empty lines in the input.
    /// </summary>
    [Params(100, 1000)]
    public int LineCount { get; set; }

    /// <summary>
    /// Creates representative multiline template output.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder(LineCount * 32);

        for (var i = 0; i < LineCount; i++)
        {
            builder.Append("   Line ");
            builder.Append(i);
            builder.Append(" with content   \r\n");

            if (i % 5 == 0)
            {
                builder.Append(" \t\r\n\r\n");
            }
        }

        _text = builder.ToString();
    }

    /// <summary>
    /// Normalizes the representative rendered output with the original split-and-join implementation.
    /// </summary>
    /// <returns>The normalized output.</returns>
    [Benchmark(Baseline = true)]
    public string NormalizeWhitespaceBuffered()
    {
        return NormalizeWhitespaceLegacy(_text);
    }

    /// <summary>
    /// Normalizes the representative rendered output with the optimized implementation.
    /// </summary>
    /// <returns>The normalized output.</returns>
    [Benchmark]
    public string NormalizeWhitespaceOptimized()
    {
        return FluidTemplateEngine.NormalizeWhitespace(_text);
    }

    /// <summary>
    /// Preserves the original normalization algorithm as the benchmark baseline.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The normalized text.</returns>
    private static string NormalizeWhitespaceLegacy(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lines = text.Split('\n');
        var builder = new List<string>(lines.Length);
        var previousWasBlank = true;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimEnd('\r').Trim();

            if (trimmed.Length == 0)
            {
                if (!previousWasBlank)
                {
                    builder.Add(string.Empty);
                    previousWasBlank = true;
                }

                continue;
            }

            builder.Add(trimmed);
            previousWasBlank = false;
        }

        while (builder.Count > 0 && builder[^1].Length == 0)
        {
            builder.RemoveAt(builder.Count - 1);
        }

        return string.Join('\n', builder);
    }
}
