using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Support;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the original LINQ-based JSON node conversion with the production implementation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class JsonNodeGetRawValueBenchmarks
{
    private JsonNode _node;

    /// <summary>
    /// Gets or sets the JSON payload scenario.
    /// </summary>
    [Params(
        "FlatObject",
        "Mixed1000NodeTree",
        "LargeArray",
        "NestedObjects",
        "FallbackValues",
        "CatalogPayload",
        "ConfigurationPayload")]
    public string Scenario { get; set; }

    /// <summary>
    /// Creates the selected immutable JSON node graph and verifies legacy/current equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _node = CreateScenario(Scenario);

        if (Scenario == "Mixed1000NodeTree" && CountNodes(_node) != 1000)
        {
            throw new InvalidOperationException("The mixed benchmark scenario must contain exactly 1,000 non-null JSON nodes.");
        }

        var legacy = GetRawValueLegacy(_node);
        var current = _node.GetRawValue();

        EnsureEquivalent(legacy, current);
    }

    /// <summary>
    /// Converts the selected payload with the implementation captured before optimization.
    /// </summary>
    /// <returns>The recursively converted raw value.</returns>
    [Benchmark(Baseline = true)]
    public object ConvertLegacy()
    {
        return GetRawValueLegacy(_node);
    }

    /// <summary>
    /// Converts the selected payload with the production implementation.
    /// </summary>
    /// <returns>The recursively converted raw value.</returns>
    [Benchmark]
    public object ConvertCurrent()
    {
        return _node.GetRawValue();
    }

    /// <summary>
    /// Implements the original recursive LINQ-based conversion exactly.
    /// </summary>
    /// <param name="node">The JSON node.</param>
    /// <returns>The recursively converted raw value.</returns>
    private static object GetRawValueLegacy(JsonNode node)
    {
        if (node == null)
        {
            return null;
        }

        return node switch
        {
            JsonObject jsonObject => jsonObject.ToDictionary(
                property => property.Key,
                property => GetRawValueLegacy(property.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonArray jsonArray => jsonArray.Select(static item => GetRawValueLegacy(item)).ToList(),
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue) => stringValue,
            JsonValue jsonValue when jsonValue.TryGetValue<long>(out var longValue) => longValue,
            JsonValue jsonValue when jsonValue.TryGetValue<double>(out var doubleValue) => doubleValue,
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var boolValue) => boolValue,
            JsonValue jsonValue when jsonValue.TryGetValue<DateTime>(out var dateValue) => dateValue,
            _ => node.ToJsonString(),
        };
    }

    /// <summary>
    /// Creates the selected benchmark payload.
    /// </summary>
    /// <param name="scenario">The scenario name.</param>
    /// <returns>The JSON node graph.</returns>
    private static JsonNode CreateScenario(string scenario)
    {
        return scenario switch
        {
            "FlatObject" => CreateFlatObject(),
            "Mixed1000NodeTree" => CreateMixedTree(),
            "LargeArray" => CreateLargeArray(),
            "NestedObjects" => CreateNestedObjects(),
            "FallbackValues" => CreateFallbackValues(),
            "CatalogPayload" => CreateCatalogPayload(),
            "ConfigurationPayload" => CreateConfigurationPayload(),
            _ => throw new InvalidOperationException($"Unknown scenario '{scenario}'."),
        };
    }

    /// <summary>
    /// Creates a flat document-like object with 256 primitive fields.
    /// </summary>
    /// <returns>The flat JSON object.</returns>
    private static JsonObject CreateFlatObject()
    {
        var json = new JsonObject();

        for (var index = 0; index < 64; index++)
        {
            json[$"name-{index}"] = $"entry-{index}";
            json[$"rank-{index}"] = (long)index;
            json[$"score-{index}"] = index / 10d;
            json[$"enabled-{index}"] = index % 2 == 0;
        }

        return json;
    }

    /// <summary>
    /// Creates an exactly 1,000-node mixed catalog-shaped tree.
    /// </summary>
    /// <returns>The mixed JSON tree.</returns>
    private static JsonArray CreateMixedTree()
    {
        var json = new JsonArray();

        for (var index = 0; index < 99; index++)
        {
            json.Add(new JsonObject
            {
                ["Name"] = $"entry-{index}",
                ["Enabled"] = index % 2 == 0,
                ["Rank"] = (long)index,
                ["Score"] = index / 100d,
                ["Tags"] = new JsonArray($"tag-{index % 8}", $"group-{index % 4}"),
                ["Metadata"] = new JsonObject
                {
                    ["Source"] = $"source-{index % 5}",
                    ["Optional"] = null,
                },
            });
        }

        json.Add(new JsonObject
        {
            ["Name"] = "tail",
            ["Enabled"] = true,
            ["Rank"] = 99L,
            ["Score"] = 0.99d,
            ["Description"] = "Completes the exact node count.",
            ["Category"] = "benchmark",
            ["Owner"] = "system",
            ["Version"] = "1.0",
        });

        return json;
    }

    /// <summary>
    /// Creates a large heterogeneous array with 10,000 entries.
    /// </summary>
    /// <returns>The large JSON array.</returns>
    private static JsonArray CreateLargeArray()
    {
        var json = new JsonArray();

        for (var index = 0; index < 10_000; index++)
        {
            switch (index % 5)
            {
                case 0:
                    json.Add((long)index);
                    break;
                case 1:
                    json.Add($"value-{index}");
                    break;
                case 2:
                    json.Add(index / 10d);
                    break;
                case 3:
                    json.Add(index % 2 == 0);
                    break;
                default:
                    json.Add(null);
                    break;
            }
        }

        return json;
    }

    /// <summary>
    /// Creates 128 nested objects with sibling metadata at each level.
    /// </summary>
    /// <returns>The nested JSON object graph.</returns>
    private static JsonObject CreateNestedObjects()
    {
        var root = new JsonObject();
        var current = root;

        for (var index = 0; index < 128; index++)
        {
            var child = new JsonObject();
            current["Name"] = $"level-{index}";
            current["Enabled"] = index % 2 == 0;
            current["Value"] = (long)index;
            current["Child"] = child;
            current = child;
        }

        current["Name"] = "leaf";

        return root;
    }

    /// <summary>
    /// Creates unsupported primitive and custom values that use the exact JSON serialization fallback.
    /// </summary>
    /// <returns>The fallback-heavy JSON object.</returns>
    private static JsonObject CreateFallbackValues()
    {
        var json = new JsonObject();
        var dateTimeOffset = new DateTimeOffset(2025, 1, 2, 3, 4, 5, 678, TimeSpan.FromHours(-7));

        for (var index = 0; index < 64; index++)
        {
            json[$"decimal-{index}"] = index + 0.25m;
            json[$"float-{index}"] = index + 0.5f;
            json[$"offset-{index}"] = dateTimeOffset.AddMinutes(index);
            json[$"guid-{index}"] = CreateGuid(index);
            json[$"custom-{index}"] = JsonValue.Create(new Dictionary<string, object>
            {
                ["Name"] = $"custom-{index}",
                ["Count"] = index,
            });
        }

        return json;
    }

    /// <summary>
    /// Creates a realistic catalog export with nested properties, tags, ownership, and timestamps.
    /// </summary>
    /// <returns>The catalog JSON payload.</returns>
    private static JsonObject CreateCatalogPayload()
    {
        var entries = new JsonArray();
        var createdUtc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        for (var index = 0; index < 64; index++)
        {
            entries.Add(new JsonObject
            {
                ["ItemId"] = $"data-source-{index}",
                ["Name"] = $"customer-documents-{index}",
                ["DisplayText"] = $"Customer Documents {index}",
                ["Source"] = $"tenant-{index % 4}",
                ["OwnerId"] = $"user-{index % 8}",
                ["CreatedUtc"] = createdUtc.AddMinutes(index),
                ["Enabled"] = index % 3 != 0,
                ["Properties"] = new JsonObject
                {
                    ["Endpoint"] = $"https://search-{index % 4}.example.com",
                    ["IndexName"] = $"documents-{index}",
                    ["Dimensions"] = 1536L,
                    ["MinimumScore"] = 0.75d,
                    ["Tags"] = new JsonArray("customer", "documents", $"region-{index % 3}"),
                    ["Headers"] = new JsonObject
                    {
                        ["x-tenant-id"] = $"tenant-{index % 4}",
                        ["x-correlation-mode"] = "catalog",
                    },
                },
            });
        }

        return new JsonObject
        {
            ["Schema"] = "CrestApps.Core.Catalog",
            ["Version"] = 1L,
            ["Entries"] = entries,
        };
    }

    /// <summary>
    /// Creates realistic AI connection and deployment configuration.
    /// </summary>
    /// <returns>The configuration JSON payload.</returns>
    private static JsonObject CreateConfigurationPayload()
    {
        var connections = new JsonArray();
        var deployments = new JsonArray();

        for (var index = 0; index < 24; index++)
        {
            connections.Add(new JsonObject
            {
                ["Name"] = $"openai-{index}",
                ["ClientName"] =
                    index % 2 == 0
                    ? "OpenAI"
                    : "AzureOpenAI",
                ["Endpoint"] = $"https://ai-{index}.example.com",
                ["ApiKey"] = $"benchmark-key-{index}",
                ["Headers"] = new JsonObject
                {
                    ["api-version"] = "2025-04-01-preview",
                    ["x-tenant-id"] = $"tenant-{index % 4}",
                },
            });
            deployments.Add(new JsonObject
            {
                ["Name"] = $"chat-{index}",
                ["ClientName"] =
                    index % 2 == 0
                    ? "OpenAI"
                    : "AzureOpenAI",
                ["ConnectionName"] = $"openai-{index}",
                ["ModelName"] =
                    index % 3 == 0
                    ? "gpt-4.1"
                    : "gpt-4.1-mini",
                ["Type"] = "Chat",
                ["ContextWindow"] = 128_000L,
                ["Capabilities"] = new JsonArray("Chat", "Tools", "StructuredOutput"),
                ["Settings"] = new JsonObject
                {
                    ["Temperature"] = 0.2d,
                    ["TopP"] = 0.95d,
                    ["AllowParallelTools"] = true,
                },
            });
        }

        return new JsonObject
        {
            ["CrestApps"] = new JsonObject
            {
                ["AI"] = new JsonObject
                {
                    ["Connections"] = connections,
                    ["Deployments"] = deployments,
                    ["Chat"] = new JsonObject
                    {
                        ["DefaultDeploymentName"] = "chat-0",
                        ["MaxOutputTokens"] = 4096L,
                        ["EnableUsageAnalytics"] = true,
                    },
                },
            },
        };
    }

    /// <summary>
    /// Creates a stable GUID whose final bytes vary with the supplied index.
    /// </summary>
    /// <param name="index">The value embedded in the GUID.</param>
    /// <returns>The stable GUID.</returns>
    private static Guid CreateGuid(int index)
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(12), index);

        return new Guid(bytes);
    }

    /// <summary>
    /// Counts non-null JSON node instances recursively.
    /// </summary>
    /// <param name="node">The JSON node.</param>
    /// <returns>The number of non-null nodes.</returns>
    private static int CountNodes(JsonNode node)
    {
        if (node == null)
        {
            return 0;
        }

        var count = 1;

        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    count += CountNodes(property.Value);
                }
                break;

            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    count += CountNodes(item);
                }
                break;
        }

        return count;
    }

    /// <summary>
    /// Verifies exact output type, ordering, comparer, value, and recursive shape equivalence.
    /// </summary>
    /// <param name="legacy">The legacy output.</param>
    /// <param name="current">The current output.</param>
    private static void EnsureEquivalent(object legacy, object current)
    {
        if (legacy == null || current == null)
        {
            if (legacy != current)
            {
                throw new InvalidOperationException("Legacy and current null outputs differ.");
            }

            return;
        }

        if (legacy.GetType() != current.GetType())
        {
            throw new InvalidOperationException(
                $"Legacy output type '{legacy.GetType()}' differs from current output type '{current.GetType()}'.");
        }

        switch (legacy)
        {
            case Dictionary<string, object> legacyDictionary:
                EnsureEquivalentDictionaries(
                    legacyDictionary,
                    (Dictionary<string, object>)current);
                break;

            case List<object> legacyList:
                EnsureEquivalentLists(legacyList, (List<object>)current);
                break;

            default:
                if (!legacy.Equals(current))
                {
                    throw new InvalidOperationException("Legacy and current scalar outputs differ.");
                }
                break;
        }
    }

    /// <summary>
    /// Verifies dictionary comparer, ordering, keys, values, and recursive types.
    /// </summary>
    /// <param name="legacy">The legacy dictionary.</param>
    /// <param name="current">The current dictionary.</param>
    private static void EnsureEquivalentDictionaries(
        Dictionary<string, object> legacy,
        Dictionary<string, object> current)
    {
        if (!legacy.Comparer.Equals(current.Comparer) ||
            !legacy.Keys.SequenceEqual(current.Keys, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Legacy and current dictionary semantics differ.");
        }

        foreach (var key in legacy.Keys)
        {
            EnsureEquivalent(legacy[key], current[key]);
        }
    }

    /// <summary>
    /// Verifies list length, ordering, values, and recursive types.
    /// </summary>
    /// <param name="legacy">The legacy list.</param>
    /// <param name="current">The current list.</param>
    private static void EnsureEquivalentLists(
        List<object> legacy,
        List<object> current)
    {
        if (legacy.Count != current.Count)
        {
            throw new InvalidOperationException("Legacy and current list lengths differ.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            EnsureEquivalent(legacy[index], current[index]);
        }
    }
}
