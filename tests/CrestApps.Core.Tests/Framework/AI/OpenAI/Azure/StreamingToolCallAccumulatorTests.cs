using System.Text;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using OpenAI.Chat;

namespace CrestApps.Core.Tests.Framework.AI.OpenAI.Azure;

public sealed class StreamingToolCallAccumulatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Append_InterleavedToolCalls_PreservesFirstIndexAndPerCallFragmentOrder(int toolCallCount)
    {
        var accumulator = new StreamingToolCallAccumulator();
        var indexes = Enumerable.Range(0, toolCallCount)
            .Select(position => ((position * 3) + 1) % toolCallCount)
            .ToArray();

        for (var fragmentIndex = 0; fragmentIndex < 3; fragmentIndex++)
        {
            var positions =
                fragmentIndex % 2 == 0
                    ? Enumerable.Range(0, toolCallCount)
                    : Enumerable.Range(0, toolCallCount).Reverse();

            foreach (var position in positions)
            {
                var index = indexes[position];
                accumulator.Append(CreateUpdate(
                    index,
                    fragmentIndex == 0
                        ? $"call-{index}"
                        : string.Empty,
                    fragmentIndex == 0
                        ? $"function-{index}"
                        : string.Empty,
                    BinaryData.FromBytes([(byte)index, (byte)fragmentIndex])));
            }
        }

        var toolCalls = accumulator.BuildToolCalls();

        Assert.Equal(toolCallCount, toolCalls.Count);

        for (var position = 0; position < toolCallCount; position++)
        {
            var index = indexes[position];
            var expectedBytes = new byte[]
            {
                (byte)index, 0,
                (byte)index, 1,
                (byte)index, 2,
            };

            Assert.Equal($"call-{index}", toolCalls[position].Id);
            Assert.Equal($"function-{index}", toolCalls[position].FunctionName);
            Assert.Equal(expectedBytes, toolCalls[position].FunctionArguments.ToArray());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(257)]
    public void Append_ZeroOneOrManyFragments_ConcatenatesExactBytes(int fragmentCount)
    {
        var accumulator = new StreamingToolCallAccumulator();
        var expected = new List<byte>();

        accumulator.Append(CreateUpdate(3, "call-3", "function-3"));

        for (var index = 0; index < fragmentCount; index++)
        {
            var fragment = new byte[]
            {
                (byte)index,
                (byte)(index >> 8),
                (byte)(255 - (index % 256)),
            };
            expected.AddRange(fragment);
            accumulator.Append(CreateUpdate(3, arguments: BinaryData.FromBytes(fragment)));
        }

        var toolCall = Assert.Single(accumulator.BuildToolCalls());

        Assert.Equal(expected, toolCall.FunctionArguments.ToArray());
    }

    [Fact]
    public void Append_NullAndEmptyFragments_DoNotChangeArguments()
    {
        var accumulator = new StreamingToolCallAccumulator();

        accumulator.Append(CreateUpdate(0, "call-0", "function-0"));
        accumulator.Append(CreateUpdate(0, arguments: BinaryData.Empty));
        accumulator.Append(CreateUpdate(0, arguments: BinaryData.FromBytes(ReadOnlyMemory<byte>.Empty)));
        accumulator.Append(CreateUpdate(0, arguments: BinaryData.FromBytes([1, 2, 3])));
        accumulator.Append(CreateUpdate(0, arguments: BinaryData.Empty));
        accumulator.Append(CreateUpdate(0));

        var toolCall = Assert.Single(accumulator.BuildToolCalls());

        Assert.Equal(new byte[] { 1, 2, 3 }, toolCall.FunctionArguments.ToArray());
    }

    [Fact]
    public void Append_SlicedMemory_CopiesOnlyTheSelectedBytes()
    {
        var buffer = new byte[] { 90, 91, 1, 2, 3, 4, 92, 93 };
        var accumulator = new StreamingToolCallAccumulator();

        accumulator.Append(CreateUpdate(
            0,
            "call-0",
            "function-0",
            BinaryData.FromBytes(buffer.AsMemory(2, 4))));
        buffer.AsSpan().Fill(255);

        var toolCall = Assert.Single(accumulator.BuildToolCalls());

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, toolCall.FunctionArguments.ToArray());
    }

    [Fact]
    public void Append_SplitMultiByteUtf8_PreservesTheOriginalEncoding()
    {
        const string Json = """{"emoji":"😀","currency":"€"}""";
        var bytes = Encoding.UTF8.GetBytes(Json);
        var emojiStart = Json.IndexOf("😀", StringComparison.Ordinal);
        var emojiByteStart = Encoding.UTF8.GetByteCount(Json.AsSpan(0, emojiStart));
        var euroStart = Json.IndexOf('€');
        var euroByteStart = Encoding.UTF8.GetByteCount(Json.AsSpan(0, euroStart));
        var splitPoints = new[]
        {
            0,
            emojiByteStart + 1,
            emojiByteStart + 3,
            euroByteStart + 2,
            bytes.Length,
        };
        var accumulator = new StreamingToolCallAccumulator();

        accumulator.Append(CreateUpdate(0, "call-0", "function-0"));

        for (var index = 0; index < splitPoints.Length - 1; index++)
        {
            var offset = splitPoints[index];
            var length = splitPoints[index + 1] - offset;
            accumulator.Append(CreateUpdate(
                0,
                arguments: BinaryData.FromBytes(bytes.AsMemory(offset, length))));
        }

        var arguments = Assert.Single(accumulator.BuildToolCalls()).FunctionArguments;

        Assert.Equal(bytes, arguments.ToArray());
        Assert.Equal(Json, arguments.ToString());
    }

    [Fact]
    public void Append_ArbitraryBinaryBytes_PreservesEveryByte()
    {
        var fragments = new[]
        {
            new byte[] { 0, 127, 128 },
            new byte[] { 192, 175, 224, 128, 128 },
            new byte[] { 237, 160, 128, 244, 144, 128, 128, 254, 255 },
        };
        var expected = fragments.SelectMany(fragment => fragment).ToArray();
        var accumulator = new StreamingToolCallAccumulator();

        accumulator.Append(CreateUpdate(0, "call-0", "function-0"));

        foreach (var fragment in fragments)
        {
            accumulator.Append(CreateUpdate(0, arguments: BinaryData.FromBytes(fragment)));
        }

        var toolCall = Assert.Single(accumulator.BuildToolCalls());

        Assert.Equal(expected, toolCall.FunctionArguments.ToArray());
    }

    [Fact]
    public void BuildToolCalls_UsesLatestNonEmptyMetadataAndFiltersIncompleteCalls()
    {
        var accumulator = new StreamingToolCallAccumulator();

        accumulator.Append(CreateUpdate(8, arguments: BinaryData.FromBytes([8])));
        accumulator.Append(CreateUpdate(8, "call-old", "function-old"));
        accumulator.Append(CreateUpdate(8, string.Empty, string.Empty));
        accumulator.Append(CreateUpdate(8, "call-new", "function-new"));
        accumulator.Append(CreateUpdate(2, functionName: "missing-id", arguments: BinaryData.FromBytes([2])));
        accumulator.Append(CreateUpdate(5, "missing-name", arguments: BinaryData.FromBytes([5])));
        accumulator.Append(CreateUpdate(3, " ", "\t", BinaryData.FromBytes([3])));
        accumulator.Append(CreateUpdate(1, "call-new", "duplicate-id", BinaryData.FromBytes([1])));

        var toolCalls = accumulator.BuildToolCalls();

        Assert.Collection(
            toolCalls,
            toolCall =>
            {
                Assert.Equal("call-new", toolCall.Id);
                Assert.Equal("function-new", toolCall.FunctionName);
                Assert.Equal(new byte[] { 8 }, toolCall.FunctionArguments.ToArray());
            },
            toolCall =>
            {
                Assert.Equal(" ", toolCall.Id);
                Assert.Equal("\t", toolCall.FunctionName);
                Assert.Equal(new byte[] { 3 }, toolCall.FunctionArguments.ToArray());
            },
            toolCall =>
            {
                Assert.Equal("call-new", toolCall.Id);
                Assert.Equal("duplicate-id", toolCall.FunctionName);
                Assert.Equal(new byte[] { 1 }, toolCall.FunctionArguments.ToArray());
            });
    }

    [Fact]
    public void BuildToolCalls_RepeatedFinalizationAndSubsequentAppendsKeepPriorBinaryDataStable()
    {
        var accumulator = new StreamingToolCallAccumulator();

        accumulator.Append(CreateUpdate(0, "call-0", "function-0", BinaryData.FromBytes([1, 2])));
        accumulator.Append(CreateUpdate(0, arguments: BinaryData.FromBytes([3])));

        var first = Assert.Single(accumulator.BuildToolCalls()).FunctionArguments;
        var second = Assert.Single(accumulator.BuildToolCalls()).FunctionArguments;

        accumulator.Append(CreateUpdate(0, arguments: BinaryData.FromBytes([4])));

        var third = Assert.Single(accumulator.BuildToolCalls()).FunctionArguments;

        Assert.Equal(new byte[] { 1, 2, 3 }, first.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, second.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, third.ToArray());

        accumulator.Clear();
        accumulator.Append(CreateUpdate(0, "call-next", "function-next", BinaryData.FromBytes([9, 8])));

        var afterClear = Assert.Single(accumulator.BuildToolCalls()).FunctionArguments;

        Assert.Equal(new byte[] { 1, 2, 3 }, first.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, second.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, third.ToArray());
        Assert.Equal(new byte[] { 9, 8 }, afterClear.ToArray());
    }

    [Fact]
    public void BuildToolCalls_EmptyAndRepeatedFinalization_ReturnsEmptyCollections()
    {
        var accumulator = new StreamingToolCallAccumulator();

        Assert.Empty(accumulator.BuildToolCalls());
        Assert.Empty(accumulator.BuildToolCalls());

        accumulator.Append(CreateUpdate(-1, functionName: "missing-id"));
        accumulator.Append(CreateUpdate(int.MaxValue, "missing-name"));

        Assert.Empty(accumulator.BuildToolCalls());
        Assert.Empty(accumulator.BuildToolCalls());
    }

    [Fact]
    public void Clear_RemovesAllIndexesAndRestartsInsertionOrder()
    {
        var accumulator = new StreamingToolCallAccumulator();

        accumulator.Append(CreateUpdate(4, "call-4", "function-4"));
        accumulator.Append(CreateUpdate(2, "call-2", "function-2"));
        accumulator.Clear();
        accumulator.Append(CreateUpdate(2, "next-2", "next-function-2"));
        accumulator.Append(CreateUpdate(4, "next-4", "next-function-4"));

        var toolCalls = accumulator.BuildToolCalls();

        Assert.Equal(["next-2", "next-4"], toolCalls.Select(toolCall => toolCall.Id));
    }

    [Fact]
    public void Append_NullUpdate_PreservesLegacyNullReferenceException()
    {
        var accumulator = new StreamingToolCallAccumulator();

        Assert.Throws<NullReferenceException>(() => accumulator.Append(null));
    }

    [Fact]
    public void BinaryDataFromBytes_WrapsSlicedMemoryWithoutCopying()
    {
        var buffer = new byte[] { 90, 1, 2, 3, 91 };
        var data = BinaryData.FromBytes(buffer.AsMemory(1, 3));

        buffer[2] = 42;

        Assert.Equal(new byte[] { 1, 42, 3 }, data.ToArray());
    }

    [Fact]
    public void Accumulator_MatchesLegacyImplementationForMixedSdkUpdates()
    {
        var slicedBuffer = new byte[] { 90, 1, 2, 3, 91 };
        var updates = new[]
        {
            CreateUpdate(7, "call-7-old", "function-7", BinaryData.FromBytes([0, 255])),
            CreateUpdate(2, functionName: "missing-id", arguments: BinaryData.FromBytes([2])),
            CreateUpdate(7, string.Empty, string.Empty, BinaryData.Empty),
            CreateUpdate(3, "call-3", "function-3", BinaryData.FromBytes(slicedBuffer.AsMemory(1, 3))),
            CreateUpdate(7, "call-7-new", arguments: BinaryData.FromBytes([128, 192, 175])),
            CreateUpdate(5, "missing-name", arguments: BinaryData.FromBytes([5])),
            CreateUpdate(3, arguments: BinaryData.FromBytes([4, 5])),
        };
        var accumulator = new StreamingToolCallAccumulator();

        foreach (var update in updates)
        {
            accumulator.Append(update);
        }

        var legacy = AccumulateLegacy(updates);
        var current = accumulator.BuildToolCalls();

        AssertEquivalent(legacy, current);
    }

    private static StreamingChatToolCallUpdate CreateUpdate(
        int index,
        string toolCallId = null,
        string functionName = null,
        BinaryData arguments = null)
    {
        return OpenAIChatModelFactory.StreamingChatToolCallUpdate(
            index,
            toolCallId,
            ChatToolCallKind.Function,
            functionName,
            arguments);
    }

    private static List<ChatToolCall> AccumulateLegacy(IEnumerable<StreamingChatToolCallUpdate> updates)
    {
        var accumulatedToolCalls =
            new Dictionary<int, (string ToolCallId, string FunctionName, List<byte> ArgumentBytes)>();

        foreach (var update in updates)
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

        return accumulatedToolCalls.Values
            .Where(toolCall =>
                !string.IsNullOrEmpty(toolCall.ToolCallId) &&
                !string.IsNullOrEmpty(toolCall.FunctionName))
            .Select(toolCall => ChatToolCall.CreateFunctionToolCall(
                toolCall.ToolCallId,
                toolCall.FunctionName,
                BinaryData.FromBytes(toolCall.ArgumentBytes.ToArray())))
            .ToList();
    }

    private static void AssertEquivalent(
        List<ChatToolCall> expected,
        List<ChatToolCall> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].FunctionName, actual[index].FunctionName);
            Assert.Equal(
                expected[index].FunctionArguments.ToArray(),
                actual[index].FunctionArguments.ToArray());
        }
    }
}
