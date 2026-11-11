using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.AGUI;

/// <summary>
/// Writes one AG-UI run: turns the updates of a chat completion into AG-UI events (run lifecycle, text, reasoning,
/// tool calls and their results, shared state, interrupts for tool approvals) and hands each event to a sink. It
/// also keeps the tool calls of the run, so the chat can store them with the assistant message.
/// </summary>
/// <remarks>
/// All the text of one assistant message keeps the message's id, so a client can rate or copy it. When the model
/// writes text again after a tool call, that text is a new AG-UI text message whose id is the message id followed
/// by <c>-1</c>, <c>-2</c>, and so on.
/// </remarks>
public sealed class AGUIRunEventWriter
{
    /// <summary>
    /// The most characters of a tool result put on the wire and kept with the message.
    /// </summary>
    public const int MaxToolResultLength = 4000;

    private static readonly JsonElement _approvalResponseSchema = JsonElement.Parse("""
        {
          "type": "object",
          "properties": {
            "approved": { "type": "boolean" }
          },
          "required": ["approved"]
        }
        """);

    // Tool arguments go on the wire as compact JSON; the shared Microsoft.Extensions.AI options indent it.
    private static readonly JsonSerializerOptions _compactJsonOptions = new(AIJsonUtilities.DefaultOptions)
    {
        WriteIndented = false,
    };

    private readonly Func<BaseEvent, CancellationToken, ValueTask> _sink;
    private readonly IReadOnlySet<string> _clientToolNames;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ChatTurnToolCall> _toolCalls = [];
    private readonly Dictionary<string, ChatTurnToolCall> _toolCallsById = new(StringComparer.Ordinal);
    private readonly List<AGUIInterrupt> _interrupts = [];
    private readonly HashSet<string> _textMessageIds = new(StringComparer.Ordinal);

    private string _openTextMessageId;
    private string _openReasoningMessageId;
    private string _textBaseMessageId;
    private int _textSegment;
    private bool _textInSegment;
    private int _reasoningCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="AGUIRunEventWriter"/> class.
    /// </summary>
    /// <param name="threadId">The thread (chat session) id, when it is known already.</param>
    /// <param name="runId">The run id.</param>
    /// <param name="sink">Receives each event, in order.</param>
    /// <param name="clientToolNames">The names of the tools the client runs itself; a call to one ends the run waiting for the client's result.</param>
    public AGUIRunEventWriter(
        string threadId,
        string runId,
        Func<BaseEvent, CancellationToken, ValueTask> sink,
        IReadOnlySet<string> clientToolNames = null)
    {
        ThreadId = threadId;
        RunId = string.IsNullOrEmpty(runId) ? Guid.NewGuid().ToString("n") : runId;
        _sink = sink;
        _clientToolNames = clientToolNames ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets or sets the thread (chat session) id. Set it before the run starts when the session is created by the run.
    /// </summary>
    public string ThreadId { get; set; }

    /// <summary>
    /// Gets the run id.
    /// </summary>
    public string RunId { get; }

    /// <summary>
    /// Gets a value indicating whether <c>RUN_STARTED</c> was written.
    /// </summary>
    public bool IsStarted { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the run ended, with <c>RUN_FINISHED</c> or <c>RUN_ERROR</c>.
    /// </summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// Gets the tool calls of the run, in order.
    /// </summary>
    public IReadOnlyList<ChatTurnToolCall> ToolCalls => _toolCalls;

    /// <summary>
    /// Gets the ids of the tool calls a person rejected before this run, whose results the run reports as rejected.
    /// </summary>
    public ISet<string> RejectedCallIds { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the interrupts the run ends with: the tool calls that wait for an approval.
    /// </summary>
    public IReadOnlyList<AGUIInterrupt> Interrupts => _interrupts;

    /// <summary>
    /// Determines whether text was written for an assistant message.
    /// </summary>
    /// <param name="messageId">The id of the assistant message.</param>
    /// <returns><see langword="true"/> when text was written for it.</returns>
    public bool HasText(string messageId)
    {
        return messageId is not null && _textMessageIds.Contains(messageId);
    }

    /// <summary>
    /// Writes <c>RUN_STARTED</c>, unless it was written already.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureStartedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes the events for one update of the completion of an assistant message.
    /// </summary>
    /// <param name="update">The update.</param>
    /// <param name="messageId">The id of the assistant message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the events are written.</returns>
    public async ValueTask WriteUpdateAsync(ChatResponseUpdate update, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureStartedAsync(cancellationToken);

            foreach (var content in update.Contents)
            {
                await WriteContentAsync(content, messageId, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes text of an assistant message.
    /// </summary>
    /// <param name="messageId">The id of the assistant message.</param>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the events are written.</returns>
    public async ValueTask WriteTextAsync(string messageId, string text, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureStartedAsync(cancellationToken);
            await WriteTextCoreAsync(messageId, text, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes the whole shared state (<c>STATE_SNAPSHOT</c>).
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public ValueTask WriteStateSnapshotAsync(JsonObject state, CancellationToken cancellationToken = default)
    {
        return WriteEventAsync(new StateSnapshotEvent
        {
            Snapshot = JsonSerializer.SerializeToElement(state ?? [], AGUIJson.SerializerOptions),
        }, cancellationToken);
    }

    /// <summary>
    /// Writes a change of the shared state, as JSON Patch operations (<c>STATE_DELTA</c>).
    /// </summary>
    /// <param name="operations">The JSON Patch operations.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public ValueTask WriteStateDeltaAsync(JsonArray operations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        return WriteEventAsync(new StateDeltaEvent
        {
            Delta = JsonSerializer.SerializeToElement(operations, AGUIJson.SerializerOptions),
        }, cancellationToken);
    }

    /// <summary>
    /// Writes an application event (<c>CUSTOM</c>).
    /// </summary>
    /// <param name="name">The name of the event.</param>
    /// <param name="value">The value, serialized to JSON.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public ValueTask WriteCustomAsync(string name, object value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return WriteEventAsync(new CustomEvent
        {
            Name = name,
            Value = value is null ? null : JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    /// <summary>
    /// Writes the start of a named step of the run (<c>STEP_STARTED</c>).
    /// </summary>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public ValueTask WriteStepStartedAsync(string stepName, CancellationToken cancellationToken = default)
    {
        return WriteEventAsync(new StepStartedEvent { StepName = stepName }, cancellationToken);
    }

    /// <summary>
    /// Writes the end of a named step of the run (<c>STEP_FINISHED</c>).
    /// </summary>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public ValueTask WriteStepFinishedAsync(string stepName, CancellationToken cancellationToken = default)
    {
        return WriteEventAsync(new StepFinishedEvent { StepName = stepName }, cancellationToken);
    }

    /// <summary>
    /// Writes any event, after <c>RUN_STARTED</c>.
    /// </summary>
    /// <param name="evt">The event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public async ValueTask WriteEventAsync(BaseEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureStartedAsync(cancellationToken);
            await EmitAsync(evt, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Ends the run with <c>RUN_FINISHED</c>: an interrupt when tool calls wait for an approval, otherwise a success
    /// that names the calls waiting for the client's result. Does nothing when the run ended already.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public async ValueTask FinishAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (IsFinished)
            {
                return;
            }

            await EnsureStartedAsync(cancellationToken);
            await CloseOpenMessagesAsync(cancellationToken);

            RunFinishedOutcome outcome = _interrupts.Count > 0
                ? new RunFinishedInterruptOutcome { Interrupts = [.. _interrupts] }
                : new RunFinishedSuccessOutcome
                {
                    PendingToolCallIds = _toolCalls
                        .Where(call => call.Status == ChatTurnToolCallStatus.AwaitingClient)
                        .Select(call => call.CallId)
                        .ToList() is { Count: > 0 } pending
                            ? pending
                            : null,
                };

            await EmitAsync(new RunFinishedEvent
            {
                ThreadId = ThreadId,
                RunId = RunId,
                Outcome = outcome,
            }, cancellationToken);

            IsFinished = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Ends the run with <c>RUN_ERROR</c>. Does nothing when the run ended already.
    /// </summary>
    /// <param name="message">The message for the user.</param>
    /// <param name="code">A code for the error, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public async ValueTask ErrorAsync(string message, string code = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (IsFinished)
            {
                return;
            }

            await EnsureStartedAsync(cancellationToken);
            await CloseOpenMessagesAsync(cancellationToken);
            await EmitAsync(new RunErrorEvent
            {
                Message = message,
                Code = code,
            }, cancellationToken);

            IsFinished = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask WriteContentAsync(AIContent content, string messageId, CancellationToken cancellationToken)
    {
        switch (content)
        {
            case TextReasoningContent reasoning:
                await WriteReasoningAsync(reasoning, messageId, cancellationToken);
                break;

            case TextContent text:
                await WriteTextCoreAsync(messageId, text.Text, cancellationToken);
                break;

            case FunctionCallContent call:
                await WriteToolCallAsync(call, messageId, cancellationToken);
                break;

            case FunctionResultContent result:
                await WriteToolResultAsync(result, cancellationToken);
                break;

#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
            case ToolApprovalRequestContent approval when approval.ToolCall is FunctionCallContent approvalCall:
                await WriteApprovalRequestAsync(approval.RequestId, approvalCall, messageId, cancellationToken);
                break;
#pragma warning restore MEAI001
        }
    }

    private async ValueTask WriteTextCoreAsync(string messageId, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await CloseReasoningAsync(cancellationToken);

        if (!string.Equals(_textBaseMessageId, messageId, StringComparison.Ordinal))
        {
            await CloseTextAsync(cancellationToken);
            _textBaseMessageId = messageId;
            _textSegment = 0;
            _textInSegment = false;
        }

        var segmentId = _textSegment == 0
            ? messageId
            : $"{messageId}-{_textSegment}";

        if (!string.Equals(_openTextMessageId, segmentId, StringComparison.Ordinal))
        {
            await CloseTextAsync(cancellationToken);
            await EmitAsync(new TextMessageStartEvent
            {
                MessageId = segmentId,
                Role = AGUIRoles.Assistant,
            }, cancellationToken);
            _openTextMessageId = segmentId;
        }

        _textInSegment = true;

        if (messageId is not null)
        {
            _textMessageIds.Add(messageId);
        }

        await EmitAsync(new TextMessageContentEvent
        {
            MessageId = segmentId,
            Delta = text,
        }, cancellationToken);
    }

    private async ValueTask WriteReasoningAsync(TextReasoningContent reasoning, string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(reasoning.Text))
        {
            return;
        }

        await CloseTextAsync(cancellationToken);

        if (_openReasoningMessageId is null)
        {
            _openReasoningMessageId = $"{messageId ?? RunId}-reasoning-{++_reasoningCount}";

            await EmitAsync(new ReasoningStartEvent { MessageId = _openReasoningMessageId }, cancellationToken);
            await EmitAsync(new ReasoningMessageStartEvent { MessageId = _openReasoningMessageId }, cancellationToken);
        }

        await EmitAsync(new ReasoningMessageContentEvent
        {
            MessageId = _openReasoningMessageId,
            Delta = reasoning.Text,
        }, cancellationToken);
    }

    private async ValueTask WriteToolCallAsync(FunctionCallContent call, string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(call.CallId) || _toolCallsById.ContainsKey(call.CallId))
        {
            return;
        }

        var status = _clientToolNames.Contains(call.Name)
            ? ChatTurnToolCallStatus.AwaitingClient
            : ChatTurnToolCallStatus.Requested;

        await StartToolCallAsync(call, messageId, status, approvalRequestId: null, cancellationToken);
    }

    private async ValueTask WriteApprovalRequestAsync(string requestId, FunctionCallContent call, string messageId, CancellationToken cancellationToken)
    {
        if (!_toolCallsById.TryGetValue(call.CallId, out var record))
        {
            record = await StartToolCallAsync(call, messageId, ChatTurnToolCallStatus.AwaitingApproval, requestId, cancellationToken);
        }
        else
        {
            record.Status = ChatTurnToolCallStatus.AwaitingApproval;
            record.ApprovalRequestId = requestId;
        }

        _interrupts.Add(new AGUIInterrupt
        {
            Id = requestId,
            Reason = InterruptReasons.ToolCall,
            ToolCallId = call.CallId,
            Message = $"Allow the assistant to run '{call.Name}'?",
            ResponseSchema = _approvalResponseSchema,
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["toolName"] = call.Name,
                ["arguments"] = JsonElement.Parse(record.Arguments ?? "{}"),
            }, AIJsonUtilities.DefaultOptions),
        });
    }

    private async ValueTask<ChatTurnToolCall> StartToolCallAsync(
        FunctionCallContent call,
        string messageId,
        ChatTurnToolCallStatus status,
        string approvalRequestId,
        CancellationToken cancellationToken)
    {
        await CloseReasoningAsync(cancellationToken);
        await CloseTextAsync(cancellationToken);

        // Text the model writes after this call is a new text message.
        if (_textInSegment && string.Equals(_textBaseMessageId, messageId, StringComparison.Ordinal))
        {
            _textSegment++;
            _textInSegment = false;
        }

        var arguments = JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object>(), _compactJsonOptions);
        var record = new ChatTurnToolCall
        {
            CallId = call.CallId,
            Name = call.Name,
            Arguments = arguments,
            Status = status,
            ApprovalRequestId = approvalRequestId,
        };

        _toolCalls.Add(record);
        _toolCallsById[call.CallId] = record;

        await EmitAsync(new ToolCallStartEvent
        {
            ToolCallId = call.CallId,
            ToolCallName = call.Name,
            ParentMessageId = messageId,
        }, cancellationToken);
        await EmitAsync(new ToolCallArgsEvent
        {
            ToolCallId = call.CallId,
            Delta = arguments,
        }, cancellationToken);
        await EmitAsync(new ToolCallEndEvent { ToolCallId = call.CallId }, cancellationToken);

        return record;
    }

    private async ValueTask WriteToolResultAsync(FunctionResultContent result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(result.CallId))
        {
            return;
        }

        var text = Truncate(ResultToText(result));

        if (_toolCallsById.TryGetValue(result.CallId, out var record))
        {
            record.Result = text;
            record.Status = RejectedCallIds.Contains(result.CallId)
                ? ChatTurnToolCallStatus.Rejected
                : ChatTurnToolCallStatus.Completed;
        }

        await CloseReasoningAsync(cancellationToken);
        await EmitAsync(new ToolCallResultEvent
        {
            MessageId = $"{result.CallId}-result",
            ToolCallId = result.CallId,
            Content = new AGUIContent(text),
            Role = AGUIRoles.Tool,
        }, cancellationToken);
    }

    private async ValueTask CloseOpenMessagesAsync(CancellationToken cancellationToken)
    {
        await CloseReasoningAsync(cancellationToken);
        await CloseTextAsync(cancellationToken);
    }

    private async ValueTask CloseTextAsync(CancellationToken cancellationToken)
    {
        if (_openTextMessageId is null)
        {
            return;
        }

        var id = _openTextMessageId;
        _openTextMessageId = null;

        await EmitAsync(new TextMessageEndEvent { MessageId = id }, cancellationToken);
    }

    private async ValueTask CloseReasoningAsync(CancellationToken cancellationToken)
    {
        if (_openReasoningMessageId is null)
        {
            return;
        }

        var id = _openReasoningMessageId;
        _openReasoningMessageId = null;

        await EmitAsync(new ReasoningMessageEndEvent { MessageId = id }, cancellationToken);
        await EmitAsync(new ReasoningEndEvent { MessageId = id }, cancellationToken);
    }

    private async ValueTask EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (IsStarted)
        {
            return;
        }

        IsStarted = true;

        await EmitAsync(new RunStartedEvent
        {
            ThreadId = ThreadId,
            RunId = RunId,
            ProtocolVersion = AGUIProtocol.Version,
        }, cancellationToken);
    }

    private ValueTask EmitAsync(BaseEvent evt, CancellationToken cancellationToken)
    {
        if (IsFinished)
        {
            return ValueTask.CompletedTask;
        }

        return _sink(evt, cancellationToken);
    }

    private static string ResultToText(FunctionResultContent result)
    {
        return result.Result switch
        {
            null => result.Exception?.Message ?? string.Empty,
            string text => text,
            JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText(),
            _ => JsonSerializer.Serialize(result.Result, _compactJsonOptions),
        };
    }

    private static string Truncate(string text)
    {
        return text.Length > MaxToolResultLength
            ? string.Concat(text.AsSpan(0, MaxToolResultLength), "…")
            : text;
    }
}
