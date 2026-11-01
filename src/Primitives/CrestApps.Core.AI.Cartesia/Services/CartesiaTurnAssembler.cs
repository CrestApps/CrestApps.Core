#pragma warning disable MEAI001 // Speech and realtime APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.AI.Cartesia.Services;

/// <summary>
/// Turns the events of Cartesia's turns endpoint into the transcription messages a realtime session emits.
/// </summary>
/// <remarks>
/// <para>
/// Cartesia decides where a turn ends, so a turn's transcript is reported once, on <c>turn.end</c>, which carries the
/// definitive text. Before that, each <c>turn.update</c> carries the turn's transcript so far, never revised; only its new
/// words become a partial transcript, which a cascade reads as the caller talking (and so interrupting the assistant).
/// <c>turn.eager_end</c> is read as an update: Cartesia follows it with the turn's end or with <c>turn.resume</c>.
/// </para>
/// <para>
/// The turn signals are the ones a speech-to-speech provider's transcription sends. The caller starting to speak is
/// reported with the turn's first words rather than on <c>turn.start</c>, as for the other transcription legs, so a sound
/// with no words neither starts a turn nor cuts the assistant off. Punctuation is not words: a turn Cartesia opens as the
/// previous one ends can hold nothing but the previous sentence's full stop. The turn is committed just before its
/// transcript.
/// </para>
/// <para>
/// Such a turn, opened as the previous one ended, is one Cartesia can leave open through any length of silence, even with
/// words in it, until the caller speaks again: the caller who said "stop" would wait for an answer that never comes. So a
/// turn with words Cartesia has said nothing more about for the stall timeout (its own turn timeout and a margin) is ended
/// here. Cartesia's own end of that turn adds nothing, and words it hears in the same turn afterwards are a turn of their
/// own.
/// </para>
/// <para>
/// Cartesia's events carry no timing, so the completed transcript is metered with the length of the audio sent since the
/// previous turn ended.
/// </para>
/// <para>Not thread-safe: the session serializes its calls.</para>
/// </remarks>
internal sealed class CartesiaTurnAssembler
{
    private static readonly char[] _splitLeftovers = [',', '.', ';', ':'];

    private readonly Func<long> _audioBytesSent;
    private readonly int _bytesPerSecond;
    private readonly TimeSpan _stallTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    // The open Cartesia turn: its transcript so far, and how much of that was already committed here because Cartesia had
    // stopped answering for it.
    private string _transcript = string.Empty;
    private string _committed = string.Empty;
    private string _turnId;
    private long _lastEventTimestamp;

    private long _turnStartBytes;
    private int _turnCount;
    private string _itemId;
    private bool _speechStarted;

    /// <summary>
    /// Initializes a new instance of the <see cref="CartesiaTurnAssembler"/> class.
    /// </summary>
    /// <param name="audioBytesSent">Reads how many bytes of audio the session has sent so far.</param>
    /// <param name="bytesPerSecond">How many bytes one second of the session's audio takes.</param>
    /// <param name="stallTimeout">How long a turn with words may go without an event before it is ended here.</param>
    /// <param name="timeProvider">The clock, or <see langword="null"/> for the system's.</param>
    /// <param name="logger">The logger, or <see langword="null"/> for none.</param>
    public CartesiaTurnAssembler(
        Func<long> audioBytesSent,
        int bytesPerSecond,
        TimeSpan stallTimeout,
        TimeProvider timeProvider = null,
        ILogger logger = null)
    {
        ArgumentNullException.ThrowIfNull(audioBytesSent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerSecond);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stallTimeout, TimeSpan.Zero);

        _audioBytesSent = audioBytesSent;
        _bytesPerSecond = bytesPerSecond;
        _stallTimeout = stallTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Reads one event and returns the transcription messages it produces, in order.
    /// </summary>
    /// <param name="json">The event as Cartesia sent it.</param>
    public IReadOnlyList<RealtimeServerMessage> Process(ReadOnlySpan<byte> json)
    {
        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return [];
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var type = Text(root, "type");

        // Which events Cartesia sent, and for which turn, is what tells a turn Cartesia never ended from one that was
        // lost on the way. What was said is logged only at the most detailed level.
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Cartesia sent {Event}.", root.GetRawText());
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Cartesia sent {EventType} (turn {TurnId}).", type ?? "an event with no type", Text(root, "turn_id") ?? "none");
        }

        _lastEventTimestamp = _timeProvider.GetTimestamp();

        return type switch
        {
            "turn.start" => Start(Text(root, "turn_id")),
            "turn.update" or "turn.eager_end" => Update(Text(root, "transcript")),
            "turn.end" => End(Text(root, "transcript")),
            "error" => [Error(root)],
            _ => [],
        };
    }

    /// <summary>
    /// Ends the open turn when it has words Cartesia has said nothing more about for the stall timeout, and returns the
    /// messages that commit it; otherwise returns none.
    /// </summary>
    public IReadOnlyList<RealtimeServerMessage> EndStalledTurn()
    {
        if (!_speechStarted)
        {
            return [];
        }

        var silence = _timeProvider.GetElapsedTime(_lastEventTimestamp);

        if (silence < _stallTimeout)
        {
            return [];
        }

        var text = Uncommitted(_transcript);

        if (!HasWords(text))
        {
            return [];
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Cartesia had not ended turn {TurnId} {Milliseconds} ms after its last words; the turn is ended here.",
                _turnId ?? "none", (long)silence.TotalMilliseconds);
        }

        var messages = Commit(text);

        // Cartesia's own end of this turn will carry what was just committed; only words past it are new.
        _committed = _transcript;

        return messages;
    }

    private List<RealtimeServerMessage> Start(string turnId)
    {
        // Cartesia ends every turn before it starts the next, so nothing of the previous turn is still open.
        _turnId = turnId;
        _transcript = string.Empty;
        _committed = string.Empty;

        return [];
    }

    private List<RealtimeServerMessage> Update(string transcript)
    {
        transcript = transcript?.Trim() ?? string.Empty;

        // The transcript is the turn's so far, and what was sent is never revised, so only what follows it is new.
        var added = transcript.StartsWith(_transcript, StringComparison.Ordinal)
            ? transcript.Substring(_transcript.Length).Trim()
            : transcript;

        _transcript = transcript;

        if (!HasWords(added))
        {
            return [];
        }

        var messages = new List<RealtimeServerMessage>();

        if (!_speechStarted)
        {
            _speechStarted = true;
            messages.Add(CreateTurnSignal("input_audio_buffer.speech_started"));
        }

        messages.Add(CreateTranscription(RealtimeServerMessageType.InputAudioTranscriptionDelta, added));

        return messages;
    }

    private List<RealtimeServerMessage> End(string transcript)
    {
        var text = Uncommitted(transcript?.Trim() ?? string.Empty);

        _transcript = string.Empty;
        _committed = string.Empty;

        // A turn with no words, or none past what was already committed, says nothing; the next turn starts afresh.
        if (!HasWords(text))
        {
            _itemId = null;
            _speechStarted = false;

            return [];
        }

        return Commit(text);
    }

    private List<RealtimeServerMessage> Commit(string text)
    {
        var messages = new List<RealtimeServerMessage>();

        if (!_speechStarted)
        {
            messages.Add(CreateTurnSignal("input_audio_buffer.speech_started"));
        }

        messages.Add(CreateTurnSignal("input_audio_buffer.committed"));

        var completed = CreateTranscription(RealtimeServerMessageType.InputAudioTranscriptionCompleted, text);

        var sent = _audioBytesSent();
        completed.Usage = new UsageDetails
        {
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                [AIUsageAdditionalCounts.AudioDurationMs] = Math.Max(0, sent - _turnStartBytes) * 1000 / _bytesPerSecond,
            },
        };

        _turnStartBytes = sent;
        _itemId = null;
        _speechStarted = false;
        messages.Add(completed);

        return messages;
    }

    /// <summary>
    /// What a turn's transcript says past what was already committed of it, without the punctuation a split sentence
    /// leaves at its start.
    /// </summary>
    private string Uncommitted(string transcript)
    {
        var text = transcript.StartsWith(_committed, StringComparison.Ordinal)
            ? transcript.Substring(_committed.Length)
            : transcript;

        return text.Trim().TrimStart(_splitLeftovers).Trim();
    }

    private static bool HasWords(string text)
    {
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                return true;
            }
        }

        return false;
    }

    private static ErrorRealtimeServerMessage Error(JsonElement root)
    {
        var title = Text(root, "title");
        var message = Text(root, "message");
        var detail = string.IsNullOrWhiteSpace(title) ? message : string.IsNullOrWhiteSpace(message) ? title : $"{title}: {message}";

        return new ErrorRealtimeServerMessage
        {
            Error = new ErrorContent(string.IsNullOrWhiteSpace(detail) ? "Cartesia reported an error." : "Cartesia reported an error: " + detail),
        };
    }

    private RealtimeServerMessage CreateTurnSignal(string type)
    {
        // The signal names the turn's item, as a speech-to-speech provider's does, so the turn it starts or commits pairs
        // with the transcript that follows.
        var payload = $$"""{"type":{{JsonSerializer.Serialize(type)}},"item_id":{{JsonSerializer.Serialize(EnsureItemId())}}}""";

        using var document = JsonDocument.Parse(payload);

        return new RealtimeServerMessage
        {
            Type = RealtimeServerMessageType.RawContentOnly,
            RawRepresentation = document.RootElement.Clone(),
        };
    }

    private InputAudioTranscriptionRealtimeServerMessage CreateTranscription(RealtimeServerMessageType type, string transcription)
    {
        // The partials and the completed transcript of one turn share an item, as a native provider's do.
        var itemId = EnsureItemId();

        return new InputAudioTranscriptionRealtimeServerMessage(type)
        {
            ItemId = itemId,
            MessageId = itemId,
            ContentIndex = 0,
            Transcription = transcription,
        };
    }

    private string EnsureItemId()
        => _itemId ??= "cartesia_turn_" + (++_turnCount).ToString(CultureInfo.InvariantCulture);

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
