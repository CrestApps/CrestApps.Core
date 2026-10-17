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

    // A speech model that answers with a container instead of raw samples would be played as noise, with
    // nothing anywhere reporting a problem, so the format is checked rather than trusted.
    [Fact]
    public async Task Session_RefusesAudioThatIsNotRawPcm()
    {
        var transcription = new FakeTranscriptionSession();

        await using var session = new CascadedRealtimeSession(
            transcription,
            new FakeChatClient(["All good."]),
            new FakeSpeechClient(_audioChunk, mediaType: "audio/mp3"),
            new RealtimeSessionOptions(),
            chatOptions: null,
            speechOptions: new TextToSpeechOptions(),
            NullLogger.Instance);

        session.Start();

        transcription.Emit(Completed("hello"));
        transcription.Complete();

        var messages = await ReadAllAsync(session);

        var error = Assert.Single(messages.OfType<ErrorRealtimeServerMessage>());
        Assert.Contains("audio/mp3", error.Error?.Message);

        Assert.DoesNotContain(messages, message => message.Type == RealtimeServerMessageType.OutputAudioDelta);
    }

    [Theory]
    [InlineData("audio/L16", true)]
    [InlineData("audio/l16", true)]
    [InlineData("audio/pcm", true)]
    [InlineData("audio/mp3", false)]
    [InlineData("audio/wav", false)]
    [InlineData(null, false)]
    public void IsRawPcm_AcceptsOnlyHeaderlessSamples(string? mediaType, bool expected)
    {
        Assert.Equal(expected, CascadedRealtimeSession.IsRawPcm(mediaType));
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

    // A phone call opens with the assistant's greeting before anyone has said anything, and a silence is broken the
    // same way. Neither has a transcript to start from, so the caller asks for the reply, and the cascade gives it
    // instead of handing the request to the transcription leg, which could only drop it.
    [Fact]
    public async Task SendAsync_ResponseRequest_IsSpokenWithoutATranscript()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out var chatClient, reply: "Hello, this is the front desk.", instructions: "You answer the phone.");

        await session.SendAsync(new CreateResponseRealtimeClientMessage(), TestContext.Current.CancellationToken);
        transcription.Complete();

        var messages = await ReadAllAsync(session);

        Assert.Contains(messages, message => message.Type == RealtimeServerMessageType.OutputAudioDelta);

        var done = messages
            .OfType<ResponseCreatedRealtimeServerMessage>()
            .Single(message => message.Type == RealtimeServerMessageType.ResponseDone);

        Assert.Equal(RealtimeResponseStatus.Completed, done.Status);

        var request = Assert.Single(chatClient.Requests);
        Assert.DoesNotContain(request, message => message.Role == ChatRole.User);
        Assert.Empty(transcription.Sent);
    }

    // A prompt after a silence follows the assistant's own last line. Models that do not continue an assistant's
    // message refuse a conversation ending with one (seen on a call with claude-haiku-5-5: "This model does not support
    // assistant message prefill"), so the conversation for such a reply ends with a short user turn.
    [Fact]
    public async Task SendAsync_ResponseRequestAfterTheAssistantsLine_EndsTheConversationWithTheUser()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out var chatClient, reply: "We open at nine.", instructions: "You answer the phone.");

        transcription.Emit(Completed("when do you open?"));
        await chatClient.Streaming.Task.WaitAsync(TestContext.Current.CancellationToken);
        await session.SendAsync(new CreateResponseRealtimeClientMessage { Instructions = "Ask whether they are still there." }, TestContext.Current.CancellationToken);
        transcription.Complete();

        await ReadAllAsync(session);

        Assert.Equal(2, chatClient.Requests.Count);
        Assert.Equal(ChatRole.User, chatClient.Requests[0][^1].Role);

        var prompt = chatClient.Requests[1];
        Assert.Equal(ChatRole.Assistant, prompt[^2].Role);
        Assert.Equal(ChatRole.User, prompt[^1].Role);
        Assert.Equal(CascadedRealtimeSession.NothingNewFromTheUser, prompt[^1].Text);
    }

    // Instructions given with one response replace the session's for that response, as a speech-to-speech
    // provider treats them; the next reply is written under the session's own again.
    [Fact]
    public async Task SendAsync_ResponseInstructions_ApplyToThatReplyOnly()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out var chatClient, reply: "Are you still there?", instructions: "You are terse.");

        await session.SendAsync(new CreateResponseRealtimeClientMessage { Instructions = "Ask whether they are still there." }, TestContext.Current.CancellationToken);
        transcription.Emit(Completed("yes, sorry"));
        transcription.Complete();

        await ReadAllAsync(session);

        Assert.Equal(2, chatClient.Requests.Count);
        Assert.Equal("Ask whether they are still there.", Assert.Single(chatClient.Requests[0], message => message.Role == ChatRole.System).Text);

        // The prompt was said, so the next reply knows it was, under the session's instructions.
        Assert.Equal("You are terse.", Assert.Single(chatClient.Requests[1], message => message.Role == ChatRole.System).Text);
        Assert.Contains(chatClient.Requests[1], message => message.Role == ChatRole.Assistant && message.Text == "Are you still there?");
    }

    // A filler ("let me look that up") is spoken out of band: the model must not later see itself having said it,
    // and it must not call tools or run long.
    [Fact]
    public async Task SendAsync_OutOfBandResponse_IsSpokenButNotKeptInHistory()
    {
        var transcription = new FakeTranscriptionSession();
        await using var session = CreateSession(transcription, out var chatClient, reply: "One moment.");

        await session.SendAsync(
            new CreateResponseRealtimeClientMessage
            {
                Instructions = "Say you are checking.",
                ExcludeFromConversation = true,
                ToolMode = ChatToolMode.None,
                MaxOutputTokens = 20,
            },
            TestContext.Current.CancellationToken);

        transcription.Emit(Completed("thanks"));
        transcription.Complete();

        await ReadAllAsync(session);

        Assert.Equal(2, chatClient.Requests.Count);
        Assert.DoesNotContain(chatClient.Requests[1], message => message.Role == ChatRole.Assistant);

        Assert.Same(ChatToolMode.None, chatClient.Options[0]?.ToolMode);
        Assert.Equal(20, chatClient.Options[0]?.MaxOutputTokens);

        // The next reply is back on the session's own options.
        Assert.Null(chatClient.Options[1]);
    }

    // A reply the caller asked for is interrupted like any other: the caller talking over the greeting stops it.
    [Fact(Timeout = 30000)]
    public async Task SendAsync_ResponseRequest_StopsWhenTheUserTalksOverIt()
    {
        var transcription = new FakeTranscriptionSession();
        var chatClient = new FakeChatClient(["Hello, this is the front desk. ", "How can I help you today? "]);

        await using var session = new CascadedRealtimeSession(
            transcription,
            chatClient,
            new FakeSpeechClient(_audioChunk),
            new RealtimeSessionOptions(),
            chatOptions: null,
            speechOptions: new TextToSpeechOptions(),
            NullLogger.Instance);

        session.Start();

        await session.SendAsync(new CreateResponseRealtimeClientMessage(), TestContext.Current.CancellationToken);
        await chatClient.Streaming.Task.WaitAsync(TestContext.Current.CancellationToken);

        transcription.Emit(new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionDelta)
        {
            Transcription = "hello?",
        });

        transcription.Complete();

        var messages = await ReadAllAsync(session);

        var done = messages
            .OfType<ResponseCreatedRealtimeServerMessage>()
            .Last(message => message.Type == RealtimeServerMessageType.ResponseDone);

        Assert.Equal(RealtimeResponseStatus.Cancelled, done.Status);
    }

    // A session that grounds each turn stops replying on its own: the host adds the knowledge and then asks. The
    // cascade used to answer every transcript regardless, so the host's request produced a second reply, and the
    // knowledge never reached the chat leg because the item was handed to the transcription leg.
    [Fact(Timeout = 30000)]
    public async Task GroundedSession_AnswersOnceWhenAskedAndWritesTheReplyFromTheKnowledge()
    {
        var transcription = new FakeTranscriptionSession();
        var chatClient = new FakeChatClient(["The office opens at nine."]);

        await using var session = CreateGroundedSession(transcription, chatClient, instructions: "You answer the phone.");

        transcription.Emit(Completed("when do you open"));

        // The host reacts to the transcript it has been shown, which is what orders the knowledge after the question.
        await using var enumerator = session.GetStreamingResponseAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        List<RealtimeServerMessage> messages = [];

        do
        {
            Assert.True(await enumerator.MoveNextAsync());
            messages.Add(enumerator.Current);
        }
        while (enumerator.Current.Type != RealtimeServerMessageType.InputAudioTranscriptionCompleted);

        // Nothing is said until the host asks.
        Assert.Empty(chatClient.Requests);

        var knowledge = new RealtimeConversationItem([new TextContent("Opening hours: 9am to 5pm.")], id: null, ChatRole.System);

        await session.SendAsync(new CreateConversationItemRealtimeClientMessage(knowledge), TestContext.Current.CancellationToken);
        await session.SendAsync(new CreateResponseRealtimeClientMessage(), TestContext.Current.CancellationToken);

        transcription.Complete();

        while (await enumerator.MoveNextAsync())
        {
            messages.Add(enumerator.Current);
        }

        // Exactly one reply...
        Assert.Single(messages.OfType<ResponseCreatedRealtimeServerMessage>(), message => message.Type == RealtimeServerMessageType.ResponseDone);

        // ...written from the question and the knowledge, under the session's instructions, and none of it went
        // to the transcription leg.
        var request = Assert.Single(chatClient.Requests);
        Assert.Equal(["You answer the phone.", "when do you open", "Opening hours: 9am to 5pm."], request.Select(message => message.Text));
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.System], request.Select(message => message.Role));
        Assert.Empty(transcription.Sent);
    }

    // Asking for a reply while another is speaking only queues it. Waiting for the speaking reply would hold up the
    // transcription pump, which is what has to notice the user talking over it.
    [Fact(Timeout = 30000)]
    public async Task SendAsync_ResponseRequest_DoesNotWaitForTheReplyInProgress()
    {
        var transcription = new FakeTranscriptionSession();
        var chatClient = new FakeChatClient(["Part one. ", "Part two. ", "Part three. "]);

        await using var session = new CascadedRealtimeSession(
            transcription,
            chatClient,
            new FakeSpeechClient(_audioChunk),
            new RealtimeSessionOptions(),
            chatOptions: null,
            speechOptions: new TextToSpeechOptions(),
            NullLogger.Instance);

        session.Start();

        transcription.Emit(Completed("tell me a story"));
        await chatClient.Streaming.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Returns while the story is still being told.
        await session.SendAsync(new CreateResponseRealtimeClientMessage(), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // The pump is free, so it still hears the user talk over it.
        transcription.Emit(new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionDelta)
        {
            Transcription = "actually",
        });

        transcription.Complete();

        var messages = await ReadAllAsync(session);

        Assert.Contains(messages, message => message.Type == RealtimeServerMessageType.RawContentOnly);

        // The story is stopped, and so is the reply that was waiting behind it: it was meant for a moment that has passed.
        var done = messages
            .OfType<ResponseCreatedRealtimeServerMessage>()
            .Where(message => message.Type == RealtimeServerMessageType.ResponseDone)
            .ToList();

        Assert.Equal(2, done.Count);
        Assert.All(done, message => Assert.Equal(RealtimeResponseStatus.Cancelled, message.Status));
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

    private static CascadedRealtimeSession CreateGroundedSession(FakeTranscriptionSession transcription, FakeChatClient chatClient, string? instructions = null)
    {
        var session = new CascadedRealtimeSession(
            transcription,
            chatClient,
            new FakeSpeechClient(_audioChunk),
            new RealtimeSessionOptions
            {
                Instructions = instructions,

                // How the orchestrator configures a session whose turns are grounded.
                RawRepresentationFactory = () => new RealtimeTurnDetectionOverrides { CreateResponse = false },
            },
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

        public List<ChatOptions?> Options { get; } = [];

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);
            Options.Add(options);

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
        private readonly string _mediaType;

        public FakeSpeechClient(byte[] audio, string mediaType = "audio/pcm")
        {
            _audio = audio;
            _mediaType = mediaType;
        }

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new TextToSpeechResponseUpdate([new DataContent(_audio, _mediaType)]);

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
