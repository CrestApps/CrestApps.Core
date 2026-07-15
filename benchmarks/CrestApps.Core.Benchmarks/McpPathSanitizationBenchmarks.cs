using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures MCP remote-resource path sanitization against the implementation captured before optimization.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class McpPathSanitizationBenchmarks
{
    private string _path;

    /// <summary>
    /// Gets or sets the path sanitization scenario.
    /// </summary>
    [Params("Flat", "Nested", "NormalizeSeparators", "ManySegments")]
    public string Scenario { get; set; }

    /// <summary>
    /// Initializes the path for the selected scenario and verifies exact output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _path = Scenario switch
        {
            "Flat" => "annual-report.txt",
            "Nested" => "documents/reports/2026/annual-report.txt",
            "NormalizeSeparators" => @"\documents\\reports\2026//annual-report.txt/",
            "ManySegments" => string.Join('/', Enumerable.Range(0, 64).Select(index => $"segment-{index}")),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'."),
        };

        var legacy = SanitizeLegacy(_path);
        var current = TestResourceHandler.Sanitize(_path);

        if (!string.Equals(legacy, current, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The legacy and current path sanitizers are not equivalent.");
        }
    }

    /// <summary>
    /// Sanitizes the path with the captured split-and-join implementation.
    /// </summary>
    /// <returns>The sanitized path.</returns>
    [Benchmark(Baseline = true)]
    public string SanitizeLegacy()
    {
        return SanitizeLegacy(_path);
    }

    /// <summary>
    /// Sanitizes the path with the production implementation.
    /// </summary>
    /// <returns>The sanitized path.</returns>
    [Benchmark]
    public string SanitizeCurrent()
    {
        return TestResourceHandler.Sanitize(_path);
    }

    /// <summary>
    /// Sanitizes a path with the captured split-and-join implementation.
    /// </summary>
    /// <param name="path">The path to sanitize.</param>
    /// <returns>The sanitized path.</returns>
    private static string SanitizeLegacy(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (path.Contains('\0'))
        {
            throw new ArgumentException("Path contains invalid characters.", nameof(path));
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment == ".." || segment == ".")
            {
                throw new ArgumentException("Path must not contain directory traversal sequences.", nameof(path));
            }
        }

        return string.Join("/", segments);
    }

    /// <summary>
    /// Exposes the protected production sanitizer to the benchmark.
    /// </summary>
    private sealed class TestResourceHandler : McpResourceTypeHandlerBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestResourceHandler"/> class.
        /// </summary>
        public TestResourceHandler()
            : base("benchmark")
        {
        }

        /// <summary>
        /// Sanitizes the supplied path with the production implementation.
        /// </summary>
        /// <param name="path">The path to sanitize.</param>
        /// <returns>The sanitized path.</returns>
        public static string Sanitize(string path)
        {
            return SanitizePath(path);
        }

        /// <inheritdoc/>
        protected override Task<ReadResourceResult> GetResultAsync(
            McpResource resource,
            IReadOnlyDictionary<string, string> variables,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
