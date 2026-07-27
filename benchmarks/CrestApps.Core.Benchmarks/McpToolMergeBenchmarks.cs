using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures the isolated merge of local protocol tools with SDK server tools.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class McpToolMergeBenchmarks
{
    private static readonly JsonElement _schema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object"
    }
    """);

    private IReadOnlyList<Tool> _localTools;
    private IReadOnlyList<McpServerTool> _sdkTools;

    /// <summary>
    /// Gets or sets the total number of local and SDK input tools.
    /// </summary>
    [Params(100, 1000, 10000)]
    public int ToolCount { get; set; }

    /// <summary>
    /// Gets or sets the duplicate and casing distribution.
    /// </summary>
    [Params(
        ToolMergeScenario.EmptySdk,
        ToolMergeScenario.NoDuplicates,
        ToolMergeScenario.DuplicateHeavyOverlap,
        ToolMergeScenario.SdkInternalDuplicates,
        ToolMergeScenario.CaseOnlyNames)]
    public ToolMergeScenario Scenario { get; set; }

    /// <summary>
    /// Creates immutable local and SDK tool inputs and verifies the candidate against current semantics.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        (_localTools, _sdkTools) = CreateTools(ToolCount, Scenario);

        var legacy = MergeWithRepeatedScan();
        var candidate = MergeCurrent();

        EnsureEquivalent(legacy, candidate);
    }

    /// <summary>
    /// Merges tools using the current repeated linear name scan.
    /// </summary>
    /// <returns>The merged tools.</returns>
    [Benchmark(Baseline = true)]
    public List<Tool> MergeWithRepeatedScan()
    {
        var tools = new List<Tool>(_localTools);

        foreach (var sdkTool in _sdkTools)
        {
            if (!tools.Any(tool => tool.Name == sdkTool.ProtocolTool.Name))
            {
                tools.Add(sdkTool.ProtocolTool);
            }
        }

        return tools;
    }

    /// <summary>
    /// Merges tools using the current empty-enumerable fast path and ordinal name set.
    /// </summary>
    /// <returns>The merged tools.</returns>
    [Benchmark]
    public List<Tool> MergeCurrent()
    {
        var tools = new List<Tool>(_localTools);
        using var sdkToolEnumerator = _sdkTools.GetEnumerator();

        if (sdkToolEnumerator.MoveNext())
        {
            var toolNames = new HashSet<string>(tools.Count, StringComparer.Ordinal);

            foreach (var tool in tools)
            {
                toolNames.Add(tool.Name);
            }

            do
            {
                var sdkTool = sdkToolEnumerator.Current;

                if (toolNames.Add(sdkTool.ProtocolTool.Name))
                {
                    tools.Add(sdkTool.ProtocolTool);
                }
            }
            while (sdkToolEnumerator.MoveNext());
        }

        return tools;
    }

    /// <summary>
    /// Creates benchmark tools for the requested scale and scenario.
    /// </summary>
    /// <param name="toolCount">The total number of local and SDK input tools.</param>
    /// <param name="scenario">The duplicate and casing distribution.</param>
    /// <returns>The local and SDK tool inputs.</returns>
    private static (IReadOnlyList<Tool> LocalTools, IReadOnlyList<McpServerTool> SdkTools) CreateTools(
        int toolCount,
        ToolMergeScenario scenario)
    {
        var localCount = scenario == ToolMergeScenario.EmptySdk
            ? toolCount
            : toolCount / 2;
        var sdkCount = toolCount - localCount;
        var localTools = new Tool[localCount];
        var sdkTools = new McpServerTool[sdkCount];

        for (var index = 0; index < localCount; index++)
        {
            var name = scenario == ToolMergeScenario.SdkInternalDuplicates
                ? $"local-{index}"
                : $"tool-{index}";

            localTools[index] = CreateProtocolTool(name, $"Local tool {index}");
        }

        for (var index = 0; index < sdkCount; index++)
        {
            var name = scenario switch
            {
                ToolMergeScenario.NoDuplicates => $"tool-{localCount + index}",
                ToolMergeScenario.DuplicateHeavyOverlap when index < (sdkCount * 3 / 4) =>
                    $"tool-{index % localCount}",
                ToolMergeScenario.DuplicateHeavyOverlap => $"sdk-{index}",
                ToolMergeScenario.SdkInternalDuplicates => $"sdk-{index / 4}",
                ToolMergeScenario.CaseOnlyNames => $"TOOL-{index}",
                _ => throw new InvalidOperationException($"Unsupported scenario '{scenario}'."),
            };

            sdkTools[index] = CreateSdkTool(name, $"SDK tool {index}");
        }

        return (localTools, sdkTools);
    }

    /// <summary>
    /// Creates a local protocol tool.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The tool description.</param>
    /// <returns>The protocol tool.</returns>
    private static Tool CreateProtocolTool(string name, string description)
    {
        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = _schema,
        };
    }

    /// <summary>
    /// Creates an SDK server tool.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The tool description.</param>
    /// <returns>The SDK server tool.</returns>
    private static McpServerTool CreateSdkTool(string name, string description)
    {
        return McpServerTool.Create(
            (Func<string>)(static () => string.Empty),
            new McpServerToolCreateOptions
            {
                Name = name,
                Description = description,
            });
    }

    /// <summary>
    /// Verifies exact order and protocol tool identity between merge implementations.
    /// </summary>
    /// <param name="legacy">The current merge result.</param>
    /// <param name="candidate">The hash-based merge result.</param>
    private static void EnsureEquivalent(List<Tool> legacy, List<Tool> candidate)
    {
        if (legacy.Count != candidate.Count)
        {
            throw new InvalidOperationException("Merge implementations returned different tool counts.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            if (!ReferenceEquals(legacy[index], candidate[index]))
            {
                throw new InvalidOperationException(
                    $"Merge implementations returned different tools at index {index}.");
            }
        }
    }

    /// <summary>
    /// Defines the benchmark's duplicate and casing distributions.
    /// </summary>
    public enum ToolMergeScenario
    {
        /// <summary>
        /// All tools are local and the non-null SDK enumerable is empty.
        /// </summary>
        EmptySdk,

        /// <summary>
        /// All local and SDK names are unique.
        /// </summary>
        NoDuplicates,

        /// <summary>
        /// Three quarters of SDK names overlap local names.
        /// </summary>
        DuplicateHeavyOverlap,

        /// <summary>
        /// Each SDK name is repeated four times.
        /// </summary>
        SdkInternalDuplicates,

        /// <summary>
        /// SDK names differ from local names only by case.
        /// </summary>
        CaseOnlyNames,
    }
}
