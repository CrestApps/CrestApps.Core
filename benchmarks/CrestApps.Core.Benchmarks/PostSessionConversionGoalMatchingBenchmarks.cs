using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares repeated conversion-goal scans with optimized per-call matching.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class PostSessionConversionGoalMatchingBenchmarks
{
    private const string AllMatchedScenario = "AllMatched";
    private const string SparseMatchedScenario = "SparseMatched";
    private const string DuplicatesScenario = "Duplicates";
    private const string CaseVariantsScenario = "CaseVariants";
    private const string NullEntriesFallbackScenario = "NullEntriesFallback";

    private List<ConversionGoal> _configuredGoals;
    private List<BenchmarkEvaluationResult> _returnedGoals;

    /// <summary>
    /// Gets or sets the number of configured and returned goals.
    /// </summary>
    [Params(10, 100, 1_000, 10_000)]
    public int GoalCount { get; set; }

    /// <summary>
    /// Gets or sets the matching distribution.
    /// </summary>
    [Params(
        AllMatchedScenario,
        SparseMatchedScenario,
        DuplicatesScenario,
        CaseVariantsScenario,
        NullEntriesFallbackScenario)]
    public string Scenario { get; set; }

    /// <summary>
    /// Creates configured and returned goals and verifies exact mapping equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        (_configuredGoals, _returnedGoals) = Scenario switch
        {
            AllMatchedScenario => CreateAllMatched(GoalCount),
            SparseMatchedScenario => CreateSparseMatched(GoalCount),
            DuplicatesScenario => CreateDuplicates(GoalCount),
            CaseVariantsScenario => CreateCaseVariants(GoalCount),
            NullEntriesFallbackScenario => CreateNullEntriesFallback(GoalCount),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'."),
        };

        if (_configuredGoals.Count != GoalCount || _returnedGoals.Count != GoalCount)
        {
            throw new InvalidOperationException("The benchmark must use equal configured and returned goal counts.");
        }

        VerifyEquivalent(
            MapLegacy(_configuredGoals, _returnedGoals),
            MapCurrent(_configuredGoals, _returnedGoals));
        VerifyExceptionalSemantics();
    }

    /// <summary>
    /// Maps returned goals by scanning configured goals for every result.
    /// </summary>
    /// <returns>The mapped conversion-goal results.</returns>
    [Benchmark(Baseline = true)]
    public List<ConversionGoalResult> MapLegacy()
    {
        return MapLegacy(_configuredGoals, _returnedGoals);
    }

    /// <summary>
    /// Maps returned goals with a per-call lookup or the compatibility fallback.
    /// </summary>
    /// <returns>The mapped conversion-goal results.</returns>
    [Benchmark]
    public List<ConversionGoalResult> MapCurrent()
    {
        return MapCurrent(_configuredGoals, _returnedGoals);
    }

    /// <summary>
    /// Creates unique configured goals with every returned goal matched in reverse order.
    /// </summary>
    /// <param name="goalCount">The number of configured and returned goals.</param>
    /// <returns>The benchmark inputs.</returns>
    private static (List<ConversionGoal> Configured, List<BenchmarkEvaluationResult> Returned) CreateAllMatched(
        int goalCount)
    {
        var configured = CreateUniqueGoals(goalCount, "goal");
        var returned = new List<BenchmarkEvaluationResult>(goalCount);

        for (var index = 0; index < goalCount; index++)
        {
            var configuredIndex = goalCount - index - 1;
            returned.Add(CreateReturnedGoal(
                configured[configuredIndex].Name,
                index,
                configuredIndex));
        }

        return (configured, returned);
    }

    /// <summary>
    /// Creates unique configured goals with ten percent matched and ninety percent unmatched results.
    /// </summary>
    /// <param name="goalCount">The number of configured and returned goals.</param>
    /// <returns>The benchmark inputs.</returns>
    private static (List<ConversionGoal> Configured, List<BenchmarkEvaluationResult> Returned) CreateSparseMatched(
        int goalCount)
    {
        var configured = CreateUniqueGoals(goalCount, "sparse");
        var returned = new List<BenchmarkEvaluationResult>(goalCount);

        for (var index = 0; index < goalCount; index++)
        {
            var isMatched = index % 10 == 0;
            var configuredIndex = goalCount - index - 1;
            var name = isMatched
                ? configured[configuredIndex].Name
                : $"unknown_{index}";
            returned.Add(CreateReturnedGoal(name, index, configuredIndex));
        }

        return (configured, returned);
    }

    /// <summary>
    /// Creates duplicate configured and returned names, including null and whitespace names.
    /// </summary>
    /// <param name="goalCount">The number of configured and returned goals.</param>
    /// <returns>The benchmark inputs.</returns>
    private static (List<ConversionGoal> Configured, List<BenchmarkEvaluationResult> Returned) CreateDuplicates(
        int goalCount)
    {
        const int duplicateGroupSize = 4;
        var configured = new List<ConversionGoal>(goalCount);

        for (var index = 0; index < goalCount; index++)
        {
            var groupIndex = index / duplicateGroupSize;
            var name = groupIndex switch
            {
                0 => null,
                1 => "  duplicate_1  ",
                _ when index % 2 == 0 => $"duplicate_{groupIndex}",
                _ => $"DUPLICATE_{groupIndex}",
            };
            configured.Add(CreateConfiguredGoal(name, index));
        }

        var groupCount = (goalCount + duplicateGroupSize - 1) / duplicateGroupSize;
        var returned = new List<BenchmarkEvaluationResult>(goalCount);

        for (var index = 0; index < goalCount; index++)
        {
            var groupIndex = (index / 2) % groupCount;
            var name = groupIndex switch
            {
                0 => null,
                1 => index % 2 == 0 ? "  DUPLICATE_1  " : "  duplicate_1  ",
                _ when index % 2 == 0 => $"DUPLICATE_{groupIndex}",
                _ => $"duplicate_{groupIndex}",
            };
            returned.Add(CreateReturnedGoal(name, index, groupIndex * duplicateGroupSize));
        }

        return (configured, returned);
    }

    /// <summary>
    /// Creates unique configured goals with lower- and upper-case returned variants.
    /// </summary>
    /// <param name="goalCount">The number of configured and returned goals.</param>
    /// <returns>The benchmark inputs.</returns>
    private static (List<ConversionGoal> Configured, List<BenchmarkEvaluationResult> Returned) CreateCaseVariants(
        int goalCount)
    {
        var configured = CreateUniqueGoals(goalCount, "CaseGoal");
        var returned = new List<BenchmarkEvaluationResult>(goalCount);

        for (var index = 0; index < goalCount; index++)
        {
            var configuredIndex = goalCount - index - 1;
            var configuredName = configured[configuredIndex].Name;
            var name = index % 2 == 0
                ? configuredName.ToUpperInvariant()
                : configuredName.ToLowerInvariant();
            returned.Add(CreateReturnedGoal(name, index, configuredIndex));
        }

        return (configured, returned);
    }

    /// <summary>
    /// Creates a null configured element with every returned goal matched before that element.
    /// </summary>
    /// <param name="goalCount">The number of configured and returned goals.</param>
    /// <returns>The benchmark inputs.</returns>
    private static (List<ConversionGoal> Configured, List<BenchmarkEvaluationResult> Returned) CreateNullEntriesFallback(
        int goalCount)
    {
        var configured = CreateUniqueGoals(goalCount, "fallback");
        var nullIndex = goalCount / 2;
        configured[nullIndex] = null;
        var returned = new List<BenchmarkEvaluationResult>(goalCount);

        for (var index = 0; index < goalCount; index++)
        {
            var configuredIndex = nullIndex - (index % nullIndex) - 1;
            returned.Add(CreateReturnedGoal(
                configured[configuredIndex].Name,
                index,
                configuredIndex));
        }

        return (configured, returned);
    }

    /// <summary>
    /// Creates unique configured goals.
    /// </summary>
    /// <param name="goalCount">The number of goals.</param>
    /// <param name="prefix">The name prefix.</param>
    /// <returns>The configured goals.</returns>
    private static List<ConversionGoal> CreateUniqueGoals(
        int goalCount,
        string prefix)
    {
        var goals = new List<ConversionGoal>(goalCount);

        for (var index = 0; index < goalCount; index++)
        {
            goals.Add(CreateConfiguredGoal($"{prefix}_{index}", index));
        }

        return goals;
    }

    /// <summary>
    /// Creates one configured goal with index-specific score bounds.
    /// </summary>
    /// <param name="name">The goal name.</param>
    /// <param name="index">The configured index.</param>
    /// <returns>The configured goal.</returns>
    private static ConversionGoal CreateConfiguredGoal(
        string name,
        int index)
    {
        var minimumScore = index % 4;

        return new ConversionGoal
        {
            Name = name,
            Description = $"Configured conversion goal {index}.",
            MinScore = minimumScore,
            MaxScore = minimumScore + 10 + (index % 3),
        };
    }

    /// <summary>
    /// Creates one returned evaluation result.
    /// </summary>
    /// <param name="name">The returned name.</param>
    /// <param name="responseIndex">The response index.</param>
    /// <param name="configuredIndex">The expected configured index.</param>
    /// <returns>The returned result.</returns>
    private static BenchmarkEvaluationResult CreateReturnedGoal(
        string name,
        int responseIndex,
        int configuredIndex)
    {
        return new BenchmarkEvaluationResult
        {
            Name = name,
            Score = (responseIndex % 24) - 6,
            Reasoning = $"Response {responseIndex}; configured {configuredIndex}.",
        };
    }

    /// <summary>
    /// Maps results with the captured repeated-scan implementation.
    /// </summary>
    /// <param name="configuredGoals">The configured goals.</param>
    /// <param name="returnedGoals">The returned goals.</param>
    /// <returns>The mapped results.</returns>
    private static List<ConversionGoalResult> MapLegacy(
        List<ConversionGoal> configuredGoals,
        List<BenchmarkEvaluationResult> returnedGoals)
    {
        var results = new List<ConversionGoalResult>();

        foreach (var result in returnedGoals)
        {
            var goal = configuredGoals.FirstOrDefault(configuredGoal =>
                string.Equals(configuredGoal.Name, result.Name, StringComparison.OrdinalIgnoreCase));

            if (goal == null)
            {
                continue;
            }

            var score = Math.Clamp(result.Score, goal.MinScore, goal.MaxScore);

            results.Add(new ConversionGoalResult
            {
                Name = goal.Name,
                Score = score,
                MaxScore = goal.MaxScore,
                Reasoning = result.Reasoning,
            });
        }

        return results;
    }

    /// <summary>
    /// Maps results with a per-call lookup or the legacy null-entry fallback.
    /// </summary>
    /// <param name="configuredGoals">The configured goals.</param>
    /// <param name="returnedGoals">The returned goals.</param>
    /// <returns>The mapped results.</returns>
    private static List<ConversionGoalResult> MapCurrent(
        List<ConversionGoal> configuredGoals,
        List<BenchmarkEvaluationResult> returnedGoals)
    {
        var containsNullGoal = configuredGoals.Any(static goal => goal is null);
        Dictionary<string, ConversionGoal> goalsByName = null;
        ConversionGoal nullNameGoal = null;

        if (!containsNullGoal)
        {
            goalsByName = new Dictionary<string, ConversionGoal>(
                configuredGoals.Count,
                StringComparer.OrdinalIgnoreCase);

            foreach (var goal in configuredGoals)
            {
                if (goal.Name is null)
                {
                    nullNameGoal ??= goal;

                    continue;
                }

                goalsByName.TryAdd(goal.Name, goal);
            }
        }

        var results = new List<ConversionGoalResult>();

        foreach (var result in returnedGoals)
        {
            ConversionGoal goal;

            if (containsNullGoal)
            {
                goal = configuredGoals.FirstOrDefault(configuredGoal =>
                    string.Equals(configuredGoal.Name, result.Name, StringComparison.OrdinalIgnoreCase));
            }
            else if (result.Name is null)
            {
                goal = nullNameGoal;
            }
            else
            {
                goalsByName.TryGetValue(result.Name, out goal);
            }

            if (goal == null)
            {
                continue;
            }

            var score = Math.Clamp(result.Score, goal.MinScore, goal.MaxScore);

            results.Add(new ConversionGoalResult
            {
                Name = goal.Name,
                Score = score,
                MaxScore = goal.MaxScore,
                Reasoning = result.Reasoning,
            });
        }

        return results;
    }

    /// <summary>
    /// Verifies exact result count, order, and mapped values.
    /// </summary>
    /// <param name="legacy">The legacy results.</param>
    /// <param name="current">The current candidate results.</param>
    private static void VerifyEquivalent(
        List<ConversionGoalResult> legacy,
        List<ConversionGoalResult> current)
    {
        if (legacy.Count != current.Count)
        {
            throw new InvalidOperationException("The current mapping changed the result count.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            if (!string.Equals(legacy[index].Name, current[index].Name, StringComparison.Ordinal)
                || legacy[index].Score != current[index].Score
                || legacy[index].MaxScore != current[index].MaxScore
                || !string.Equals(legacy[index].Reasoning, current[index].Reasoning, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The current mapping changed result ordering or values.");
            }
        }
    }

    /// <summary>
    /// Verifies that the candidate retains exceptional and null-entry short-circuit semantics.
    /// </summary>
    private static void VerifyExceptionalSemantics()
    {
        var goals = new List<ConversionGoal>
        {
            new()
            {
                Name = "invalid",
                MinScore = 10,
                MaxScore = 1,
            },
        };
        var nullReturnedGoal = new List<BenchmarkEvaluationResult>
        {
            null,
        };
        var invalidRangeResult = new List<BenchmarkEvaluationResult>
        {
            new()
            {
                Name = "invalid",
                Score = 5,
            },
        };
        var goalsWithNullEntry = new List<ConversionGoal>
        {
            CreateConfiguredGoal("before", 0),
            null,
            CreateConfiguredGoal("after", 1),
        };
        var matchBeforeNull = new List<BenchmarkEvaluationResult>
        {
            CreateReturnedGoal("before", 0, 0),
        };
        var matchAfterNull = new List<BenchmarkEvaluationResult>
        {
            CreateReturnedGoal("after", 0, 1),
        };
        var unmatchedAfterNull = new List<BenchmarkEvaluationResult>
        {
            CreateReturnedGoal("unknown", 0, 0),
        };

        VerifyMatchingException<NullReferenceException>(
            () => MapLegacy(goals, nullReturnedGoal),
            () => MapCurrent(goals, nullReturnedGoal));
        VerifyMatchingException<ArgumentException>(
            () => MapLegacy(goals, invalidRangeResult),
            () => MapCurrent(goals, invalidRangeResult));
        VerifyEquivalent(
            MapLegacy(goalsWithNullEntry, matchBeforeNull),
            MapCurrent(goalsWithNullEntry, matchBeforeNull));
        VerifyEquivalent(
            MapLegacy(goalsWithNullEntry, []),
            MapCurrent(goalsWithNullEntry, []));
        VerifyMatchingException<NullReferenceException>(
            () => MapLegacy(goalsWithNullEntry, matchAfterNull),
            () => MapCurrent(goalsWithNullEntry, matchAfterNull));
        VerifyMatchingException<NullReferenceException>(
            () => MapLegacy(goalsWithNullEntry, unmatchedAfterNull),
            () => MapCurrent(goalsWithNullEntry, unmatchedAfterNull));
    }

    /// <summary>
    /// Verifies that two mapping operations throw the same expected exception type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="legacy">The legacy operation.</param>
    /// <param name="current">The current operation.</param>
    private static void VerifyMatchingException<TException>(
        Action legacy,
        Action current)
        where TException : Exception
    {
        var legacyException = RecordException(legacy);
        var currentException = RecordException(current);

        if (legacyException is not TException || currentException is not TException)
        {
            throw new InvalidOperationException(
                $"The mapping implementations must both throw {typeof(TException).Name}.");
        }
    }

    /// <summary>
    /// Executes an operation and returns its exception.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <returns>The thrown exception, or <see langword="null"/>.</returns>
    private static Exception RecordException(Action operation)
    {
        try
        {
            operation();

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>
    /// Represents one goal returned by conversion-goal evaluation.
    /// </summary>
    private sealed class BenchmarkEvaluationResult
    {
        /// <summary>
        /// Gets or sets the returned goal name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the returned score.
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// Gets or sets the returned reasoning.
        /// </summary>
        public string Reasoning { get; set; }
    }
}
