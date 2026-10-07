#pragma warning disable MEAI001
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Every client the AI client factory creates meters its own requests, whatever the provider, so usage can be
/// reported per model and per category or purpose.
/// </summary>
public sealed class AIUsageMeteringTests
{
    private const string ClientName = "AnyProvider";

    [Fact]
    public async Task ChatClient_RecordsEveryBillableTokenCount()
    {
        var inner = new Mock<IChatClient>();
        inner
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hi"))
            {
                ModelId = "chat-model-2025",
                ResponseId = "resp-1",
                Usage = new UsageDetails
                {
                    InputTokenCount = 100,
                    OutputTokenCount = 40,
                    TotalTokenCount = 140,
                    CachedInputTokenCount = 60,
                    ReasoningTokenCount = 25,
                    InputAudioTokenCount = 7,
                    OutputAudioTokenCount = 9,
                },
            });

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetChatClientAsync(It.IsAny<AIProviderConnectionEntry>(), "chat-deployment"))
            .Returns(new ValueTask<IChatClient>(inner.Object)));

        var client = await factory.CreateChatClientAsync(Deployment("chat-deployment"));
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        var record = Assert.Single(observer.Records);
        Assert.Equal(AIUsageOperationTypes.Chat, record.OperationType);
        Assert.Equal(ClientName, record.ClientName);
        Assert.Equal("chat-deployment", record.DeploymentName);
        Assert.Equal("chat-model-2025", record.ModelName);
        Assert.Equal("resp-1", record.ResponseId);
        Assert.Equal(100, record.InputTokenCount);
        Assert.Equal(40, record.OutputTokenCount);
        Assert.Equal(140, record.TotalTokenCount);
        Assert.Equal(60, record.CachedInputTokenCount);
        Assert.Equal(25, record.ReasoningTokenCount);
        Assert.Equal(7, record.InputAudioTokenCount);
        Assert.Equal(9, record.OutputAudioTokenCount);
    }

    [Fact]
    public async Task ChatClient_WithToolCalls_RecordsEachRoundTripAsItsOwnRequest()
    {
        var inner = new Mock<IChatClient>();
        inner
            .SetupSequence(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "lookup")]))
            {
                ResponseId = "round-trip-1",
                Usage = new UsageDetails { InputTokenCount = 50, OutputTokenCount = 5 },
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))
            {
                ResponseId = "round-trip-2",
                Usage = new UsageDetails { InputTokenCount = 70, OutputTokenCount = 20 },
            });

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetChatClientAsync(It.IsAny<AIProviderConnectionEntry>(), "chat-deployment"))
            .Returns(new ValueTask<IChatClient>(inner.Object)));

        // The tool-calling loop is added by the caller on top of the factory's client, as every orchestrator does.
        var client = await factory.CreateChatClientAsync(Deployment("chat-deployment"), builder => builder.UseFunctionInvocation());
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => "found", "lookup")] };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options, TestContext.Current.CancellationToken);

        Assert.Collection(
            observer.Records,
            first =>
            {
                Assert.Equal("round-trip-1", first.ResponseId);
                Assert.Equal(55, first.TotalTokenCount);
            },
            second =>
            {
                Assert.Equal("round-trip-2", second.ResponseId);
                Assert.Equal(90, second.TotalTokenCount);
            });
    }

    [Fact]
    public async Task EmbeddingGenerator_RecordsTokensWithTheScopePurpose()
    {
        var inner = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        inner
            .Setup(generator => generator.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new[] { 1F }) { ModelId = "embedding-model" }])
            {
                Usage = new UsageDetails { InputTokenCount = 12, TotalTokenCount = 12 },
            });

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetEmbeddingGeneratorAsync(It.IsAny<AIProviderConnectionEntry>(), "embedding-deployment"))
            .Returns(new ValueTask<IEmbeddingGenerator<string, Embedding<float>>>(inner.Object)));

        var generator = await factory.CreateEmbeddingGeneratorAsync(Deployment("embedding-deployment"));

        using (AIUsageScope.Begin(contextType: "Leads", purpose: AIUsagePurposes.Indexing))
        {
            await generator.GenerateAsync(["a", "b"], cancellationToken: TestContext.Current.CancellationToken);
        }

        var record = Assert.Single(observer.Records);
        Assert.Equal(AIUsageOperationTypes.Embedding, record.OperationType);
        Assert.Equal("embedding-model", record.ModelName);
        Assert.Equal(12, record.InputTokenCount);
        Assert.Equal("Leads", record.ContextType);
        Assert.Equal(AIUsagePurposes.Indexing, record.Purpose);
    }

    [Fact]
    public async Task SpeechToTextClient_RecordsAudioDurationFromWhereSpeechEnded()
    {
        var inner = new Mock<ISpeechToTextClient>();
        inner
            .Setup(client => client.GetTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeechToTextResponse("hello")
            {
                StartTime = TimeSpan.FromMilliseconds(300),
                EndTime = TimeSpan.FromMilliseconds(4_250),
            });

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetSpeechToTextClientAsync(It.IsAny<AIProviderConnectionEntry>(), "stt-deployment"))
            .Returns(new ValueTask<ISpeechToTextClient>(inner.Object)));

        var client = await factory.CreateSpeechToTextClientAsync(Deployment("stt-deployment"));
        await client.GetTextAsync(new MemoryStream([1, 2, 3]), cancellationToken: TestContext.Current.CancellationToken);

        var record = Assert.Single(observer.Records);
        Assert.Equal(AIUsageOperationTypes.SpeechToText, record.OperationType);
        Assert.Equal(4_250, record.AudioDurationMs);
    }

    [Fact]
    public async Task SpeechToTextClient_Streaming_RecordsOnceWithTheLatestEndTimeAndReportedTokens()
    {
        var inner = new Mock<ISpeechToTextClient>();
        inner
            .Setup(client => client.GetStreamingTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsync(
                new SpeechToTextResponseUpdate("hel") { EndTime = TimeSpan.FromSeconds(1) },
                new SpeechToTextResponseUpdate("hello") { EndTime = TimeSpan.FromSeconds(3) },
                new SpeechToTextResponseUpdate([new UsageContent(new UsageDetails { InputTokenCount = 30, InputAudioTokenCount = 30 })])));

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetSpeechToTextClientAsync(It.IsAny<AIProviderConnectionEntry>(), "stt-deployment"))
            .Returns(new ValueTask<ISpeechToTextClient>(inner.Object)));

        var client = await factory.CreateSpeechToTextClientAsync(Deployment("stt-deployment"));
        var options = new SpeechToTextOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [AICompletionContextKeys.UsagePurpose] = AIUsagePurposes.Transcription,
            },
        };

        await foreach (var _ in client.GetStreamingTextAsync(new MemoryStream([1]), options, TestContext.Current.CancellationToken))
        {
        }

        var record = Assert.Single(observer.Records);
        Assert.True(record.IsStreaming);
        Assert.Equal(3_000, record.AudioDurationMs);
        Assert.Equal(30, record.InputTokenCount);
        Assert.Equal(30, record.InputAudioTokenCount);
        Assert.Equal(AIUsagePurposes.Transcription, record.Purpose);
    }

    [Fact]
    public async Task TextToSpeechClient_RecordsCharactersSynthesized()
    {
        var inner = new Mock<ITextToSpeechClient>();
        inner
            .Setup(client => client.GetStreamingAudioAsync(It.IsAny<string>(), It.IsAny<TextToSpeechOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsync(
                new TextToSpeechResponseUpdate([new DataContent(new byte[] { 1, 2 }, "audio/mpeg")]),
                new TextToSpeechResponseUpdate([new DataContent(new byte[] { 3 }, "audio/mpeg")])));

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetTextToSpeechClientAsync(It.IsAny<AIProviderConnectionEntry>(), "tts-deployment"))
            .Returns(new ValueTask<ITextToSpeechClient>(inner.Object)));

        var client = await factory.CreateTextToSpeechClientAsync(Deployment("tts-deployment"));

        await foreach (var _ in client.GetStreamingAudioAsync("Hello there", cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        var record = Assert.Single(observer.Records);
        Assert.Equal(AIUsageOperationTypes.TextToSpeech, record.OperationType);
        Assert.Equal(11, record.CharacterCount);
    }

    [Fact]
    public async Task ImageGenerator_RecordsImagesProduced()
    {
        var inner = new Mock<IImageGenerator>();
        inner
            .Setup(generator => generator.GenerateAsync(It.IsAny<ImageGenerationRequest>(), It.IsAny<ImageGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageGenerationResponse([
                new DataContent(new byte[] { 1 }, "image/png"),
                new UriContent(new Uri("https://example.test/image.png"), "image/png"),
            ]));

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetImageGeneratorAsync(It.IsAny<AIProviderConnectionEntry>(), "image-deployment"))
            .Returns(new ValueTask<IImageGenerator>(inner.Object)));

        var generator = await factory.CreateImageGeneratorAsync(Deployment("image-deployment"));
        await generator.GenerateAsync(new ImageGenerationRequest("a cat"), cancellationToken: TestContext.Current.CancellationToken);

        var record = Assert.Single(observer.Records);
        Assert.Equal(AIUsageOperationTypes.Image, record.OperationType);
        Assert.Equal(2, record.ImageCount);
    }

    [Fact]
    public async Task RealtimeClient_RecordsEachCompletedResponseAndTranscription()
    {
        var session = new FakeRealtimeSession(
            new RealtimeSessionOptions
            {
                Model = "realtime-model",
                TranscriptionOptions = new TranscriptionOptions { ModelId = "transcribe-model" },
            },
            new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseCreated) { ResponseId = "r1" },
            new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
            {
                ItemId = "item-1",
                Usage = new UsageDetails { InputTokenCount = 15, OutputTokenCount = 4 },
            },
            new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
            {
                ResponseId = "r1",
                Usage = new UsageDetails { InputTokenCount = 200, OutputTokenCount = 80, InputAudioTokenCount = 150, OutputAudioTokenCount = 70 },
            },
            new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone) { ResponseId = "r2" });

        var inner = new Mock<IRealtimeClient>();
        inner
            .Setup(client => client.CreateSessionAsync(It.IsAny<RealtimeSessionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetRealtimeClientAsync(It.IsAny<AIProviderConnectionEntry>(), "realtime-deployment"))
            .Returns(new ValueTask<IRealtimeClient>(inner.Object)));

        var client = await factory.CreateRealtimeClientAsync(Deployment("realtime-deployment"));

        IRealtimeClientSession metered;

        using (AIUsageScope.Begin(purpose: AIUsagePurposes.VoiceConversation))
        {
            metered = await client.CreateSessionAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        var messages = new List<RealtimeServerMessage>();

        await foreach (var message in metered.GetStreamingResponseAsync(TestContext.Current.CancellationToken))
        {
            messages.Add(message);
        }

        Assert.Equal(4, messages.Count);
        Assert.Collection(
            observer.Records,
            transcription =>
            {
                Assert.Equal(AIUsageOperationTypes.RealtimeTranscription, transcription.OperationType);
                Assert.Equal("transcribe-model", transcription.ModelName);
                Assert.Equal(19, transcription.TotalTokenCount);
                Assert.Equal(AIUsagePurposes.VoiceConversation, transcription.Purpose);
            },
            response =>
            {
                Assert.Equal(AIUsageOperationTypes.Realtime, response.OperationType);
                Assert.Equal("realtime-model", response.ModelName);
                Assert.Equal("r1", response.ResponseId);
                Assert.Equal(150, response.InputAudioTokenCount);
                Assert.Equal(70, response.OutputAudioTokenCount);
                Assert.Equal(AIUsagePurposes.VoiceConversation, response.Purpose);
            });
    }

    [Fact]
    public async Task Labels_RequestValueWinsOverScopeWhichWinsOverDefaultAndSession()
    {
        var inner = new Mock<IChatClient>();
        inner
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hi")));

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetChatClientAsync(It.IsAny<AIProviderConnectionEntry>(), "chat-deployment"))
            .Returns(new ValueTask<IChatClient>(inner.Object)));

        var client = await factory.CreateChatClientAsync(Deployment("chat-deployment"));
        var session = new AIChatSession { SessionId = "session-1", ProfileId = "profile-1" };

        // Only the session and a default: the session names the category, the default names the purpose.
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "1")], Options(session, defaultPurpose: AIUsagePurposes.Conversation), TestContext.Current.CancellationToken);

        using (AIUsageScope.Begin(contextType: "Sms", purpose: "AutoReply"))
        {
            // A scope overrides the session's category and the default purpose.
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "2")], Options(session, defaultPurpose: AIUsagePurposes.Conversation), TestContext.Current.CancellationToken);

            // A value on the request overrides the scope.
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "3")], Options(session, purpose: AIUsagePurposes.DataExtraction), TestContext.Current.CancellationToken);
        }

        Assert.Collection(
            observer.Records,
            first =>
            {
                Assert.Equal(nameof(AIChatSession), first.ContextType);
                Assert.Equal(AIUsagePurposes.Conversation, first.Purpose);
                Assert.Equal("session-1", first.SessionId);
            },
            second =>
            {
                Assert.Equal("Sms", second.ContextType);
                Assert.Equal("AutoReply", second.Purpose);
                Assert.Equal("session-1", second.SessionId);
            },
            third =>
            {
                Assert.Equal("Sms", third.ContextType);
                Assert.Equal(AIUsagePurposes.DataExtraction, third.Purpose);
            });
    }

    [Fact]
    public async Task ChatClient_InsideAnInvocationScope_RecordsTheInteraction()
    {
        var inner = new Mock<IChatClient>();
        inner
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hi")));

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetChatClientAsync(It.IsAny<AIProviderConnectionEntry>(), "chat-deployment"))
            .Returns(new ValueTask<IChatClient>(inner.Object)));

        var client = await factory.CreateChatClientAsync(Deployment("chat-deployment"));

        using (var invocation = AIInvocationScope.Begin())
        {
            invocation.Context.ChatInteraction = new ChatInteraction { ItemId = "interaction-1", OwnerId = "user-1" };

            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);
        }

        var record = Assert.Single(observer.Records);
        Assert.Equal(nameof(ChatInteraction), record.ContextType);
        Assert.Equal("interaction-1", record.InteractionId);
    }

    [Fact]
    public async Task UsageTrackingDisabled_RecordsNothing()
    {
        var inner = new Mock<IChatClient>();
        inner
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hi")));

        var (factory, observer) = CreateFactory(
            provider => provider
                .Setup(p => p.GetChatClientAsync(It.IsAny<AIProviderConnectionEntry>(), "chat-deployment"))
                .Returns(new ValueTask<IChatClient>(inner.Object)),
            trackingEnabled: false);

        var client = await factory.CreateChatClientAsync(Deployment("chat-deployment"));
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(observer.Records);
    }

    [Fact]
    public async Task UseUsageLabels_LabelsTheClientsRequests_AndNeverSendsTheLabelsToTheProvider()
    {
        var sentOptions = new List<ChatOptions>();
        var inner = new Mock<IChatClient>();
        inner
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((_, options, _) => sentOptions.Add(options))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hi")));

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetChatClientAsync(It.IsAny<AIProviderConnectionEntry>(), "chat-deployment"))
            .Returns(new ValueTask<IChatClient>(inner.Object)));

        var client = await factory.CreateChatClientAsync(
            Deployment("chat-deployment"),
            builder => builder.UseUsageLabels(contextType: "Leads", purpose: "LeadScoring"));

        // The client's labels win over the scope, and a label set on the request wins over the client's.
        using (AIUsageScope.Begin(contextType: "Sms", purpose: "AutoReply"))
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "1")], cancellationToken: TestContext.Current.CancellationToken);
        }

        var callerOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [AICompletionContextKeys.UsagePurpose] = "Override",
            },
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "2")], callerOptions, TestContext.Current.CancellationToken);

        Assert.Collection(
            observer.Records,
            first =>
            {
                Assert.Equal("Leads", first.ContextType);
                Assert.Equal("LeadScoring", first.Purpose);
            },
            second =>
            {
                Assert.Equal("Leads", second.ContextType);
                Assert.Equal("Override", second.Purpose);
            });

        Assert.All(sentOptions, options =>
        {
            Assert.False(options.AdditionalProperties?.ContainsKey(AICompletionContextKeys.UsageContextType) ?? false);
            Assert.False(options.AdditionalProperties?.ContainsKey(AICompletionContextKeys.UsagePurpose) ?? false);
        });

        // The caller's own options are left as they were.
        Assert.Equal("Override", callerOptions.AdditionalProperties[AICompletionContextKeys.UsagePurpose]);
        Assert.False(callerOptions.AdditionalProperties.ContainsKey(AICompletionContextKeys.UsageContextType));
    }

    [Fact]
    public async Task UseUsageLabels_HoldsForStreamedResponsesReadFromAnIterator()
    {
        var inner = new Mock<IChatClient>();
        inner
            .Setup(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => ToAsync(
                new ChatResponseUpdate(ChatRole.Assistant, "a"),
                new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 5, OutputTokenCount = 2 })])));

        var (factory, observer) = CreateFactory(provider => provider
            .Setup(p => p.GetChatClientAsync(It.IsAny<AIProviderConnectionEntry>(), "chat-deployment"))
            .Returns(new ValueTask<IChatClient>(inner.Object)));

        var client = await factory.CreateChatClientAsync(Deployment("chat-deployment"), builder => builder.UseUsageLabels(purpose: "Summaries"));

        await foreach (var _ in StreamThroughIterator(client, TestContext.Current.CancellationToken))
        {
        }

        var record = Assert.Single(observer.Records);
        Assert.Equal("Summaries", record.Purpose);
        Assert.Equal(7, record.TotalTokenCount);
    }

    [Fact]
    public async Task UseUsageLabels_LabelsEveryKindOfClient()
    {
        var embeddings = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embeddings
            .Setup(generator => generator.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new[] { 1F })]));
        var speechToText = new Mock<ISpeechToTextClient>();
        speechToText
            .Setup(client => client.GetTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeechToTextResponse("hi"));
        var textToSpeech = new Mock<ITextToSpeechClient>();
        textToSpeech
            .Setup(client => client.GetAudioAsync(It.IsAny<string>(), It.IsAny<TextToSpeechOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextToSpeechResponse([new DataContent(new byte[] { 1 }, "audio/mpeg")]));
        var images = new Mock<IImageGenerator>();
        images
            .Setup(generator => generator.GenerateAsync(It.IsAny<ImageGenerationRequest>(), It.IsAny<ImageGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageGenerationResponse([new DataContent(new byte[] { 1 }, "image/png")]));
        var session = new FakeRealtimeSession(
            new RealtimeSessionOptions(),
            new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone) { Usage = new UsageDetails { InputTokenCount = 1 } });
        var realtime = new Mock<IRealtimeClient>();
        realtime
            .Setup(client => client.CreateSessionAsync(It.IsAny<RealtimeSessionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var (factory, observer) = CreateFactory(provider =>
        {
            provider.Setup(p => p.GetEmbeddingGeneratorAsync(It.IsAny<AIProviderConnectionEntry>(), It.IsAny<string>())).Returns(new ValueTask<IEmbeddingGenerator<string, Embedding<float>>>(embeddings.Object));
            provider.Setup(p => p.GetSpeechToTextClientAsync(It.IsAny<AIProviderConnectionEntry>(), It.IsAny<string>())).Returns(new ValueTask<ISpeechToTextClient>(speechToText.Object));
            provider.Setup(p => p.GetTextToSpeechClientAsync(It.IsAny<AIProviderConnectionEntry>(), It.IsAny<string>())).Returns(new ValueTask<ITextToSpeechClient>(textToSpeech.Object));
            provider.Setup(p => p.GetImageGeneratorAsync(It.IsAny<AIProviderConnectionEntry>(), It.IsAny<string>())).Returns(new ValueTask<IImageGenerator>(images.Object));
            provider.Setup(p => p.GetRealtimeClientAsync(It.IsAny<AIProviderConnectionEntry>(), It.IsAny<string>())).Returns(new ValueTask<IRealtimeClient>(realtime.Object));
        });

        var cancellationToken = TestContext.Current.CancellationToken;

        var generator = await factory.CreateEmbeddingGeneratorAsync(Deployment("embedding"), builder => builder.UseUsageLabels("Kind", "Embedding"));
        await generator.GenerateAsync(["a"], cancellationToken: cancellationToken);

        var transcriber = await factory.CreateSpeechToTextClientAsync(Deployment("stt"), builder => builder.UseUsageLabels("Kind", "SpeechToText"));
        await transcriber.GetTextAsync(new MemoryStream([1]), cancellationToken: cancellationToken);

        var speaker = await factory.CreateTextToSpeechClientAsync(Deployment("tts"), builder => builder.UseUsageLabels("Kind", "TextToSpeech"));
        await speaker.GetAudioAsync("hi", cancellationToken: cancellationToken);

        var imageGenerator = await factory.CreateImageGeneratorAsync(Deployment("image"), builder => builder.UseUsageLabels("Kind", "Image"));
        await imageGenerator.GenerateAsync(new ImageGenerationRequest("a cat"), cancellationToken: cancellationToken);

        var realtimeClient = (await factory.CreateRealtimeClientAsync(Deployment("realtime")))
            .AsBuilder()
            .UseUsageLabels("Kind", "Realtime")
            .Build();
        var metered = await realtimeClient.CreateSessionAsync(cancellationToken: cancellationToken);

        await foreach (var _ in metered.GetStreamingResponseAsync(cancellationToken))
        {
        }

        Assert.All(observer.Records, record => Assert.Equal("Kind", record.ContextType));
        Assert.Equal(
            [AIUsageOperationTypes.Embedding, AIUsageOperationTypes.SpeechToText, AIUsageOperationTypes.TextToSpeech, AIUsageOperationTypes.Image, AIUsageOperationTypes.Realtime],
            observer.Records.Select(record => record.Purpose));
    }

    [Fact]
    public void UsageScope_NestedScopesInheritUnsetLabelsAndRestoreOnDispose()
    {
        Assert.Null(AIUsageScope.Current);

        using (AIUsageScope.Begin(contextType: "Leads"))
        {
            using (AIUsageScope.Begin(purpose: "Scoring"))
            {
                Assert.Equal("Leads", AIUsageScope.Current.ContextType);
                Assert.Equal("Scoring", AIUsageScope.Current.Purpose);
            }

            Assert.Equal("Leads", AIUsageScope.Current.ContextType);
            Assert.Null(AIUsageScope.Current.Purpose);
        }

        Assert.Null(AIUsageScope.Current);
    }

    [Fact]
    public void Report_GroupsByModelAndKindAndThenByPurpose()
    {
        AICompletionUsageRecord[] records =
        [
            new() { ClientName = "Azure", ModelName = "gpt-4o", OperationType = AIUsageOperationTypes.Chat, Purpose = AIUsagePurposes.Conversation, InputTokenCount = 100, OutputTokenCount = 10, TotalTokenCount = 110, ResponseLatencyMs = 200 },
            new() { ClientName = "Azure", ModelName = "gpt-4o", OperationType = AIUsageOperationTypes.Chat, Purpose = AIUsagePurposes.Planning, InputTokenCount = 30, OutputTokenCount = 5, TotalTokenCount = 35, ResponseLatencyMs = 100 },
            new() { ClientName = "Azure", ModelName = "gpt-4o", OperationType = AIUsageOperationTypes.Chat, Purpose = AIUsagePurposes.Conversation, InputTokenCount = 50, CachedInputTokenCount = 20, OutputTokenCount = 15, TotalTokenCount = 65 },

            // Written before kinds were recorded: counted as chat.
            new() { ClientName = "Azure", DeploymentName = "gpt-4o", TotalTokenCount = 5 },
            new() { ClientName = "Azure", ModelName = "whisper", OperationType = AIUsageOperationTypes.SpeechToText, AudioDurationMs = 4_000 },
        ];

        var byModel = AIUsageReport.Build(records, AIUsageReportGrouping.Model);

        Assert.Collection(
            byModel,
            chat =>
            {
                Assert.Equal("gpt-4o", chat.ModelName);
                Assert.Equal(AIUsageOperationTypes.Chat, chat.OperationType);
                Assert.Equal(4, chat.RequestCount);
                Assert.Equal(180, chat.InputTokenCount);
                Assert.Equal(20, chat.CachedInputTokenCount);
                Assert.Equal(215, chat.TotalTokenCount);
                Assert.Equal(150, chat.AverageResponseLatencyMs);
                Assert.Null(chat.Purpose);
            },
            speech =>
            {
                Assert.Equal(AIUsageOperationTypes.SpeechToText, speech.OperationType);
                Assert.Equal(4_000, speech.AudioDurationMs);
            });

        var byPurpose = AIUsageReport.Build(records, AIUsageReportGrouping.ModelAndPurpose);

        Assert.Equal(4, byPurpose.Count);
        Assert.Equal(2, byPurpose.Single(row => row.Purpose == AIUsagePurposes.Conversation).RequestCount);
        Assert.Equal(1, byPurpose.Single(row => row.Purpose == AIUsagePurposes.Planning).RequestCount);
        Assert.Equal(2, byPurpose.Count(row => row.Purpose == AIUsageReport.Unspecified));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamThroughIterator(
        IChatClient client,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // A streaming orchestrator reads the client from its own iterator. A scope begun here would not survive the
        // yields, but labels set on the client do.
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken))
        {
            yield return update;
        }
    }

    private static ChatOptions Options(AIChatSession session, string purpose = null, string defaultPurpose = null)
    {
        var properties = new AdditionalPropertiesDictionary
        {
            [AICompletionContextKeys.Session] = session,
        };

        if (purpose is not null)
        {
            properties[AICompletionContextKeys.UsagePurpose] = purpose;
        }

        if (defaultPurpose is not null)
        {
            properties[AICompletionContextKeys.DefaultUsagePurpose] = defaultPurpose;
        }

        return new ChatOptions { AdditionalProperties = properties };
    }

    private static AIDeployment Deployment(string modelName)
    {
        return new AIDeployment
        {
            ClientName = ClientName,
            ModelName = modelName,
        };
    }

    private static (DefaultAIClientFactory Factory, CapturingUsageObserver Observer) CreateFactory(
        Action<Mock<IAIClientProvider>> setup,
        bool trackingEnabled = true)
    {
        var observer = new CapturingUsageObserver();
        var services = new ServiceCollection()
            .AddLogging()
            .Configure<GeneralAIOptions>(options => options.EnableAIUsageTracking = trackingEnabled)
            .AddSingleton<IAICompletionUsageObserver>(observer)
            .BuildServiceProvider();

        var provider = new Mock<IAIClientProvider>();
        provider.Setup(p => p.CanHandle(ClientName)).Returns(true);
        setup(provider);

        var factory = new DefaultAIClientFactory(
            [provider.Object],
            [],
            new EphemeralDataProtectionProvider(),
            services,
            Mock.Of<IAIProviderConnectionStore>());

        return (factory, observer);
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();

            yield return item;
        }
    }

    private sealed class CapturingUsageObserver : IAICompletionUsageObserver
    {
        public List<AICompletionUsageRecord> Records { get; } = [];

        public Task UsageRecordedAsync(AICompletionUsageRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);

            return Task.CompletedTask;
        }
    }

    private sealed class FakeRealtimeSession : IRealtimeClientSession
    {
        private readonly RealtimeServerMessage[] _messages;

        public FakeRealtimeSession(RealtimeSessionOptions options, params RealtimeServerMessage[] messages)
        {
            Options = options;
            _messages = messages;
        }

        public RealtimeSessionOptions Options { get; }

        public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var message in _messages)
            {
                await Task.Yield();

                yield return message;
            }
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
