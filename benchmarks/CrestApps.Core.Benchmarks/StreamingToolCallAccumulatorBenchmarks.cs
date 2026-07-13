using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using OpenAI.Chat;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares legacy streamed tool-call argument accumulation with the current accumulator.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 8)]
public class StreamingToolCallAccumulatorBenchmarks
{
    private StreamingChatToolCallUpdate[][] _updateChunks;
    private byte[][] _expectedArguments;
    private int[] _toolCallIndexes;

    /// <summary>
    /// Gets or sets the number of argument fragments emitted for each tool call.
    /// </summary>
    [Params(1, 10, 100, 1000)]
    public int FragmentCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of argument bytes emitted for each tool call.
    /// </summary>
    [Params(1024, 64 * 1024, 1024 * 1024)]
    public int TotalArgumentBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of tool calls interleaved in each streaming fragment round.
    /// </summary>
    [Params(1, 4, 8)]
    public int ToolCallCount { get; set; }

    /// <summary>
    /// Creates deterministic SDK updates and verifies exact legacy and current output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _toolCallIndexes = Enumerable.Range(0, ToolCallCount)
            .Select(position => ((position * 3) + 1) % ToolCallCount)
            .ToArray();
        _expectedArguments = new byte[ToolCallCount][];
        _updateChunks = new StreamingChatToolCallUpdate[FragmentCount][];

        for (var position = 0; position < ToolCallCount; position++)
        {
            _expectedArguments[position] = CreateArgumentBytes(position, TotalArgumentBytes);
        }

        var baseFragmentLength = TotalArgumentBytes / FragmentCount;
        var longerFragmentCount = TotalArgumentBytes % FragmentCount;
        var offsets = new int[ToolCallCount];

        for (var fragmentIndex = 0; fragmentIndex < FragmentCount; fragmentIndex++)
        {
            var fragmentLength =
                fragmentIndex < longerFragmentCount
                    ? baseFragmentLength + 1
                    : baseFragmentLength;
            var updates = new StreamingChatToolCallUpdate[ToolCallCount];

            for (var position = 0; position < ToolCallCount; position++)
            {
                var toolCallIndex = _toolCallIndexes[position];
                var fragment = _expectedArguments[position]
                    .AsSpan(offsets[position], fragmentLength)
                    .ToArray();
                offsets[position] += fragmentLength;
                updates[position] = OpenAIChatModelFactory.StreamingChatToolCallUpdate(
                    toolCallIndex,
                    fragmentIndex == 0
                        ? $"call-{toolCallIndex}"
                        : null,
                    ChatToolCallKind.Function,
                    fragmentIndex == 0
                        ? $"function-{toolCallIndex}"
                        : null,
                    BinaryData.FromBytes(fragment));
            }

            _updateChunks[fragmentIndex] = updates;
        }

        var legacy = AccumulateWithLists();
        var current = AccumulateCurrent();

        EnsureEquivalent(legacy, current);
        EnsureExpectedOutput(current);
    }

    /// <summary>
    /// Accumulates fragments with the legacy per-fragment and final byte-array copies.
    /// </summary>
    /// <returns>The materialized tool calls.</returns>
    [Benchmark(Baseline = true)]
    public List<ChatToolCall> AccumulateWithLists()
    {
        var accumulatedToolCalls =
            new Dictionary<int, (string ToolCallId, string FunctionName, List<byte> ArgumentBytes)>();

        foreach (var updateChunk in _updateChunks)
        {
            foreach (var update in updateChunk)
            {
                if (!accumulatedToolCalls.TryGetValue(update.Index, out var accumulated))
                {
                    accumulated = (update.ToolCallId, update.FunctionName, []);
                    accumulatedToolCalls[update.Index] = accumulated;
                }

                if (!string.IsNullOrEmpty(update.ToolCallId))
                {
                    accumulated.ToolCallId = update.ToolCallId;
                }

                if (!string.IsNullOrEmpty(update.FunctionName))
                {
                    accumulated.FunctionName = update.FunctionName;
                }

                if (update.FunctionArgumentsUpdate is not null)
                {
                    accumulated.ArgumentBytes.AddRange(update.FunctionArgumentsUpdate.ToArray());
                }

                accumulatedToolCalls[update.Index] = accumulated;
            }
        }

        var toolCalls = accumulatedToolCalls.Values
            .Where(toolCall =>
                !string.IsNullOrEmpty(toolCall.ToolCallId) &&
                !string.IsNullOrEmpty(toolCall.FunctionName))
            .Select(toolCall => ChatToolCall.CreateFunctionToolCall(
                toolCall.ToolCallId,
                toolCall.FunctionName,
                BinaryData.FromBytes(toolCall.ArgumentBytes.ToArray())))
            .ToList();
        accumulatedToolCalls.Clear();

        return toolCalls;
    }

    /// <summary>
    /// Accumulates fragments with the current production accumulator.
    /// </summary>
    /// <returns>The materialized tool calls.</returns>
    [Benchmark]
    public List<ChatToolCall> AccumulateCurrent()
    {
        var accumulator = new StreamingToolCallAccumulator();

        foreach (var updateChunk in _updateChunks)
        {
            foreach (var update in updateChunk)
            {
                accumulator.Append(update);
            }
        }

        var toolCalls = accumulator.BuildToolCalls();
        accumulator.Clear();

        return toolCalls;
    }

    /// <summary>
    /// Creates deterministic binary argument data for one tool call.
    /// </summary>
    /// <param name="toolCallPosition">The tool call's insertion position.</param>
    /// <param name="length">The total argument length.</param>
    /// <returns>The generated argument bytes.</returns>
    private static byte[] CreateArgumentBytes(int toolCallPosition, int length)
    {
        var bytes = new byte[length];

        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((toolCallPosition * 31) + (index * 17) + (index >> 8));
        }

        bytes[0] = 0;
        bytes[^1] = 255;

        return bytes;
    }

    /// <summary>
    /// Verifies tool-call metadata, ordering, and exact binary arguments between implementations.
    /// </summary>
    /// <param name="legacy">The legacy result.</param>
    /// <param name="current">The current result.</param>
    private static void EnsureEquivalent(List<ChatToolCall> legacy, List<ChatToolCall> current)
    {
        if (legacy.Count != current.Count)
        {
            throw new InvalidOperationException("Accumulator implementations returned different tool-call counts.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            if (!string.Equals(legacy[index].Id, current[index].Id, StringComparison.Ordinal) ||
                !string.Equals(legacy[index].FunctionName, current[index].FunctionName, StringComparison.Ordinal) ||
                !legacy[index].FunctionArguments.ToMemory().Span.SequenceEqual(
                    current[index].FunctionArguments.ToMemory().Span))
            {
                throw new InvalidOperationException(
                    $"Accumulator implementations returned different output at position {index}.");
            }
        }
    }

    /// <summary>
    /// Verifies that materialized calls match the generated insertion order and argument bytes.
    /// </summary>
    /// <param name="toolCalls">The materialized tool calls.</param>
    private void EnsureExpectedOutput(List<ChatToolCall> toolCalls)
    {
        if (toolCalls.Count != ToolCallCount)
        {
            throw new InvalidOperationException("The accumulator returned an unexpected tool-call count.");
        }

        for (var position = 0; position < toolCalls.Count; position++)
        {
            var toolCallIndex = _toolCallIndexes[position];

            if (!string.Equals(toolCalls[position].Id, $"call-{toolCallIndex}", StringComparison.Ordinal) ||
                !string.Equals(
                    toolCalls[position].FunctionName,
                    $"function-{toolCallIndex}",
                    StringComparison.Ordinal) ||
                !toolCalls[position].FunctionArguments.ToMemory().Span.SequenceEqual(
                    _expectedArguments[position]))
            {
                throw new InvalidOperationException(
                    $"The accumulator did not preserve expected output at position {position}.");
            }
        }
    }
}
