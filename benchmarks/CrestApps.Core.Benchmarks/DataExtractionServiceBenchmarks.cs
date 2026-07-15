using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures matching extracted result names to configured extraction entries.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DataExtractionMatchingBenchmarks
{
    private List<DataExtractionEntry> _entries;
    private string[] _resultNames;

    /// <summary>
    /// Gets or sets the number of configured entries and extracted results.
    /// </summary>
    [Params(10, 100)]
    public int EntryCount { get; set; }

    /// <summary>
    /// Creates configured entries and a mixed set of direct, normalized, semantic, and unknown results.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _entries = new List<DataExtractionEntry>(EntryCount);

        for (var index = 0; index < EntryCount - 2; index++)
        {
            _entries.Add(new DataExtractionEntry
            {
                Name = $"field_{index}",
                Description = $"Configured field {index}",
            });
        }

        _entries.Add(new DataExtractionEntry
        {
            Name = "customer_name",
            Description = "The customer's full name.",
        });
        _entries.Add(new DataExtractionEntry
        {
            Name = "customer_phone",
            Description = "The customer's phone number.",
        });

        _resultNames = new string[EntryCount];

        for (var index = 0; index < EntryCount; index++)
        {
            _resultNames[index] = (index % 4) switch
            {
                0 => $"field_{index % (EntryCount - 2)}",
                1 => $"field-{index % (EntryCount - 2)}",
                2 => index % 8 == 2 ? "firstName" : "phoneNumber",
                _ => $"unknown-{index}",
            };
        }
    }

    /// <summary>
    /// Matches every result with repeated configured-entry scans and normalization.
    /// </summary>
    /// <returns>The number of matched results.</returns>
    [Benchmark(Baseline = true)]
    public int MatchLegacy()
    {
        var count = 0;

        foreach (var resultName in _resultNames)
        {
            if (FindLegacy(resultName) != null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Builds one per-call index and reuses it for every extracted result.
    /// </summary>
    /// <returns>The number of matched results.</returns>
    [Benchmark]
    public int MatchOptimized()
    {
        var index = new DataExtractionService.ExtractionEntryIndex(_entries);
        var count = 0;

        foreach (var resultName in _resultNames)
        {
            if (index.Find(resultName) != null)
            {
                count++;
            }
        }

        return count;
    }

    private LegacyMatch FindLegacy(string resultName)
    {
        var directMatch = _entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, resultName, StringComparison.OrdinalIgnoreCase));

        if (directMatch != null)
        {
            return new LegacyMatch(directMatch, Classify(resultName), false);
        }

        var normalizedResultName = Normalize(resultName);

        if (string.IsNullOrEmpty(normalizedResultName))
        {
            return null;
        }

        var normalizedMatch = _entries.FirstOrDefault(entry =>
            string.Equals(Normalize(entry.Name), normalizedResultName, StringComparison.OrdinalIgnoreCase));

        if (normalizedMatch != null)
        {
            return new LegacyMatch(normalizedMatch, Classify(resultName), false);
        }

        var resultKind = Classify(resultName);

        if (resultKind == FieldKind.Unknown)
        {
            return null;
        }

        var semanticMatch = _entries.FirstOrDefault(entry => IsSemanticMatch(entry, resultKind));

        return semanticMatch == null
            ? null
            : new LegacyMatch(semanticMatch, resultKind, true);
    }

    private static bool IsSemanticMatch(DataExtractionEntry entry, FieldKind resultKind)
    {
        var entryKind = Classify(entry?.Name, entry?.Description);

        if (resultKind == FieldKind.PhoneNumber)
        {
            return entryKind == FieldKind.PhoneNumber;
        }

        if (resultKind is FieldKind.FirstName or FieldKind.LastName or FieldKind.FullName)
        {
            return entryKind == FieldKind.FullName;
        }

        return false;
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.Length == 0
            ? null
            : builder.ToString();
    }

    private static FieldKind Classify(
        string name,
        string description = null)
    {
        return ClassifyNormalized(Normalize(name), Normalize(description));
    }

    private static FieldKind ClassifyNormalized(
        string normalizedName,
        string normalizedDescription)
    {
        if (ContainsAny(normalizedName, normalizedDescription, "phone", "phonenumber", "telephone", "mobile", "cell"))
        {
            return FieldKind.PhoneNumber;
        }

        if (ContainsAny(normalizedName, normalizedDescription, "firstname", "givenname"))
        {
            return FieldKind.FirstName;
        }

        if (ContainsAny(normalizedName, normalizedDescription, "lastname", "surname", "familyname"))
        {
            return FieldKind.LastName;
        }

        if (ContainsAny(normalizedName, normalizedDescription, "fullname", "customername"))
        {
            return FieldKind.FullName;
        }

        if (!string.IsNullOrEmpty(normalizedDescription) &&
            normalizedDescription.Contains("firstname", StringComparison.Ordinal) &&
            normalizedDescription.Contains("lastname", StringComparison.Ordinal))
        {
            return FieldKind.FullName;
        }

        return FieldKind.Unknown;
    }

    private static bool ContainsAny(
        string normalizedName,
        string normalizedDescription,
        params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if ((!string.IsNullOrEmpty(normalizedName) && normalizedName.Contains(candidate, StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(normalizedDescription) && normalizedDescription.Contains(candidate, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private enum FieldKind
    {
        Unknown,
        FullName,
        FirstName,
        LastName,
        PhoneNumber,
    }

    private sealed record LegacyMatch(
        DataExtractionEntry Entry,
        FieldKind ResultFieldKind,
        bool IsSemanticAlias);
}
