using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures full chat-session deserialization against a narrow timestamp-only projection.
/// The projection is benchmarked as a rejected experiment because it does not validate
/// malformed values in the omitted chat-session properties.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 6)]
public class EntityCoreAIChatSessionSummaryDeserializationBenchmarks
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private string[] _payloads;

    /// <summary>
    /// Gets or sets the serialized chat-session payload size in bytes.
    /// </summary>
    [Params(1_024, 65_536, 1_048_576)]
    public int PayloadSize { get; set; }

    /// <summary>
    /// Gets or sets the number of payload rows deserialized per operation.
    /// </summary>
    [Params(1, 20, 200)]
    public int RowCount { get; set; }

    /// <summary>
    /// Creates exact-size valid JSON payloads and verifies matching timestamps.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var payload = CreatePayload(PayloadSize);
        _payloads = Enumerable.Repeat(payload, RowCount).ToArray();

        if (FullSession() != NarrowProjection())
        {
            throw new InvalidOperationException(
                "Full and narrow deserialization returned different timestamps.");
        }
    }

    /// <summary>
    /// Deserializes the complete chat-session model for every payload.
    /// </summary>
    /// <returns>A checksum over the modified timestamps.</returns>
    [Benchmark(Baseline = true)]
    public long FullSession()
    {
        var checksum = 0L;

        foreach (var payload in _payloads)
        {
            checksum += JsonSerializer.Deserialize<AIChatSession>(
                payload,
                _jsonSerializerOptions).ModifiedUtc?.Ticks ?? 0;
        }

        return checksum;
    }

    /// <summary>
    /// Deserializes only the modified timestamp and skips all other JSON properties.
    /// </summary>
    /// <returns>A checksum over the modified timestamps.</returns>
    [Benchmark]
    public long NarrowProjection()
    {
        var checksum = 0L;

        foreach (var payload in _payloads)
        {
            checksum += JsonSerializer.Deserialize<ModifiedUtcProjection>(
                payload,
                _jsonSerializerOptions).ModifiedUtc?.Ticks ?? 0;
        }

        return checksum;
    }

    /// <summary>
    /// Creates an ASCII JSON document with the exact requested UTF-8 byte length.
    /// </summary>
    /// <param name="payloadSize">The requested payload size.</param>
    /// <returns>The valid chat-session JSON payload.</returns>
    private static string CreatePayload(int payloadSize)
    {
        const string prefix = "{\"ModifiedUtc\":\"2026-07-13T12:34:56Z\",\"Properties\":{\"Payload\":\"";
        const string suffix = "\"}}";
        var contentLength = payloadSize - prefix.Length - suffix.Length;

        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payloadSize),
                payloadSize,
                "The payload size is too small for the JSON envelope.");
        }

        return string.Concat(prefix, new string('x', contentLength), suffix);
    }

    private sealed class ModifiedUtcProjection
    {
        /// <summary>
        /// Gets or sets the projected modified timestamp.
        /// </summary>
        public DateTime? ModifiedUtc { get; set; }
    }
}
