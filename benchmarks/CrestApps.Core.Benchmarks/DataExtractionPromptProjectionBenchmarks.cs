using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures the local field and current-state projections used to build data-extraction prompt arguments.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DataExtractionPromptProjectionBenchmarks
{
    private const string LastUserMessage = "My updated details are Ada Lovelace, ada@example.com, and +1 425 555 0100.";
    private const string LastAssistantMessage = "Please provide your full name, email address, and preferred phone number.";
    private List<DataExtractionEntry> _fields;
    private AIChatSession _session;

    /// <summary>
    /// Gets or sets the number of configured fields and current-state entries.
    /// </summary>
    [Params(10, 100, 1_000)]
    public int EntryCount { get; set; }

    /// <summary>
    /// Gets or sets whether the current extracted state is sparse or dense.
    /// </summary>
    [Params(StateDensity.Sparse, StateDensity.Dense)]
    public StateDensity Density { get; set; }

    /// <summary>
    /// Creates realistic fields and extracted state, then verifies every projection experiment.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _fields = new List<DataExtractionEntry>(EntryCount);
        _session = new AIChatSession();

        for (var index = 0; index < EntryCount; index++)
        {
            _fields.Add(CreateEntry(index));

            var state = CreateState(index);
            _session.ExtractedData.Add($"state_{index:D4}", state);
        }

        var expected = JsonSerializer.Serialize(ProjectLegacy());

        if (!string.Equals(expected, JsonSerializer.Serialize(ProjectFieldsWithConvertAll()), StringComparison.Ordinal) ||
            !string.Equals(expected, JsonSerializer.Serialize(ProjectCurrent()), StringComparison.Ordinal) ||
            !string.Equals(expected, JsonSerializer.Serialize(ProjectSinglePassStateExperiment()), StringComparison.Ordinal) ||
            !string.Equals(expected, JsonSerializer.Serialize(ProjectWithPreSizedStateExperiment()), StringComparison.Ordinal) ||
            !string.Equals(expected, JsonSerializer.Serialize(ProjectWithCountedStateExperiment()), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prompt projection experiment changed output for {EntryCount} '{Density}' entries.");
        }
    }

    /// <summary>
    /// Projects prompt arguments using the captured LINQ Select, Where, and ToList implementation.
    /// </summary>
    /// <returns>The projected prompt argument dictionary.</returns>
    [Benchmark(Baseline = true)]
    public Dictionary<string, object> ProjectLegacy()
    {
        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["fields"] = _fields.Select(field => new
            {
                field.Name,
                field.Description,
                field.AllowMultipleValues,
                field.IsUpdatable,
            }).ToList(),
            ["currentState"] = _session.ExtractedData?
                .Where(entry => entry.Value?.Values.Count > 0)
                .Select(entry => new
                {
                    Name = entry.Key,
                    Values = entry.Value.Values,
                })
                .ToList() ?? [],
            ["lastUserMessage"] = LastUserMessage,
        };
        arguments["lastAssistantMessage"] = LastAssistantMessage;

        return arguments;
    }

    /// <summary>
    /// Projects prompt arguments using the unchanged production LINQ implementation.
    /// </summary>
    /// <returns>The projected prompt argument dictionary.</returns>
    [Benchmark]
    public Dictionary<string, object> ProjectCurrent()
    {
        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["fields"] = _fields.Select(field => new
            {
                field.Name,
                field.Description,
                field.AllowMultipleValues,
                field.IsUpdatable,
            }).ToList(),
            ["currentState"] = _session.ExtractedData?
                .Where(entry => entry.Value?.Values.Count > 0)
                .Select(entry => new
                {
                    Name = entry.Key,
                    Values = entry.Value.Values,
                })
                .ToList() ?? [],
            ["lastUserMessage"] = LastUserMessage,
        };
        arguments["lastAssistantMessage"] = LastAssistantMessage;

        return arguments;
    }

    /// <summary>
    /// Replaces only the unfiltered field projection with List.ConvertAll.
    /// </summary>
    /// <returns>The projected prompt argument dictionary.</returns>
    [Benchmark]
    public Dictionary<string, object> ProjectFieldsWithConvertAll()
    {
        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["fields"] = _fields.ConvertAll(static field => new
            {
                field.Name,
                field.Description,
                field.AllowMultipleValues,
                field.IsUpdatable,
            }),
            ["currentState"] = _session.ExtractedData?
                .Where(entry => entry.Value?.Values.Count > 0)
                .Select(entry => new
                {
                    Name = entry.Key,
                    Values = entry.Value.Values,
                })
                .ToList() ?? [],
            ["lastUserMessage"] = LastUserMessage,
        };
        arguments["lastAssistantMessage"] = LastAssistantMessage;

        return arguments;
    }

    /// <summary>
    /// Uses ConvertAll for fields and a single-pass, non-pre-sized loop for filtered current state.
    /// </summary>
    /// <returns>The projected prompt argument dictionary.</returns>
    [Benchmark]
    public Dictionary<string, object> ProjectSinglePassStateExperiment()
    {
        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["fields"] = _fields.ConvertAll(static field => new
            {
                field.Name,
                field.Description,
                field.AllowMultipleValues,
                field.IsUpdatable,
            }),
            ["currentState"] = SelectPopulatedState(
                _session.ExtractedData,
                0,
                static entry => new
                {
                    Name = entry.Key,
                    Values = entry.Value.Values,
                }),
            ["lastUserMessage"] = LastUserMessage,
        };
        arguments["lastAssistantMessage"] = LastAssistantMessage;

        return arguments;
    }

    /// <summary>
    /// Tests pre-sizing filtered current state to the full dictionary count.
    /// </summary>
    /// <returns>The projected prompt argument dictionary.</returns>
    [Benchmark]
    public Dictionary<string, object> ProjectWithPreSizedStateExperiment()
    {
        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["fields"] = _fields.ConvertAll(static field => new
            {
                field.Name,
                field.Description,
                field.AllowMultipleValues,
                field.IsUpdatable,
            }),
            ["currentState"] = SelectPopulatedState(
                _session.ExtractedData,
                _session.ExtractedData?.Count ?? 0,
                static entry => new
                {
                    Name = entry.Key,
                    Values = entry.Value.Values,
                }),
            ["lastUserMessage"] = LastUserMessage,
        };
        arguments["lastAssistantMessage"] = LastAssistantMessage;

        return arguments;
    }

    /// <summary>
    /// Tests an exact-count pass before allocating and filling filtered current state.
    /// </summary>
    /// <returns>The projected prompt argument dictionary.</returns>
    [Benchmark]
    public Dictionary<string, object> ProjectWithCountedStateExperiment()
    {
        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["fields"] = _fields.ConvertAll(static field => new
            {
                field.Name,
                field.Description,
                field.AllowMultipleValues,
                field.IsUpdatable,
            }),
            ["currentState"] = SelectPopulatedStateWithCount(
                _session.ExtractedData,
                static entry => new
                {
                    Name = entry.Key,
                    Values = entry.Value.Values,
                }),
            ["lastUserMessage"] = LastUserMessage,
        };
        arguments["lastAssistantMessage"] = LastAssistantMessage;

        return arguments;
    }

    /// <summary>
    /// Creates a realistic extraction entry containing aliases and examples.
    /// </summary>
    /// <param name="index">The entry index.</param>
    /// <returns>The configured entry.</returns>
    private static DataExtractionEntry CreateEntry(int index)
    {
        var (name, description) = (index % 5) switch
        {
            0 => (
                "customer_name",
                "Customer full name. Aliases: fullName, display name. Examples: Ada Lovelace; 李 小龍."),
            1 => (
                "email_address",
                "Primary email. Aliases: email, e-mail. Examples: ada@example.com; support+vip@example.org."),
            2 => (
                "phone_number",
                "Preferred phone. Aliases: phone, mobile, telephone. Examples: +1 425 555 0100; (702) 555-0199."),
            3 => (
                $"account_reference_{index % 17}",
                "Customer account reference. Aliases: account ID, reference. Examples: ACCT-1042; EU/2026/009."),
            _ => (
                $"custom_field_{index}",
                "Free-form profile value. Aliases: custom value. Examples: Gold; Montréal; 東京."),
        };

        return new DataExtractionEntry
        {
            Name = name,
            Description = description,
            AllowMultipleValues = index % 3 == 0,
            IsUpdatable = index % 2 == 0,
        };
    }

    /// <summary>
    /// Creates extracted state at the configured density, including null and empty states.
    /// </summary>
    /// <param name="index">The state index.</param>
    /// <returns>The extracted field state.</returns>
    private ExtractedFieldState CreateState(int index)
    {
        if (index % 41 == 0)
        {
            return null;
        }

        var isPopulated = Density == StateDensity.Dense
            ? index % 10 != 0
            : index % 10 == 0;

        if (!isPopulated)
        {
            return new ExtractedFieldState();
        }

        return new ExtractedFieldState
        {
            Values = (index % 3) switch
            {
                0 => ["Ada Lovelace", "李 小龍"],
                1 => ["ada@example.com"],
                _ => ["+1 425 555 0100", "(702) 555-0199", null],
            },
        };
    }

    /// <summary>
    /// Projects populated state entries in one pass with the requested initial capacity.
    /// </summary>
    /// <typeparam name="TResult">The anonymous projection type.</typeparam>
    /// <param name="source">The extracted state dictionary.</param>
    /// <param name="capacity">The initial result capacity.</param>
    /// <param name="selector">The projection selector.</param>
    /// <returns>The projected populated state.</returns>
    private static List<TResult> SelectPopulatedState<TResult>(
        IReadOnlyDictionary<string, ExtractedFieldState> source,
        int capacity,
        Func<KeyValuePair<string, ExtractedFieldState>, TResult> selector)
    {
        if (source is null)
        {
            return [];
        }

        var results = new List<TResult>(capacity);

        foreach (var entry in source)
        {
            if (entry.Value?.Values.Count > 0)
            {
                results.Add(selector(entry));
            }
        }

        return results;
    }

    /// <summary>
    /// Counts populated state entries before allocating and filling the exact-sized result list.
    /// </summary>
    /// <typeparam name="TResult">The anonymous projection type.</typeparam>
    /// <param name="source">The extracted state dictionary.</param>
    /// <param name="selector">The projection selector.</param>
    /// <returns>The projected populated state.</returns>
    private static List<TResult> SelectPopulatedStateWithCount<TResult>(
        IReadOnlyDictionary<string, ExtractedFieldState> source,
        Func<KeyValuePair<string, ExtractedFieldState>, TResult> selector)
    {
        if (source is null)
        {
            return [];
        }

        var count = 0;

        foreach (var entry in source)
        {
            if (entry.Value?.Values.Count > 0)
            {
                count++;
            }
        }

        return SelectPopulatedState(source, count, selector);
    }

    /// <summary>
    /// Identifies the proportion of current-state entries containing values.
    /// </summary>
    public enum StateDensity
    {
        /// <summary>
        /// Approximately ten percent of entries contain extracted values.
        /// </summary>
        Sparse,

        /// <summary>
        /// Approximately ninety percent of entries contain extracted values.
        /// </summary>
        Dense,
    }
}
