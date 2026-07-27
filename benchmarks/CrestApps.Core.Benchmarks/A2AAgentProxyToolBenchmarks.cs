using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.A2A.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures A2A proxy tool construction with per-instance versus shared JSON schema parsing.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class A2AAgentProxyToolConstructionBenchmarks
{
    private const string _jsonSchema =
        """
        {
          "type": "object",
          "properties": {
            "message": {
              "type": "string",
              "description": "The message or task to send to the remote agent for processing."
            },
            "contextId": {
              "type": "string",
              "description": "An optional context identifier to maintain conversation continuity with the remote agent."
            }
          },
          "required": ["message"]
        }

        """;

    private int _schemaKindSink;

    /// <summary>
    /// Gets or sets the number of proxy tools created per benchmark operation.
    /// </summary>
    [Params(1, 100)]
    public int ToolCount { get; set; }

    /// <summary>
    /// Verifies that the captured schema and production schema remain identical.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var legacySchema = JsonElement.Parse(_jsonSchema);
        var tool = CreateTool(ToolCount);

        if (!string.Equals(
            legacySchema.GetRawText(),
            tool.JsonSchema.GetRawText(),
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Legacy and current A2A proxy schemas differ.");
        }
    }

    /// <summary>
    /// Creates proxy tools while parsing the original per-instance schema.
    /// </summary>
    /// <returns>The last created proxy tool.</returns>
    [Benchmark(Baseline = true)]
    public object CreateToolsLegacy()
    {
        A2AAgentProxyTool tool = null;
        var schemaKind = 0;

        for (var i = 0; i < ToolCount; i++)
        {
            var schema = JsonElement.Parse(_jsonSchema);
            tool = CreateTool(i);
            schemaKind += (int)schema.ValueKind;
        }

        _schemaKindSink = schemaKind;

        return tool;
    }

    /// <summary>
    /// Creates proxy tools with the shared production schema.
    /// </summary>
    /// <returns>The last created proxy tool.</returns>
    [Benchmark]
    public object CreateToolsCurrent()
    {
        A2AAgentProxyTool tool = null;
        var schemaKind = 0;

        for (var i = 0; i < ToolCount; i++)
        {
            tool = CreateTool(i);
            schemaKind += (int)tool.JsonSchema.ValueKind;
        }

        _schemaKindSink = schemaKind;

        return tool;
    }

    /// <summary>
    /// Creates an A2A proxy tool.
    /// </summary>
    /// <param name="index">The tool index.</param>
    /// <returns>The proxy tool.</returns>
    private static A2AAgentProxyTool CreateTool(int index)
    {
        return new A2AAgentProxyTool(
            $"agent-{index}",
            "Handles a benchmark task.",
            "https://agent.example",
            $"connection-{index}");
    }
}
