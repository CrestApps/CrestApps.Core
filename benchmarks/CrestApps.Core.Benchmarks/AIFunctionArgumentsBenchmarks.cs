using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Extensions;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures typed values read from JSON-backed AI function arguments.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class AIFunctionArgumentsBenchmarks
{
    private JsonDocument _document;
    private AIFunctionArguments _arguments;

    /// <summary>
    /// Creates representative JSON-backed function arguments.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _document = JsonDocument.Parse("""
            {
              "number": 42,
              "person": {
                "name": "Ada",
                "age": 37
              },
              "items": ["one", "two", "three", "four"]
            }
            """);

        _arguments = new AIFunctionArguments
        {
            ["number"] = _document.RootElement.GetProperty("number"),
            ["person"] = _document.RootElement.GetProperty("person"),
            ["items"] = _document.RootElement.GetProperty("items"),
        };
    }

    /// <summary>
    /// Disposes the JSON document.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _document.Dispose();
    }

    /// <summary>
    /// Reads an integer argument.
    /// </summary>
    /// <returns>The converted integer.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Int32")]
    public int ReadInt32Buffered()
    {
        TryGetFirstLegacy<int>(_arguments, "number", out var value);

        return value;
    }

    /// <summary>
    /// Reads an integer argument without materializing raw JSON text.
    /// </summary>
    /// <returns>The converted integer.</returns>
    [Benchmark]
    [BenchmarkCategory("Int32")]
    public int ReadInt32Optimized()
    {
        _arguments.TryGetFirst<int>("number", out var value);

        return value;
    }

    /// <summary>
    /// Reads an object argument.
    /// </summary>
    /// <returns>The converted object.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Object")]
    public Person ReadObjectBuffered()
    {
        TryGetFirstLegacy<Person>(_arguments, "person", out var value);

        return value;
    }

    /// <summary>
    /// Reads an object argument without materializing raw JSON text.
    /// </summary>
    /// <returns>The converted object.</returns>
    [Benchmark]
    [BenchmarkCategory("Object")]
    public Person ReadObjectOptimized()
    {
        _arguments.TryGetFirst<Person>("person", out var value);

        return value;
    }

    /// <summary>
    /// Reads an array argument.
    /// </summary>
    /// <returns>The converted array.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Array")]
    public string[] ReadArrayBuffered()
    {
        TryGetFirstLegacy<string[]>(_arguments, "items", out var value);

        return value;
    }

    /// <summary>
    /// Reads an array argument without materializing raw JSON text.
    /// </summary>
    /// <returns>The converted array.</returns>
    [Benchmark]
    [BenchmarkCategory("Array")]
    public string[] ReadArrayOptimized()
    {
        _arguments.TryGetFirst<string[]>("items", out var value);

        return value;
    }

    /// <summary>
    /// Preserves the original raw-text JSON conversion as a benchmark baseline.
    /// </summary>
    /// <typeparam name="T">The target value type.</typeparam>
    /// <param name="arguments">The function arguments.</param>
    /// <param name="key">The argument key.</param>
    /// <param name="value">Receives the converted value.</param>
    /// <returns><see langword="true"/> when conversion succeeds.</returns>
    private static bool TryGetFirstLegacy<T>(
        AIFunctionArguments arguments,
        string key,
        out T value)
    {
        value = default;

        if (!arguments.TryGetValue(key, out var unsafeValue) || unsafeValue is null)
        {
            return false;
        }

        if (unsafeValue is T alreadyTyped)
        {
            value = alreadyTyped;

            return true;
        }

        if (unsafeValue is JsonElement jsonElement)
        {
            value = JsonSerializer.Deserialize<T>(
                jsonElement.GetRawText(),
                JSOptions.CaseInsensitive);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Represents a benchmark function argument.
    /// </summary>
    public sealed class Person
    {
        /// <summary>
        /// Gets or sets the person's name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the person's age.
        /// </summary>
        public int Age { get; set; }
    }
}
