#pragma warning disable MEAI001 // The realtime types from Microsoft.Extensions.AI are for evaluation purposes only.
#nullable enable
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.OpenAI.Azure.Realtime;

/// <summary>
/// Translates between the <see cref="Microsoft.Extensions.AI"/> realtime message model and the
/// Azure OpenAI realtime WebSocket JSON event protocol.
/// </summary>
/// <remarks>
/// This is a temporary transport that exists only because the current <c>Azure.AI.OpenAI</c> package is
/// incompatible with the <c>OpenAI</c> SDK version required by <c>Microsoft.Extensions.AI.OpenAI</c>. It targets
/// the Azure OpenAI realtime preview event schema (flat <c>input_audio_format</c> / <c>output_audio_format</c>
/// fields). Delete the whole <c>Realtime</c> folder once an <c>Azure.AI.OpenAI</c> release targets a compatible
/// <c>OpenAI</c> SDK and <c>AzureOpenAIClient.GetRealtimeClient()</c> works again.
/// </remarks>
internal static class AzureRealtimeProtocol
{
    /// <summary>
    /// Serializes a client message into the Azure realtime JSON event bytes, or <see langword="null"/> when the
    /// message has no representation to send.
    /// </summary>
    public static ReadOnlyMemory<byte>? WriteClientMessage(RealtimeClientMessage message)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            switch (message)
            {
                case SessionUpdateRealtimeClientMessage sessionUpdate:
                    WriteSessionUpdate(writer, sessionUpdate);
                    break;

                case InputAudioBufferAppendRealtimeClientMessage audioAppend:
                    WriteInputAudioAppend(writer, audioAppend);
                    break;

                case InputAudioBufferCommitRealtimeClientMessage:
                    WriteSimpleEvent(writer, "input_audio_buffer.commit", message.MessageId);
                    break;

                case CreateResponseRealtimeClientMessage responseCreate:
                    WriteResponseCreate(writer, responseCreate);
                    break;

                case CreateConversationItemRealtimeClientMessage itemCreate:
                    WriteConversationItemCreate(writer, itemCreate);
                    break;

                default:
                    return WriteRaw(message);
            }
        }

        return buffer.WrittenMemory;
    }

    /// <summary>
    /// Parses an Azure realtime JSON server event into a <see cref="RealtimeServerMessage"/>. Unknown events map to
    /// <see cref="RealtimeServerMessageType.RawContentOnly"/> so nothing is silently lost.
    /// </summary>
    public static RealtimeServerMessage ReadServerMessage(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());
        var root = document.RootElement.Clone();

        var type = GetString(root, "type") ?? string.Empty;
        var eventId = GetString(root, "event_id");

        RealtimeServerMessage message = type switch
        {
            "error" => ReadError(root),
            "response.created" => new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseCreated) { ResponseId = GetString(GetProperty(root, "response"), "id") },
            "response.done" => ReadResponseDone(root),
            "response.output_item.added" => ReadOutputItem(root, RealtimeServerMessageType.ResponseOutputItemAdded),
            "response.output_item.done" => ReadOutputItem(root, RealtimeServerMessageType.ResponseOutputItemDone),
            "response.audio.delta" or "response.output_audio.delta" => ReadAudioDelta(root),
            "response.audio.done" or "response.output_audio.done" => ReadAudioMarker(root, RealtimeServerMessageType.OutputAudioDone),
            "response.audio_transcript.delta" or "response.output_audio_transcript.delta" => ReadTranscript(root, RealtimeServerMessageType.OutputAudioTranscriptionDelta, "delta"),
            "response.audio_transcript.done" or "response.output_audio_transcript.done" => ReadTranscript(root, RealtimeServerMessageType.OutputAudioTranscriptionDone, "transcript"),
            "response.text.delta" or "response.output_text.delta" => ReadTranscript(root, RealtimeServerMessageType.OutputTextDelta, "delta"),
            "response.text.done" or "response.output_text.done" => ReadTranscript(root, RealtimeServerMessageType.OutputTextDone, "text"),
            "conversation.item.input_audio_transcription.delta" => ReadInputTranscription(root, RealtimeServerMessageType.InputAudioTranscriptionDelta, "delta"),
            "conversation.item.input_audio_transcription.completed" => ReadInputTranscription(root, RealtimeServerMessageType.InputAudioTranscriptionCompleted, "transcript"),
            "conversation.item.input_audio_transcription.failed" => ReadInputTranscriptionFailed(root),
            "conversation.item.created" or "conversation.item.added" => ReadConversationItem(root, RealtimeServerMessageType.ConversationItemAdded),
            "conversation.item.done" => ReadConversationItem(root, RealtimeServerMessageType.ConversationItemDone),
            _ => new RealtimeServerMessage { Type = RealtimeServerMessageType.RawContentOnly },
        };

        message.MessageId = eventId;
        message.RawRepresentation = root;

        return message;
    }

    private static ReadOnlyMemory<byte>? WriteRaw(RealtimeClientMessage message)
    {
        switch (message.RawRepresentation)
        {
            case string json:
                return Encoding.UTF8.GetBytes(json);
            case ReadOnlyMemory<byte> bytes:
                return bytes;
            case byte[] bytes:
                return bytes;
            case JsonElement element:
                return Encoding.UTF8.GetBytes(element.GetRawText());
            default:
                return null;
        }
    }

    private static void WriteSimpleEvent(Utf8JsonWriter writer, string type, string? eventId)
    {
        writer.WriteStartObject();
        WriteType(writer, type, eventId);
        writer.WriteEndObject();
    }

    private static void WriteInputAudioAppend(Utf8JsonWriter writer, InputAudioBufferAppendRealtimeClientMessage message)
    {
        writer.WriteStartObject();
        WriteType(writer, "input_audio_buffer.append", message.MessageId);
        writer.WriteString("audio", Convert.ToBase64String(message.Content.Data.Span));
        writer.WriteEndObject();
    }

    private static void WriteSessionUpdate(Utf8JsonWriter writer, SessionUpdateRealtimeClientMessage message)
    {
        writer.WriteStartObject();
        WriteType(writer, "session.update", message.MessageId);
        writer.WritePropertyName("session");
        WriteSessionOptions(writer, message.Options);
        writer.WriteEndObject();
    }

    private static void WriteSessionOptions(Utf8JsonWriter writer, RealtimeSessionOptions options)
    {
        // Azure OpenAI GA realtime session schema (gpt-realtime): audio settings are nested under
        // session.audio.input / session.audio.output, and the session carries a "type": "realtime" discriminator.
        writer.WriteStartObject();

        writer.WriteString("type", "realtime");

        if (options.Model is not null)
        {
            writer.WriteString("model", options.Model);
        }

        if (options.Instructions is not null)
        {
            writer.WriteString("instructions", options.Instructions);
        }

        if (options.OutputModalities is { Count: > 0 })
        {
            writer.WriteStartArray("output_modalities");
            foreach (var modality in options.OutputModalities)
            {
                writer.WriteStringValue(modality);
            }

            writer.WriteEndArray();
        }

        if (options.MaxOutputTokens is { } maxTokens)
        {
            writer.WriteNumber("max_output_tokens", maxTokens);
        }

        writer.WriteStartObject("audio");

        writer.WriteStartObject("input");
        WriteAudioFormat(writer, options.InputAudioFormat);
        if (options.TranscriptionOptions is { } transcription && transcription.ModelId is not null)
        {
            writer.WriteStartObject("transcription");
            writer.WriteString("model", transcription.ModelId);
            if (transcription.SpeechLanguage is not null)
            {
                // The realtime transcription API expects an ISO-639-1 language code (for example "en"),
                // not a full culture name ("en-US"), so use the primary subtag.
                var language = transcription.SpeechLanguage.Split('-', '_')[0];
                if (!string.IsNullOrWhiteSpace(language))
                {
                    writer.WriteString("language", language);
                }
            }

            writer.WriteEndObject();
        }

        if (options.VoiceActivityDetection is { } vad)
        {
            if (vad.Enabled)
            {
                // The algorithm, eagerness, silence duration and detection threshold are not expressible via the
                // MEAI VAD options, so they ride RawRepresentationFactory (see DefaultRealtimeSessionConfigurator /
                // RealtimeTurnDetectionOverrides).
                var overrides = options.RawRepresentationFactory?.Invoke() as RealtimeTurnDetectionOverrides;

                WriteTurnDetection(writer, vad.AllowInterruption, overrides);
            }
            else
            {
                writer.WriteNull("turn_detection");
            }
        }

        writer.WriteEndObject(); // input

        writer.WriteStartObject("output");
        if (options.Voice is not null)
        {
            writer.WriteString("voice", options.Voice);
        }

        WriteAudioFormat(writer, options.OutputAudioFormat);
        writer.WriteEndObject(); // output

        writer.WriteEndObject(); // audio

        WriteTools(writer, options.Tools);
        WriteToolChoice(writer, options.ToolMode);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a <c>turn_detection</c> object. Semantic detection carries only its eagerness; server VAD carries the
    /// silence window and threshold. Sending the server-VAD knobs alongside <c>semantic_vad</c> is rejected by the
    /// provider, so the two sets are never mixed.
    /// </summary>
    public static void WriteTurnDetection(Utf8JsonWriter writer, bool allowInterruption, RealtimeTurnDetectionOverrides? overrides)
    {
        var type = string.IsNullOrWhiteSpace(overrides?.Type) ? RealtimeTurnDetectionTypes.ServerVad : overrides!.Type;
        var semantic = string.Equals(type, RealtimeTurnDetectionTypes.SemanticVad, StringComparison.OrdinalIgnoreCase);

        writer.WriteStartObject("turn_detection");
        writer.WriteString("type", semantic ? RealtimeTurnDetectionTypes.SemanticVad : RealtimeTurnDetectionTypes.ServerVad);
        writer.WriteBoolean("create_response", true);
        writer.WriteBoolean("interrupt_response", allowInterruption);

        if (semantic)
        {
            if (!string.IsNullOrWhiteSpace(overrides?.Eagerness))
            {
                writer.WriteString("eagerness", overrides!.Eagerness);
            }
        }
        else if (overrides is not null)
        {
            if (overrides.SilenceDurationMs is { } silenceMs)
            {
                writer.WriteNumber("silence_duration_ms", silenceMs);
            }

            if (overrides.Threshold is { } threshold)
            {
                writer.WriteNumber("threshold", threshold);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteResponseCreate(Utf8JsonWriter writer, CreateResponseRealtimeClientMessage message)
    {
        writer.WriteStartObject();
        WriteType(writer, "response.create", message.MessageId);
        writer.WriteStartObject("response");

        if (message.Instructions is not null)
        {
            writer.WriteString("instructions", message.Instructions);
        }

        if (message.MaxOutputTokens is { } maxTokens)
        {
            writer.WriteNumber("max_output_tokens", maxTokens);
        }

        if (message.OutputModalities is { Count: > 0 })
        {
            writer.WriteStartArray("output_modalities");
            foreach (var modality in message.OutputModalities)
            {
                writer.WriteStringValue(modality);
            }

            writer.WriteEndArray();
        }

        if (message.OutputVoice is not null || message.OutputAudioOptions is not null)
        {
            writer.WriteStartObject("audio");
            writer.WriteStartObject("output");
            if (message.OutputVoice is not null)
            {
                writer.WriteString("voice", message.OutputVoice);
            }

            WriteAudioFormat(writer, message.OutputAudioOptions);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        if (message.ExcludeFromConversation == true)
        {
            writer.WriteString("conversation", "none");
        }

        WriteTools(writer, message.Tools);
        WriteToolChoice(writer, message.ToolMode);

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteConversationItemCreate(Utf8JsonWriter writer, CreateConversationItemRealtimeClientMessage message)
    {
        writer.WriteStartObject();
        WriteType(writer, "conversation.item.create", message.MessageId);
        writer.WritePropertyName("item");
        WriteConversationItem(writer, message.Item);
        writer.WriteEndObject();
    }

    private static void WriteConversationItem(Utf8JsonWriter writer, RealtimeConversationItem item)
    {
        writer.WriteStartObject();

        if (item.Id is not null)
        {
            writer.WriteString("id", item.Id);
        }

        var firstContent = item.Contents is { Count: > 0 } ? item.Contents[0] : null;

        if (firstContent is FunctionResultContent functionResult)
        {
            writer.WriteString("type", "function_call_output");
            writer.WriteString("call_id", functionResult.CallId ?? string.Empty);
            writer.WriteString("output", functionResult.Result?.ToString() ?? string.Empty);
        }
        else if (firstContent is FunctionCallContent functionCall)
        {
            writer.WriteString("type", "function_call");
            writer.WriteString("call_id", functionCall.CallId ?? string.Empty);
            writer.WriteString("name", functionCall.Name);
            writer.WriteString("arguments", functionCall.Arguments is not null ? JsonSerializer.Serialize(functionCall.Arguments) : "{}");
        }
        else
        {
            writer.WriteString("type", "message");
            var role = item.Role?.Value ?? "user";
            writer.WriteString("role", role);
            writer.WriteStartArray("content");
            var textType = role == "assistant" ? "text" : "input_text";
            foreach (var content in item.Contents ?? [])
            {
                if (content is TextContent text)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", textType);
                    writer.WriteString("text", text.Text ?? string.Empty);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteTools(Utf8JsonWriter writer, IEnumerable<AITool>? tools)
    {
        if (tools is null)
        {
            return;
        }

        var functions = tools.OfType<AIFunction>().Where(function => !string.IsNullOrEmpty(function.Name)).ToArray();
        if (functions.Length == 0)
        {
            return;
        }

        writer.WriteStartArray("tools");
        foreach (var function in functions)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WriteString("name", function.Name);
            if (!string.IsNullOrEmpty(function.Description))
            {
                writer.WriteString("description", function.Description);
            }

            writer.WritePropertyName("parameters");
            function.JsonSchema.WriteTo(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteToolChoice(Utf8JsonWriter writer, ChatToolMode? toolMode)
    {
        switch (toolMode)
        {
            case RequiredChatToolMode required when required.RequiredFunctionName is not null:
                writer.WriteStartObject("tool_choice");
                writer.WriteString("type", "function");
                writer.WriteString("name", required.RequiredFunctionName);
                writer.WriteEndObject();
                break;
            case RequiredChatToolMode:
                writer.WriteString("tool_choice", "required");
                break;
            case NoneChatToolMode:
                writer.WriteString("tool_choice", "none");
                break;
            case AutoChatToolMode:
                writer.WriteString("tool_choice", "auto");
                break;
        }
    }

    private static void WriteType(Utf8JsonWriter writer, string type, string? eventId)
    {
        writer.WriteString("type", type);
        if (eventId is not null)
        {
            writer.WriteString("event_id", eventId);
        }
    }

    private static ErrorRealtimeServerMessage ReadError(JsonElement root)
    {
        var error = GetProperty(root, "error");
        var message = new ErrorRealtimeServerMessage
        {
            Error = new ErrorContent(GetString(error, "message"))
            {
                ErrorCode = GetString(error, "code"),
                Details = GetString(error, "param"),
            },
            OriginatingMessageId = GetString(error, "event_id"),
        };

        return message;
    }

    private static ResponseCreatedRealtimeServerMessage ReadResponseDone(JsonElement root)
    {
        var response = GetProperty(root, "response");
        return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = GetString(response, "id"),
            Status = GetString(response, "status"),
        };
    }

    private static ResponseOutputItemRealtimeServerMessage ReadOutputItem(JsonElement root, RealtimeServerMessageType type)
    {
        return new ResponseOutputItemRealtimeServerMessage(type)
        {
            ResponseId = GetString(root, "response_id"),
            OutputIndex = GetInt32(root, "output_index"),
            Item = ReadConversationItemElement(GetProperty(root, "item")),
        };
    }

    private static ResponseOutputItemRealtimeServerMessage ReadConversationItem(JsonElement root, RealtimeServerMessageType type)
    {
        return new ResponseOutputItemRealtimeServerMessage(type)
        {
            Item = ReadConversationItemElement(GetProperty(root, "item")),
        };
    }

    private static OutputTextAudioRealtimeServerMessage ReadAudioDelta(JsonElement root)
    {
        return new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDelta)
        {
            ResponseId = GetString(root, "response_id"),
            ItemId = GetString(root, "item_id"),
            OutputIndex = GetInt32(root, "output_index"),
            ContentIndex = GetInt32(root, "content_index"),
            Audio = GetString(root, "delta"),
        };
    }

    private static OutputTextAudioRealtimeServerMessage ReadAudioMarker(JsonElement root, RealtimeServerMessageType type)
    {
        return new OutputTextAudioRealtimeServerMessage(type)
        {
            ResponseId = GetString(root, "response_id"),
            ItemId = GetString(root, "item_id"),
            OutputIndex = GetInt32(root, "output_index"),
            ContentIndex = GetInt32(root, "content_index"),
        };
    }

    private static OutputTextAudioRealtimeServerMessage ReadTranscript(JsonElement root, RealtimeServerMessageType type, string field)
    {
        return new OutputTextAudioRealtimeServerMessage(type)
        {
            ResponseId = GetString(root, "response_id"),
            ItemId = GetString(root, "item_id"),
            OutputIndex = GetInt32(root, "output_index"),
            ContentIndex = GetInt32(root, "content_index"),
            Text = GetString(root, field),
        };
    }

    private static InputAudioTranscriptionRealtimeServerMessage ReadInputTranscription(JsonElement root, RealtimeServerMessageType type, string field)
    {
        return new InputAudioTranscriptionRealtimeServerMessage(type)
        {
            ItemId = GetString(root, "item_id"),
            ContentIndex = GetInt32(root, "content_index"),
            Transcription = GetString(root, field),
        };
    }

    private static InputAudioTranscriptionRealtimeServerMessage ReadInputTranscriptionFailed(JsonElement root)
    {
        var error = GetProperty(root, "error");
        return new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionFailed)
        {
            ItemId = GetString(root, "item_id"),
            ContentIndex = GetInt32(root, "content_index"),
            Error = new ErrorContent(GetString(error, "message"))
            {
                ErrorCode = GetString(error, "code"),
            },
        };
    }

    private static RealtimeConversationItem? ReadConversationItemElement(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetString(item, "id");
        var type = GetString(item, "type");

        switch (type)
        {
            case "function_call":
                var arguments = GetString(item, "arguments");
                return new RealtimeConversationItem(
                    [new FunctionCallContent(GetString(item, "call_id") ?? string.Empty, GetString(item, "name") ?? string.Empty, DeserializeArguments(arguments))],
                    id);

            case "function_call_output":
                return new RealtimeConversationItem(
                    [new FunctionResultContent(GetString(item, "call_id") ?? string.Empty, GetString(item, "output"))],
                    id);

            default:
                var contents = new List<AIContent>();
                if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        var text = GetString(part, "text") ?? GetString(part, "transcript");
                        if (!string.IsNullOrEmpty(text))
                        {
                            contents.Add(new TextContent(text));
                        }
                    }
                }

                var role = GetString(item, "role");
                return new RealtimeConversationItem(contents, id, role is null ? null : new ChatRole(role));
        }
    }

    private static Dictionary<string, object?>? DeserializeArguments(string? argumentsJson)
    {
        if (string.IsNullOrEmpty(argumentsJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteAudioFormat(Utf8JsonWriter writer, RealtimeAudioFormat? format)
    {
        // GA realtime audio format is an object: { "type": "audio/pcm", "rate": 24000 }.
        if (format is null)
        {
            return;
        }

        writer.WriteStartObject("format");
        writer.WriteString("type", format.MediaType);
        writer.WriteNumber("rate", format.SampleRate);
        writer.WriteEndObject();
    }

    private static JsonElement GetProperty(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : default;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt32(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
    }
}
