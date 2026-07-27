using System.Buffers;
using OpenAI.Chat;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

/// <summary>
/// Accumulates indexed streaming tool-call updates until they can be materialized.
/// </summary>
internal sealed class StreamingToolCallAccumulator
{
    /// <summary>
    /// Stores tool calls in the order in which each index first appears.
    /// </summary>
    private readonly Dictionary<int, (string ToolCallId, string FunctionName, ArrayBufferWriter<byte> ArgumentBytes)> _toolCalls = [];

    /// <summary>
    /// Appends one SDK tool-call update to the data associated with its index.
    /// </summary>
    /// <param name="update">The streaming tool-call update.</param>
    public void Append(StreamingChatToolCallUpdate update)
    {
        if (!_toolCalls.TryGetValue(update.Index, out var accumulated))
        {
            accumulated = (update.ToolCallId, update.FunctionName, null);
            _toolCalls[update.Index] = accumulated;
        }

        if (!string.IsNullOrEmpty(update.ToolCallId))
        {
            accumulated.ToolCallId = update.ToolCallId;
        }

        if (!string.IsNullOrEmpty(update.FunctionName))
        {
            accumulated.FunctionName = update.FunctionName;
        }

        if (update.FunctionArgumentsUpdate is { IsEmpty: false } argumentUpdate)
        {
            var argumentBytes = argumentUpdate.ToMemory();
            accumulated.ArgumentBytes ??= new ArrayBufferWriter<byte>(argumentBytes.Length);
            argumentBytes.Span.CopyTo(accumulated.ArgumentBytes.GetSpan(argumentBytes.Length));
            accumulated.ArgumentBytes.Advance(argumentBytes.Length);
        }

        _toolCalls[update.Index] = accumulated;
    }

    /// <summary>
    /// Materializes complete tool calls in first-index appearance order.
    /// </summary>
    /// <returns>The complete tool calls with non-empty identifiers and function names.</returns>
    public List<ChatToolCall> BuildToolCalls()
    {
        var toolCalls = new List<ChatToolCall>(_toolCalls.Count);

        foreach (var toolCall in _toolCalls.Values)
        {
            if (string.IsNullOrEmpty(toolCall.ToolCallId) ||
                string.IsNullOrEmpty(toolCall.FunctionName))
            {
                continue;
            }

            var argumentBytes = toolCall.ArgumentBytes?.WrittenMemory ?? ReadOnlyMemory<byte>.Empty;
            toolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                toolCall.ToolCallId,
                toolCall.FunctionName,
                BinaryData.FromBytes(argumentBytes)));
        }

        return toolCalls;
    }

    /// <summary>
    /// Removes all accumulated tool calls.
    /// </summary>
    public void Clear()
    {
        _toolCalls.Clear();
    }
}
