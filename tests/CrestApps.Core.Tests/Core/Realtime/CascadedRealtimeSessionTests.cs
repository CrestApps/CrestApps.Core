#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
#nullable enable
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Realtime;

/// <summary>
/// Verifies that <see cref="CascadedRealtimeSession"/> turns three ordinary clients into something that
/// behaves like a speech-to-speech provider: the user's transcript starts a turn, the reply is streamed as
/// transcript and audio, and speaking over the assistant cuts the reply short.
/// </summary>
public sealed class CascadedRealtimeSessionTests
{
    private static readonly byte[] _audioChunk = [1, 2, 3, 4];

    [Fact]
    public async Task Session_TurnsACommittedTranscriptIntoASpokenReply()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out _, reply: "All good.");

        transcription.Emit(Completed("turn the lights on"));
        transcription.Complete();

        var messages = await ReadAllAsync(session);

        // The user's own transcript is passed through so the caller can show what was heard...
        Assert.Contains(messages, message => message.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted);

        // ...then the reply arrives as a response, with transcript and audio, and is closed off.
        Assert.Contains(messages, message => message.Type == RealtimeServerMessageType.ResponseCreated);
        Assert.Contains(messages, message => message.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta);
        Assert.Contains(messages, message => message.Type == RealtimeServerMessageType.OutputAudioTranscriptionDone);
        Assert.Contains(messages, message => message.Type == RealtimeServerMessageType.ResponseDone);

        var audio = messages
            .OfType<OutputTextAudioRealtimeServerMessage>()
            .Where(message => message.Type == RealtimeServerMessageType.OutputAudioDelta)
            .ToList();

        Assert.NotEmpty(audio);
        Assert.Equal(Convert.ToBase64String(_audioChunk), audio[0].Audio);
    }

    [Fact]
    public async Task Session_SendsTheUserTranscriptToTheChatClient()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out var chatClient, reply: "Done.");

        transcription.Emit(Completed("turn the lights on"));
        transcription.Complete();

        await ReadAllAsync(session);

        var lastRequest = Assert.Single(chatClient.Requests);
        Assert.Equal("turn the lights on", lastRequest.Last(message => message.Role == ChatRole.User).Text);
    }

    // The instructions the orchestrator resolved for the profile are what make the assistant answer in
    // character, so they have to reach the chat leg as a system message.
    [Fact]
    public async Task Session_PutsTheSessionInstructionsAtTheHeadOfTheConversation()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out var chatClient, reply: "Done.", instructions: "You are terse.");

        transcription.Emit(Completed("hello"));
        transcription.Complete();

        await ReadAllAsync(session);

        var request = Assert.Single(chatClient.Requests);
        Assert.Equal(ChatRole.System, request[0].Role);
        Assert.Equal("You are terse.", request[0].Text);
    }

    [Fact]
    public async Task Session_KeepsTheReplyInHistoryForTheNextTurn()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out var chatClient, reply: "The lights are on.");

        transcription.Emit(Completed("turn the lights on"));
        transcription.Emit(Completed("and the kettle"));
        transcription.Complete();

        await ReadAllAsync(session);

        Assert.Equal(2, chatClient.Requests.Count);

        var secondRequest = chatClient.Requests[1];
        Assert.Contains(secondRequest, message => message.Role == ChatRole.Assistant && message.Text == "The lights are on.");
    }

    // Speaking over the assistant has to stop the reply, or the user is talked over for as long as the
    // model keeps writing. The fake reply is held open indefinitely, so the only thing that can end this
    // turn is the interruption cancelling it; the timeout turns a regression into a failure, not a hang.
    [Fact(Timeout = 30000)]
    public async Task Session_StopsSpeakingWhenTheUserTalksOverTheReply()
    {
        var transcription = new FakeTranscriptionSession();
        var chatClient = new FakeChatClient(["Part one. ", "Part two. ", "Part three. "]);
        var speechClient = new FakeSpeechClient(_audioChunk);

        await using var session = new CascadedRealtimeSession(
            transcription,
            chatClient,
            speechClient,
            new RealtimeSessionOptions(),
            chatOptions: null,
            speechOptions: new TextToSpeechOptions(),
            NullLogger.Instance);

        session.Start();

        transcription.Emit(Completed("tell me a story"));

        // Wait until the reply is actually being spoken before interrupting it.
        await chatClient.Streaming.Task.WaitAsync(TestContext.Current.CancellationToken);

        transcription.Emit(new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionDelta)
        {
            Transcription = "actually",
        });

        transcription.Complete();

        var messages = await ReadAllAsync(session);

        // The caller is told to drop audio it has already buffered...
        var flush = messages.FirstOrDefault(message => message.Type == RealtimeServerMessageType.RawContentOnly);
        Assert.NotNull(flush);

        var element = Assert.IsType<JsonElement>(flush!.RawRepresentation);
        Assert.Equal("input_audio_buffer.speech_started", element.GetProperty("type").GetString());

        // ...and the reply is closed as cancelled rather than completed.
        var done = messages
            .OfType<ResponseCreatedRealtimeServerMessage>()
            .Last(message => message.Type == RealtimeServerMessageType.ResponseDone);

        Assert.Equal(RealtimeResponseStatus.Cancelled, done.Status);
    }

    [Fact]
    public async Task SendAsync_ForwardsToTheTranscriptionLeg()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out _, reply: "Done.");

        var message = new InputAudioBufferCommitRealtimeClientMessage();
        await session.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Same(message, Assert.Single(transcription.Sent));
    }

    [Theory]
    // A complete sentence past the minimum length is spoken on its own.
    [InlineData("The lights are now on in the kitchen. And ", "The lights are now on in the kitchen.", " And ")]
    // A decimal point is not the end of a sentence.
    [InlineData("The reading was 3.5 degrees below the target. ", "The reading was 3.5 degrees below the target.", " ")]
    // Too short to speak well on its own, so it waits for more text.
    [InlineData("Sure. ", null, "Sure. ")]
    public void TryTakeSpeakableFragment_SplitsOnSentencesWorthSpeaking(string pending, string? expected, string remaining)
    {
        var builder = new StringBuilder(pending);

        var taken = CascadedRealtimeSession.TryTakeSpeakableFragment(builder, out var fragment);

        Assert.Equal(expected is not null, taken);

        if (expected is not null)
        {
            Assert.Equal(expected, fragment);
        }

        Assert.Equal(remaining, builder.ToString());
    }

    private static CascadedRealtimeSession CreateSession(
        FakeTranscriptionSession transcription,
        out FakeChatClient chatClient,
        string reply,
        string? instructions = null)
    {
        chatClient = new FakeChatClient([reply]);

        var session = new CascadedRealtimeSession(
            transcription,
            chatClient,
            new FakeSpeechClient(_audioChunk),
            new RealtimeSessionOptions { Instructions = instructions },
            chatOptions: null,
            speechOptions: new TextToSpeechOptions(),
            NullLogger.Instance);

        session.Start();

        return session;
    }

    private static InputAudioTranscriptionRealtimeServerMessage Completed(string text)
    {
        return new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
        {
            Transcription = text,
        };
    }

    private static async Task<List<RealtimeServerMessage>> ReadAllAsync(CascadedRealtimeSession session)
    {
        var messages = new List<RealtimeServerMessage>();

        await foreach (var message in session.GetStreamingResponseAsync(TestContext.Current.CancellationToken))
        {
            messages.Add(message);
        }

        return messages;
    }

    private sealed class FakeTranscriptionSession : IRealtimeClientSession
    {
        private readonly Channel<RealtimeServerMessage> _messages = Channel.CreateUnbounded<RealtimeServerMessage>();

        public RealtimeSessionOptions? Options => null;

        public List<RealtimeClientMessage> Sent { get; } = [];

        public void Emit(RealtimeServerMessage message)
        {
            _messages.Writer.TryWrite(message);
        }

        public void Complete()
        {
            _messages.Writer.TryComplete();
        }

        public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);

            return Task.CompletedTask;
        }

        public IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(CancellationToken cancellationToken = default)
        {
            return _messages.Reader.ReadAllAsync(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public ValueTask DisposeAsync()
        {
            Complete();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string[] _updates;
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeChatClient(string[] updates)
        {
            _updates = updates;
        }

        /// <summary>
        /// Completes once the client has produced its first update, so a test can interrupt a reply that
        /// is genuinely in flight instead of racing the turn.
        /// </summary>
        public TaskCompletionSource Streaming { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);

            for (var index = 0; index < _updates.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return new ChatResponseUpdate(ChatRole.Assistant, _updates[index]);

                if (index == 0 && _updates.Length > 1)
                {
                    // Hold the reply open after the first update so an interruption lands mid-reply. Only
                    // cancelling the turn releases it, which is exactly what the interruption should do.
                    Streaming.TrySetResult();

                    await _held.Task.WaitAsync(cancellationToken);
                }
            }

            Streaming.TrySetResult();
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeSpeechClient : ITextToSpeechClient
    {
        private readonly byte[] _audio;

        public FakeSpeechClient(byte[] audio)
        {
            _audio = audio;
        }

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new TextToSpeechResponseUpdate([new DataContent(_audio, "audio/pcm")]);

            await Task.CompletedTask;
        }

        public Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
