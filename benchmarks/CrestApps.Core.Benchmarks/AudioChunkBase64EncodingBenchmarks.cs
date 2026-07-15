using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the legacy copied audio-chunk Base64 conversion with span-based conversion.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class AudioChunkBase64EncodingBenchmarks
{
    private ReadOnlyMemory<byte>[] _chunks;

    /// <summary>
    /// Gets or sets the number of bytes in each audio chunk.
    /// </summary>
    [Params(256, 4 * 1024, 64 * 1024, 1024 * 1024)]
    public int ChunkSize { get; set; }

    /// <summary>
    /// Gets or sets the offset of each chunk within its backing buffer.
    /// </summary>
    [Params(0, 17)]
    public int SliceOffset { get; set; }

    /// <summary>
    /// Creates deterministic binary audio chunks and verifies exact output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var chunkCount = GetChunkCount(ChunkSize);
        var random = new Random(42);
        _chunks = new ReadOnlyMemory<byte>[chunkCount];

        for (var index = 0; index < _chunks.Length; index++)
        {
            var buffer = new byte[ChunkSize + (2 * SliceOffset)];
            random.NextBytes(buffer);
            var chunk = buffer.AsMemory(SliceOffset, ChunkSize);
            chunk.Span[0] = 0;
            chunk.Span[^1] = 255;
            _chunks[index] = chunk;

            var legacy = Convert.ToBase64String(chunk.ToArray());
            var current = Convert.ToBase64String(chunk.Span);

            if (!string.Equals(legacy, current, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Legacy and span-based Base64 conversion produced different output.");
            }
        }
    }

    /// <summary>
    /// Encodes every chunk after copying its selected memory range to a new array.
    /// </summary>
    /// <returns>The total encoded character count.</returns>
    [Benchmark(Baseline = true)]
    public int ToArrayThenBase64()
    {
        var encodedLength = 0;

        foreach (var audioData in _chunks)
        {
            encodedLength += Convert.ToBase64String(audioData.ToArray()).Length;
        }

        return encodedLength;
    }

    /// <summary>
    /// Encodes every chunk directly from its selected memory span.
    /// </summary>
    /// <returns>The total encoded character count.</returns>
    [Benchmark]
    public int SpanBase64()
    {
        var encodedLength = 0;

        foreach (var audioData in _chunks)
        {
            encodedLength += Convert.ToBase64String(audioData.Span).Length;
        }

        return encodedLength;
    }

    /// <summary>
    /// Gets a representative streaming chunk count for the requested chunk size.
    /// </summary>
    /// <param name="chunkSize">The chunk size in bytes.</param>
    /// <returns>The number of chunks encoded per benchmark operation.</returns>
    private static int GetChunkCount(int chunkSize)
    {
        return chunkSize switch
        {
            256 => 256,
            4 * 1024 => 64,
            64 * 1024 => 16,
            1024 * 1024 => 4,
            _ => throw new InvalidOperationException($"Unsupported chunk size '{chunkSize}'."),
        };
    }
}
