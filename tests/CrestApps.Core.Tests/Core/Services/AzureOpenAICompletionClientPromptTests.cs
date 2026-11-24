using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenAI.Chat;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using AIContent = Microsoft.Extensions.AI.AIContent;
using DataContent = Microsoft.Extensions.AI.DataContent;
using SdkChatMessage = OpenAI.Chat.ChatMessage;
using TextContent = Microsoft.Extensions.AI.TextContent;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Prevents tests that create Azure OpenAI clients from running in parallel, because they share the static client
/// cache of <see cref="AzureOpenAIClientFactory"/>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AzureOpenAIClientCacheCollection
{
    /// <summary>
    /// The collection name.
    /// </summary>
    public const string Name = "AzureOpenAIClientCache";
}

/// <summary>
/// Verifies the Azure OpenAI adapter's exact raw-message conversion and history-selection contract.
/// </summary>
[Collection(AzureOpenAIClientCacheCollection.Name)]
public sealed class AzureOpenAICompletionClientPromptTests
{
    private const string ApiKey = "test-api-key";
    private const string ConnectionName = "test-connection";
    private const string DeploymentName = "test-deployment";
    private const string ModelName = "test-model";

    private static readonly Func<AIChatMessage, UserChatMessage> _createUserMessage =
        AzureOpenAIChatMessageConverter.CreateUserMessage;

    private static readonly Func<AICompletionContext, List<SdkChatMessage>, List<SdkChatMessage>> _getPrompts =
        CreateGetPromptsDelegate();

    private static readonly ConcurrentDictionary<string, AzureOpenAIClient> _clientCache =
        GetClientCache();

    /// <summary>
    /// Verifies that a positive count keeps that many converted messages (one included), and that a missing
    /// or non-positive count keeps them all.
    /// </summary>
    /// <param name="pastMessagesCount">The configured history count.</param>
    /// <param name="expectedIndexes">The expected converted-message indexes.</param>
    [Theory]
    [InlineData(null, "0,1,2,3,4")]
    [InlineData(int.MinValue, "0,1,2,3,4")]
    [InlineData(-1, "0,1,2,3,4")]
    [InlineData(0, "0,1,2,3,4")]
    [InlineData(1, "4")]
    [InlineData(2, "3,4")]
    [InlineData(3, "2,3,4")]
    [InlineData(4, "1,2,3,4")]
    [InlineData(int.MaxValue, "0,1,2,3,4")]
    public void Convert_WithPastMessagesCount_CountsConvertedMessages(
        int? pastMessagesCount,
        string expectedIndexes)
    {
        var messages = Enumerable
            .Range(0, 5)
            .Select(index => new AIChatMessage(AIChatRole.User, $"message-{index}"))
            .ToList();
        var context = new AICompletionContext
        {
            PastMessagesCount = pastMessagesCount,
        };

        var converted = AzureOpenAIChatMessageConverter.Convert(
            messages,
            pastMessagesCount);
        var prompts = _getPrompts(context, converted);
        var indexes = expectedIndexes.Split(',').Select(int.Parse).ToArray();

        Assert.Equal(indexes.Length, prompts.Count);

        for (var index = 0; index < indexes.Length; index++)
        {
            Assert.Equal(
                $"message-{indexes[index]}",
                Assert.Single(prompts[index].Content).Text);
        }
    }

    /// <summary>
    /// Verifies null, empty, and whitespace system-message handling.
    /// </summary>
    /// <param name="systemMessage">The configured system message.</param>
    /// <param name="isIncluded">Whether a system prompt should be emitted.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", true)]
    [InlineData("\t", true)]
    public void GetPrompts_WithSystemMessage_UsesNullOrEmptyFiltering(
        string systemMessage,
        bool isIncluded)
    {
        var prompts = _getPrompts(
            new AICompletionContext
            {
                SystemMessage = systemMessage,
            },
            []);

        if (!isIncluded)
        {
            Assert.Empty(prompts);

            return;
        }

        var prompt = Assert.IsType<SystemChatMessage>(Assert.Single(prompts));

        Assert.Equal(systemMessage, Assert.Single(prompt.Content).Text);
    }

    /// <summary>
    /// Verifies which roles and content shapes produce converted Azure SDK messages.
    /// </summary>
    [Fact]
    public void ConvertMessages_WithRolesAndContentShapes_PreservesEligibility()
    {
        var messages = new List<AIChatMessage>
        {
            new(AIChatRole.System, "raw-system"),
            new(AIChatRole.Tool, "raw-tool"),
            new(new AIChatRole("observer"), "raw-unknown"),
            new(AIChatRole.User, (string)null),
            new(AIChatRole.User, string.Empty),
            new(AIChatRole.User, " \t"),
            new(AIChatRole.User, (IList<AIContent>)null),
            new(AIChatRole.User, []),
            new(AIChatRole.User, [new TextContent(null)]),
            new(AIChatRole.User, [new UnsupportedContent()]),
            new(AIChatRole.User, [new DataContent(ReadOnlyMemory<byte>.Empty, "image/png")]),
            new(AIChatRole.User, [new DataContent(new byte[] { 1 }, "application/pdf")]),
            new(AIChatRole.User, "user-text"),
            new(AIChatRole.Assistant, (string)null),
            new(AIChatRole.Assistant, string.Empty),
            new(AIChatRole.Assistant, " \t"),
            new(
                AIChatRole.Assistant,
                [
                    new TextContent("assistant-"),
                    new DataContent(new byte[] { 2 }, "image/png"),
                    new UnsupportedContent(),
                    new TextContent("text"),
                ]),
        };

        var converted = AzureOpenAIChatMessageConverter.Convert(
            messages,
            int.MaxValue);

        Assert.Collection(
            converted,
            message =>
            {
                var user = Assert.IsType<UserChatMessage>(message);
                var part = Assert.Single(user.Content);

                Assert.Equal(ChatMessageContentPartKind.Image, part.Kind);
                Assert.Equal("application/pdf", part.ImageBytesMediaType);
                Assert.Equal(new byte[] { 1 }, part.ImageBytes.ToArray());
            },
            message =>
            {
                var user = Assert.IsType<UserChatMessage>(message);
                var part = Assert.Single(user.Content);

                Assert.Equal(ChatMessageContentPartKind.Text, part.Kind);
                Assert.Equal("user-text", part.Text);
            },
            message =>
            {
                var assistant = Assert.IsType<AssistantChatMessage>(message);
                var part = Assert.Single(assistant.Content);

                Assert.Equal(ChatMessageContentPartKind.Text, part.Kind);
                Assert.Equal("assistant-text", part.Text);
            });
    }

    /// <summary>
    /// Verifies empty, unsupported, and mixed user content uses the existing fallback behavior.
    /// </summary>
    [Fact]
    public void CreateUserMessage_WithEmptyUnsupportedAndMixedContent_PreservesFallback()
    {
        Assert.Null(_createUserMessage(new AIChatMessage(AIChatRole.User, [])));
        Assert.Null(_createUserMessage(new AIChatMessage(
            AIChatRole.User,
            [
                new UnsupportedContent(),
                new TextContent(" \t"),
                new DataContent(ReadOnlyMemory<byte>.Empty, "image/png"),
            ])));

        var message = _createUserMessage(new AIChatMessage(
            AIChatRole.User,
            [
                new UnsupportedContent(),
                new TextContent("kept"),
                new DataContent(ReadOnlyMemory<byte>.Empty, "image/png"),
            ]));
        var part = Assert.Single(message.Content);

        Assert.Equal(ChatMessageContentPartKind.Text, part.Kind);
        Assert.Equal("kept", part.Text);
    }

    /// <summary>
    /// Verifies multiple text and binary parts retain their relative order and values.
    /// </summary>
    [Fact]
    public void CreateUserMessage_WithMultipleTextAndImageParts_PreservesPartOrder()
    {
        var message = _createUserMessage(new AIChatMessage(
            AIChatRole.User,
            [
                new TextContent("first"),
                new UnsupportedContent(),
                new DataContent(new byte[] { 1, 2 }, "image/png"),
                new TextContent(" "),
                new TextContent("last"),
                new DataContent(new byte[] { 3, 4 }, "image/jpeg"),
            ]));

        Assert.Collection(
            message.Content,
            part =>
            {
                Assert.Equal(ChatMessageContentPartKind.Text, part.Kind);
                Assert.Equal("first", part.Text);
            },
            part =>
            {
                Assert.Equal(ChatMessageContentPartKind.Image, part.Kind);
                Assert.Equal("image/png", part.ImageBytesMediaType);
                Assert.Equal(new byte[] { 1, 2 }, part.ImageBytes.ToArray());
            },
            part =>
            {
                Assert.Equal(ChatMessageContentPartKind.Text, part.Kind);
                Assert.Equal("last", part.Text);
            },
            part =>
            {
                Assert.Equal(ChatMessageContentPartKind.Image, part.Kind);
                Assert.Equal("image/jpeg", part.ImageBytesMediaType);
                Assert.Equal(new byte[] { 3, 4 }, part.ImageBytes.ToArray());
            });
    }

    /// <summary>
    /// Verifies converted image parts own a copy independent from the source memory.
    /// </summary>
    [Fact]
    public void CreateUserMessage_WithImageContent_CopiesSourceBytes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var message = _createUserMessage(new AIChatMessage(
            AIChatRole.User,
            [new DataContent(bytes, "image/png")]));

        bytes[0] = 9;

        var part = Assert.Single(message.Content);

        Assert.Equal(new byte[] { 1, 2, 3 }, part.ImageBytes.ToArray());
    }

    /// <summary>
    /// Verifies stable order and independent SDK conversion for repeated raw-message references.
    /// </summary>
    [Fact]
    public void ConvertMessages_WithDuplicateReferences_PreservesOccurrencesAndOrder()
    {
        var duplicate = new AIChatMessage(AIChatRole.User, "duplicate");
        var converted = AzureOpenAIChatMessageConverter.Convert(
            [
                new AIChatMessage(AIChatRole.User, "first"),
                duplicate,
                duplicate,
                new AIChatMessage(AIChatRole.Assistant, "last"),
            ],
            null);

        Assert.Equal(4, converted.Count);
        Assert.Equal("first", Assert.Single(converted[0].Content).Text);
        Assert.Equal("duplicate", Assert.Single(converted[1].Content).Text);
        Assert.Equal("duplicate", Assert.Single(converted[2].Content).Text);
        Assert.Equal("last", Assert.Single(converted[3].Content).Text);
        Assert.NotSame(converted[1], converted[2]);
        Assert.NotSame(converted[1].Content[0], converted[2].Content[0]);

        var prompts = _getPrompts(
            new AICompletionContext
            {
                PastMessagesCount = 2,
            },
            converted);

        Assert.Same(converted[2], prompts[0]);
        Assert.Same(converted[3], prompts[1]);
    }

    /// <summary>
    /// Verifies list and iterator inputs are each consumed once in source order.
    /// </summary>
    /// <param name="useIterator">Whether to use a forward-only iterator.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertMessages_WithListOrIterator_EnumeratesOnce(bool useIterator)
    {
        var source = new List<AIChatMessage>
        {
            new(AIChatRole.User, "first"),
            new(AIChatRole.System, "ignored"),
            new(AIChatRole.Assistant, "last"),
        };
        var enumerationCount = 0;

        IEnumerable<AIChatMessage> Enumerate()
        {
            enumerationCount++;

            foreach (var message in source)
            {
                yield return message;
            }
        }

        var converted = AzureOpenAIChatMessageConverter.Convert(
            useIterator ? Enumerate() : source,
            2);

        Assert.Equal(useIterator ? 1 : 0, enumerationCount);
        Assert.Equal("first", Assert.Single(converted[0].Content).Text);
        Assert.Equal("last", Assert.Single(converted[1].Content).Text);
    }

    /// <summary>
    /// Verifies an enumerable that permits only one enumeration remains supported.
    /// </summary>
    [Fact]
    public void ConvertMessages_WithSingleUseEnumerable_EnumeratesExactlyOnce()
    {
        var messages = new SingleUseEnumerable(
        [
            new AIChatMessage(AIChatRole.User, "first"),
            new AIChatMessage(AIChatRole.Assistant, "last"),
        ]);

        var converted = AzureOpenAIChatMessageConverter.Convert(
            messages,
            2);

        Assert.Equal(1, messages.GetEnumeratorCount);
        Assert.Equal(2, converted.Count);
    }

    /// <summary>
    /// Verifies a null message entry preserves the current null-reference failure.
    /// </summary>
    /// <param name="pastMessagesCount">The configured history count.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConvertMessages_WithNullEntry_ThrowsAtThatEntry(int? pastMessagesCount)
    {
        Assert.Throws<NullReferenceException>(() =>
            AzureOpenAIChatMessageConverter.Convert(
                [
                    new AIChatMessage(AIChatRole.User, "first"),
                    null,
                    new AIChatMessage(AIChatRole.Assistant, "last"),
                ],
                pastMessagesCount));
    }

    /// <summary>
    /// Verifies content-enumeration failures propagate from user-message conversion unchanged.
    /// </summary>
    [Fact]
    public void CreateUserMessage_WhenContentEnumerationThrows_PropagatesExactException()
    {
        var expectedException = new InvalidOperationException("content-enumeration");
        var contents = new ThrowingEnumeratorList(
            [new TextContent("text")],
            expectedException);
        var message = new AIChatMessage(AIChatRole.User, contents);

        var exception = Assert.Throws<InvalidOperationException>(
            () => _createUserMessage(message));

        Assert.Same(expectedException, exception);
    }

    /// <summary>
    /// Verifies both completion paths throw on an early conversion failure even when a bounded tail would discard it.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_WhenDiscardedMessageConversionThrows_PropagatesBeforeRequest(bool streaming)
    {
        var expectedException = new InvalidOperationException("discarded-conversion");
        var messages = new AIChatMessage[]
        {
            new(
                AIChatRole.User,
                new ThrowingEnumeratorList(
                    [new TextContent("discarded")],
                    expectedException)),
            new(AIChatRole.User, "retained-user"),
            new(AIChatRole.Assistant, "retained-assistant"),
        };
        using var clientContext = CreateCompletionClient(streaming);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeCompletionAsync(
                clientContext.Client,
                messages,
                new AICompletionContext
                {
                    ChatDeploymentName = DeploymentName,
                    DisableTools = true,
                    PastMessagesCount = 2,
                },
                streaming,
                TestContext.Current.CancellationToken));

        Assert.Same(expectedException, exception);
        Assert.Null(clientContext.Handler.RequestBody);
    }

    /// <summary>
    /// Verifies the otherwise-unused post-conversion text read remains observable in both completion paths.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_WhenPostConversionTextReadThrows_PropagatesBeforeTailSelection(bool streaming)
    {
        var expectedException = new InvalidOperationException("post-conversion-text");
        var messages = new AIChatMessage[]
        {
            new(
                AIChatRole.User,
                new ThrowOnCountAccessList(
                    [new TextContent("discarded")],
                    2,
                    expectedException)),
            new(AIChatRole.User, "retained-user"),
            new(AIChatRole.Assistant, "retained-assistant"),
        };
        using var clientContext = CreateCompletionClient(streaming);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeCompletionAsync(
                clientContext.Client,
                messages,
                new AICompletionContext
                {
                    ChatDeploymentName = DeploymentName,
                    DisableTools = true,
                    PastMessagesCount = 2,
                },
                streaming,
                TestContext.Current.CancellationToken));

        Assert.Same(expectedException, exception);
        Assert.Null(clientContext.Handler.RequestBody);
    }

    /// <summary>
    /// Verifies the second aggregate-text read for a successful assistant remains observable.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_WhenAssistantSecondTextReadThrows_PropagatesBeforeTailSelection(bool streaming)
    {
        var expectedException = new InvalidOperationException("assistant-second-text");
        var messages = new AIChatMessage[]
        {
            new(
                AIChatRole.Assistant,
                new ThrowOnCountAccessList(
                    [new TextContent("discarded")],
                    2,
                    expectedException)),
            new(AIChatRole.User, "retained-user"),
            new(AIChatRole.Assistant, "retained-assistant"),
        };
        using var clientContext = CreateCompletionClient(streaming);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeCompletionAsync(
                clientContext.Client,
                messages,
                new AICompletionContext
                {
                    ChatDeploymentName = DeploymentName,
                    DisableTools = true,
                    PastMessagesCount = 2,
                },
                streaming,
                TestContext.Current.CancellationToken));

        Assert.Same(expectedException, exception);
        Assert.Null(clientContext.Handler.RequestBody);
    }

    /// <summary>
    /// Verifies both completion paths enumerate once, copy image bytes at encounter time, and send the same bounded prompts.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_WithMutableSingleUseHistory_PreservesPromptConstruction(bool streaming)
    {
        var bytes = new byte[] { 1, 2 };

        IEnumerable<AIChatMessage> EnumerateMessages()
        {
            yield return new AIChatMessage(AIChatRole.User, "discarded");
            yield return new AIChatMessage(
                AIChatRole.User,
                [
                    new TextContent("look"),
                    new DataContent(bytes, "image/png"),
                ]);

            bytes[0] = 9;

            yield return new AIChatMessage(AIChatRole.Assistant, "retained-assistant");
        }

        var messages = new SingleUseEnumerable(EnumerateMessages());
        using var clientContext = CreateCompletionClient(streaming);
        using var cancellationTokenSource = new CancellationTokenSource();
        var context = new AICompletionContext
        {
            ChatDeploymentName = DeploymentName,
            DisableTools = true,
            SystemMessage = "system",
            PastMessagesCount = 2,
            Temperature = 0.25f,
            TopP = 0.75f,
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.25f,
            MaxTokens = 64,
        };

        await InvokeCompletionAsync(
            clientContext.Client,
            messages,
            context,
            streaming,
            cancellationTokenSource.Token);

        Assert.Equal(1, messages.GetEnumeratorCount);
        Assert.True(clientContext.Handler.CancellationToken.CanBeCanceled);
        Assert.Contains($"/openai/deployments/{ModelName}/chat/completions", clientContext.Handler.RequestUri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("api-version=", clientContext.Handler.RequestUri.Query, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(clientContext.Handler.RequestBody);
        var root = document.RootElement;
        var requestMessages = root.GetProperty("messages");

        Assert.Equal(3, requestMessages.GetArrayLength());
        Assert.Equal("system", requestMessages[0].GetProperty("role").GetString());
        Assert.Equal("system", requestMessages[0].GetProperty("content").GetString());
        Assert.Equal("user", requestMessages[1].GetProperty("role").GetString());
        Assert.Equal("assistant", requestMessages[2].GetProperty("role").GetString());
        Assert.Equal("retained-assistant", requestMessages[2].GetProperty("content").GetString());

        var contentParts = requestMessages[1].GetProperty("content");

        Assert.Equal(2, contentParts.GetArrayLength());
        Assert.Equal("text", contentParts[0].GetProperty("type").GetString());
        Assert.Equal("look", contentParts[0].GetProperty("text").GetString());
        Assert.Equal("image_url", contentParts[1].GetProperty("type").GetString());
        Assert.Equal(
            "data:image/png;base64,AQI=",
            contentParts[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal(0.25, root.GetProperty("temperature").GetDouble(), 3);
        Assert.Equal(0.75, root.GetProperty("top_p").GetDouble(), 3);
        Assert.Equal(0.5, root.GetProperty("frequency_penalty").GetDouble(), 3);
        Assert.Equal(0.25, root.GetProperty("presence_penalty").GetDouble(), 3);
        var hasMaxTokens =
            root.TryGetProperty("max_completion_tokens", out var maxTokens) ||
            root.TryGetProperty("max_tokens", out maxTokens);

        Assert.True(hasMaxTokens, clientContext.Handler.RequestBody);
        Assert.Equal(64, maxTokens.GetInt32());
    }

    /// <summary>
    /// Verifies a request that carries function tools and a reasoning effort goes to the Responses API client
    /// instead of Chat Completions, which reasoning models reject for that combination.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_WithToolsAndReasoningEffort_UsesResponsesApi(bool streaming)
    {
        var responsesClient = new CapturingChatClient();
        var clientFactory = new Mock<IAIClientFactory>(MockBehavior.Strict);
        clientFactory
            .Setup(factory => factory.CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<Microsoft.Extensions.AI.ChatClientBuilder>>()))
            .ReturnsAsync(responsesClient);
        using var clientContext = CreateCompletionClient(streaming, [new ToolsAndReasoningHandler(Microsoft.Extensions.AI.ReasoningEffort.Medium)], clientFactory.Object);

        await InvokeCompletionAsync(
            clientContext.Client,
            [new AIChatMessage(AIChatRole.User, "What time is it?")],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
            },
            streaming,
            TestContext.Current.CancellationToken);

        Assert.Equal(Microsoft.Extensions.AI.ReasoningEffort.Medium, responsesClient.LastOptions.Reasoning.Effort);
        Assert.Single(responsesClient.LastOptions.Tools);
        Assert.Null(clientContext.Handler.RequestUri);
    }

    /// <summary>
    /// Verifies the Responses API request keeps only the text of past assistant answers, as Chat Completions does, so a
    /// stored function call without its result never reaches the Responses API.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_WithResponsesApi_SendsOnlyTheTextOfAssistantMessages(bool streaming)
    {
        var responsesClient = new CapturingChatClient();
        var clientFactory = new Mock<IAIClientFactory>(MockBehavior.Strict);
        clientFactory
            .Setup(factory => factory.CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<Microsoft.Extensions.AI.ChatClientBuilder>>()))
            .ReturnsAsync(responsesClient);
        using var clientContext = CreateCompletionClient(streaming, [new ToolsAndReasoningHandler(Microsoft.Extensions.AI.ReasoningEffort.Medium)], clientFactory.Object);
        var functionCall = new Microsoft.Extensions.AI.FunctionCallContent("call-1", "get_time");

        await InvokeCompletionAsync(
            clientContext.Client,
            [
                new AIChatMessage(AIChatRole.User, "What time is it?"),
                new AIChatMessage(AIChatRole.Assistant, [functionCall]),
                new AIChatMessage(AIChatRole.Assistant, [new TextContent("It is noon."), functionCall]),
                new AIChatMessage(AIChatRole.Tool, [new Microsoft.Extensions.AI.FunctionResultContent("call-1", "12:00")]),
                new AIChatMessage(AIChatRole.User, "And in Paris?"),
            ],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
                SystemMessage = "You are helpful.",
            },
            streaming,
            TestContext.Current.CancellationToken);

        Assert.Collection(
            responsesClient.LastMessages,
            message => Assert.Equal(AIChatRole.System, message.Role),
            message => Assert.Equal("What time is it?", message.Text),
            message =>
            {
                Assert.Equal(AIChatRole.Assistant, message.Role);
                Assert.Equal("It is noon.", message.Text);
                Assert.All(message.Contents, content => Assert.IsType<TextContent>(content));
            },
            message => Assert.Equal("And in Paris?", message.Text));
    }

    /// <summary>
    /// Verifies a Responses API request that resumes after a person answered tool approvals keeps the tool calls the
    /// paused turn made before it paused, with their results, so the model sees what the turn already did. The calls
    /// of earlier turns are still left out.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    /// <param name="pastMessagesCount">The limit on past messages, or <see langword="null"/> for none.</param>
    /// <param name="expectedMessages">How many messages the request has after the system message.</param>
    [Theory]
    [InlineData(false, null, 7)]
    [InlineData(true, null, 7)]
    [InlineData(true, 4, 4)]
    [InlineData(true, 3, 2)]
#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
    public async Task Completion_WithResponsesApi_ResumingAfterAnApproval_KeepsTheCallsOfThePausedTurn(bool streaming, int? pastMessagesCount, int expectedMessages)
    {
        var responsesClient = new CapturingChatClient();
        var clientFactory = new Mock<IAIClientFactory>(MockBehavior.Strict);
        clientFactory
            .Setup(factory => factory.CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<Microsoft.Extensions.AI.ChatClientBuilder>>()))
            .ReturnsAsync(responsesClient);
        using var clientContext = CreateCompletionClient(streaming, [new ToolsAndReasoningHandler(Microsoft.Extensions.AI.ReasoningEffort.Medium)], clientFactory.Object);
        var approval = new Microsoft.Extensions.AI.ToolApprovalRequestContent("ficc_delete-1", new Microsoft.Extensions.AI.FunctionCallContent("delete-1", "delete_item"));

        await InvokeCompletionAsync(
            clientContext.Client,
            [
                new AIChatMessage(AIChatRole.User, "Which items are there?"),
                new AIChatMessage(AIChatRole.Assistant, [new Microsoft.Extensions.AI.FunctionCallContent("earlier-1", "list_items")]),
                new AIChatMessage(AIChatRole.Tool, [new Microsoft.Extensions.AI.FunctionResultContent("earlier-1", "Items: 7, 8")]),
                new AIChatMessage(AIChatRole.Assistant, "There are items 7 and 8."),
                new AIChatMessage(AIChatRole.User, "Create item 9 and delete item 7."),
                new AIChatMessage(AIChatRole.Assistant, [new Microsoft.Extensions.AI.FunctionCallContent("create-1", "create_item")]),
                new AIChatMessage(AIChatRole.Tool, [new Microsoft.Extensions.AI.FunctionResultContent("create-1", "Created item 9.")]),
                new AIChatMessage(AIChatRole.Assistant, [new Microsoft.Extensions.AI.FunctionCallContent("unanswered-1", "list_items")]),
                new AIChatMessage(AIChatRole.Assistant, [approval]),
                new AIChatMessage(AIChatRole.User, [approval.CreateResponse(approved: true, reason: null)]),
            ],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
                SystemMessage = "You are helpful.",
                PastMessagesCount = pastMessagesCount,
            },
            streaming,
            TestContext.Current.CancellationToken);

        var messages = responsesClient.LastMessages;

        Assert.Equal(AIChatRole.System, messages[0].Role);
        Assert.Equal(expectedMessages, messages.Count - 1);

        // The request always ends with the approval request and the person's answer.
        Assert.IsType<Microsoft.Extensions.AI.ToolApprovalRequestContent>(Assert.Single(messages[^2].Contents));
        Assert.IsType<Microsoft.Extensions.AI.ToolApprovalResponseContent>(Assert.Single(messages[^1].Contents));

        var calls = messages.SelectMany(message => message.Contents).OfType<Microsoft.Extensions.AI.FunctionCallContent>().Select(call => call.CallId).ToList();
        var results = messages.SelectMany(message => message.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().Select(result => result.CallId).ToList();

        // A call of an earlier turn and a call without a result are never sent, and a result is never sent
        // without its call.
        Assert.DoesNotContain("earlier-1", calls);
        Assert.DoesNotContain("unanswered-1", calls);
        Assert.Equal(calls, results);

        if (expectedMessages >= 4)
        {
            Assert.Equal(["create-1"], calls);
        }
        else
        {
            // The limit cut the call off, so its result is dropped with it.
            Assert.Empty(calls);
        }
    }
#pragma warning restore MEAI001

    /// <summary>
    /// Verifies the Chat Completions tool-calling loop tells a tool the id of the call it answers, and ends the turn
    /// without asking the model again when the tool is an agent that paused for a person's approval. Without a pause
    /// the loop asks the model again after the tool call.
    /// </summary>
    /// <param name="pauses">Whether the called agent pauses for an approval.</param>
    /// <param name="expectedRequests">How many requests the model receives.</param>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task Streaming_WhenACalledAgentPausedForAnApproval_EndsTheTurnWithoutAskingTheModelAgain(bool pauses, int expectedRequests)
    {
        var agent = new PausingAgentProbe { Pauses = pauses };
        using var clientContext = CreateCompletionClient(
            streaming: true,
            [new SingleToolHandler(agent)],
            new Mock<IAIClientFactory>(MockBehavior.Strict).Object,
            streamingResponse: CreateStreamingToolCallResponse());
        using var scope = AIInvocationScope.Begin();

        await InvokeCompletionAsync(
            clientContext.Client,
            [new AIChatMessage(AIChatRole.User, "Delete item 7.")],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
            },
            streaming: true,
            TestContext.Current.CancellationToken);

        // The fixture allows one more round of tool calls after the first, so a turn that goes on asks twice.
        Assert.Equal(expectedRequests, clientContext.Handler.RequestCount);
        Assert.Equal(expectedRequests, agent.CallIds.Count);
        Assert.All(agent.CallIds, callId => Assert.Equal("call-agent-1", callId));
        Assert.Equal(pauses, AgentToolApprovals.IsTurnPaused(scope.Context));
    }

    /// <summary>
    /// Verifies the non-streaming loop, which a streaming request falls back to when the deployment cannot stream,
    /// ends the turn after an agent paused for an approval, as the streaming loop does, and returns the calls.
    /// </summary>
    /// <param name="pauses">Whether the called agent pauses for an approval.</param>
    /// <param name="expectedRequests">How many requests the model receives.</param>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task NonStreaming_WhenACalledAgentPausedForAnApproval_EndsTheTurnWithoutAskingTheModelAgain(bool pauses, int expectedRequests)
    {
        var agent = new PausingAgentProbe { Pauses = pauses };
        using var clientContext = CreateCompletionClient(
            streaming: false,
            [new SingleToolHandler(agent)],
            new Mock<IAIClientFactory>(MockBehavior.Strict).Object,
            completionResponse: CreateToolCallCompletionResponse());
        using var scope = AIInvocationScope.Begin();

        var response = await clientContext.Client.CompleteAsync(
            [new AIChatMessage(AIChatRole.User, "Delete item 7.")],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
            },
            TestContext.Current.CancellationToken);

        // The fixture allows one round of tool calls, after which a turn that goes on asks the model once more.
        Assert.Equal(expectedRequests, clientContext.Handler.RequestCount);
        Assert.Equal("call-agent-1", Assert.Single(agent.CallIds));

        if (pauses)
        {
            // The caller gets the call and its result, as the streaming loop gives them.
            var contents = response.Messages.SelectMany(message => message.Contents).ToList();

            Assert.Equal("call-agent-1", Assert.Single(contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>()).CallId);
            Assert.Equal("call-agent-1", Assert.Single(contents.OfType<Microsoft.Extensions.AI.FunctionResultContent>()).CallId);
        }
    }

#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
    /// <summary>
    /// Verifies the non-streaming loop never runs a tool that requires an approval: the request pauses with an
    /// approval request, as the streaming loop does, and the model is not asked again.
    /// </summary>
    [Fact]
    public async Task NonStreaming_AToolThatRequiresApproval_DoesNotRun_AndTheResponseAsksForTheApproval()
    {
        var tool = new PausingAgentProbe();
        using var clientContext = CreateCompletionClient(
            streaming: false,
            [new SingleToolHandler(new Microsoft.Extensions.AI.ApprovalRequiredAIFunction(tool))],
            new Mock<IAIClientFactory>(MockBehavior.Strict).Object,
            completionResponse: CreateToolCallCompletionResponse());
        using var scope = AIInvocationScope.Begin();

        var response = await clientContext.Client.CompleteAsync(
            [new AIChatMessage(AIChatRole.User, "Delete item 7.")],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
            },
            TestContext.Current.CancellationToken);

        var request = Assert.Single(response.Messages.SelectMany(message => message.Contents).OfType<Microsoft.Extensions.AI.ToolApprovalRequestContent>());

        Assert.Empty(tool.CallIds);
        Assert.Equal(1, clientContext.Handler.RequestCount);
        Assert.Equal("ficc_call-agent-1", request.RequestId);
        Assert.Equal("content-agent", Assert.IsType<Microsoft.Extensions.AI.FunctionCallContent>(request.ToolCall).Name);
    }

    /// <summary>
    /// Verifies a non-streaming request that resumes after a person approved a call runs that call first, then asks
    /// the model, and returns the call with its result before the answer.
    /// </summary>
    [Fact]
    public async Task NonStreaming_ResumingAfterAnApproval_RunsTheApprovedCallFirst()
    {
        var tool = new PausingAgentProbe();
        using var clientContext = CreateCompletionClient(
            streaming: false,
            [new SingleToolHandler(tool)],
            new Mock<IAIClientFactory>(MockBehavior.Strict).Object);
        using var scope = AIInvocationScope.Begin();
        var approval = new Microsoft.Extensions.AI.ToolApprovalRequestContent(
            "ficc_call-agent-1",
            new Microsoft.Extensions.AI.FunctionCallContent("call-agent-1", "content-agent", new Dictionary<string, object> { ["prompt"] = "Delete item 7" }));

        var response = await clientContext.Client.CompleteAsync(
            [
                new AIChatMessage(AIChatRole.User, "Delete item 7."),
                new AIChatMessage(AIChatRole.Assistant, [approval]),
                new AIChatMessage(AIChatRole.User, [approval.CreateResponse(approved: true, reason: null)]),
            ],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["call-agent-1"], tool.CallIds);
        Assert.Equal(1, clientContext.Handler.RequestCount);
        Assert.Equal("call-agent-1", Assert.Single(response.Messages[0].Contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>()).CallId);
        Assert.Equal("done", Assert.Single(response.Messages[1].Contents.OfType<Microsoft.Extensions.AI.FunctionResultContent>()).Result);
        Assert.Equal("done", response.Messages[^1].Text);
    }

    /// <summary>
    /// Verifies a round of tool calls in which one call requires an approval asks for an approval of every call of
    /// the round, as the function-invoking chat client does, so no call runs before a person answers; and that on
    /// resume each answer decides its own call.
    /// </summary>
    [Fact]
    public async Task NonStreaming_ARoundWithACallThatRequiresApproval_AsksForEachCall_AndEachAnswerDecidesItsOwnCall()
    {
        var agent = new PausingAgentProbe();
        var listCalls = 0;
        var list = Microsoft.Extensions.AI.AIFunctionFactory.Create(
            () =>
            {
                listCalls++;

                return "Items: 7, 8";
            },
            "list_items");

        using (var pausedContext = CreateCompletionClient(
            streaming: false,
            [new ToolsHandler(new Microsoft.Extensions.AI.ApprovalRequiredAIFunction(agent), list)],
            new Mock<IAIClientFactory>(MockBehavior.Strict).Object,
            completionResponse: CreateMixedToolCallsCompletionResponse()))
        using (AIInvocationScope.Begin())
        {
            var paused = await pausedContext.Client.CompleteAsync(
                [new AIChatMessage(AIChatRole.User, "List the items and delete item 7.")],
                new AICompletionContext
                {
                    ChatDeploymentName = DeploymentName,
                },
                TestContext.Current.CancellationToken);

            var requests = paused.Messages.SelectMany(message => message.Contents).OfType<Microsoft.Extensions.AI.ToolApprovalRequestContent>().ToList();

            Assert.Equal(["content-agent", "list_items"], requests.Select(request => Assert.IsType<Microsoft.Extensions.AI.FunctionCallContent>(request.ToolCall).Name));
            Assert.Empty(agent.CallIds);
            Assert.Equal(0, listCalls);
            Assert.Equal(1, pausedContext.Handler.RequestCount);
        }

        using var resumedContext = CreateCompletionClient(
            streaming: false,
            [new ToolsHandler(new Microsoft.Extensions.AI.ApprovalRequiredAIFunction(agent), list)],
            new Mock<IAIClientFactory>(MockBehavior.Strict).Object);
        using var scope = AIInvocationScope.Begin();
        var agentRequest = new Microsoft.Extensions.AI.ToolApprovalRequestContent(
            "ficc_call-agent-1",
            new Microsoft.Extensions.AI.FunctionCallContent("call-agent-1", "content-agent", new Dictionary<string, object> { ["prompt"] = "Delete item 7" }));
        var listRequest = new Microsoft.Extensions.AI.ToolApprovalRequestContent(
            "ficc_call-list-1",
            new Microsoft.Extensions.AI.FunctionCallContent("call-list-1", "list_items", new Dictionary<string, object>()));

        await resumedContext.Client.CompleteAsync(
            [
                new AIChatMessage(AIChatRole.User, "List the items and delete item 7."),
                new AIChatMessage(AIChatRole.Assistant, [agentRequest, listRequest]),
                new AIChatMessage(AIChatRole.User, [agentRequest.CreateResponse(approved: false, reason: null), listRequest.CreateResponse(approved: true, reason: null)]),
            ],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
            },
            TestContext.Current.CancellationToken);

        // The person allowed list_items and denied the agent: only list_items ran, once.
        Assert.Empty(agent.CallIds);
        Assert.Equal(1, listCalls);
    }
#pragma warning restore MEAI001

    /// <summary>
    /// Verifies a request that carries function tools without a reasoning effort stays on Chat Completions.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_WithToolsAndNoReasoningEffort_UsesChatCompletions(bool streaming)
    {
        using var clientContext = CreateCompletionClient(streaming, [new ToolsAndReasoningHandler(Microsoft.Extensions.AI.ReasoningEffort.None)], new Mock<IAIClientFactory>(MockBehavior.Strict).Object);

        await InvokeCompletionAsync(
            clientContext.Client,
            [new AIChatMessage(AIChatRole.User, "What time is it?")],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
            },
            streaming,
            TestContext.Current.CancellationToken);

        Assert.Contains("chat/completions", clientContext.Handler.RequestUri.AbsoluteUri, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a connection whose <c>ChatApi</c> setting is <c>ChatCompletions</c> keeps a request with function tools
    /// and a reasoning effort on Chat Completions.
    /// </summary>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_WhenChatApiIsChatCompletions_UsesChatCompletions(bool streaming)
    {
        using var clientContext = CreateCompletionClient(streaming, [new ToolsAndReasoningHandler(Microsoft.Extensions.AI.ReasoningEffort.Medium)], new Mock<IAIClientFactory>(MockBehavior.Strict).Object, OpenAIChatApis.ChatCompletions);

        await InvokeCompletionAsync(
            clientContext.Client,
            [new AIChatMessage(AIChatRole.User, "What time is it?")],
            new AICompletionContext
            {
                ChatDeploymentName = DeploymentName,
            },
            streaming,
            TestContext.Current.CancellationToken);

        Assert.Contains("chat/completions", clientContext.Handler.RequestUri.AbsoluteUri, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Azure streaming update conversion preserves metadata and content order.
    /// </summary>
    [Fact]
    public void CreateStreamingResponseUpdate_WithMetadataAndContent_PreservesContract()
    {
        var createdAt = new DateTimeOffset(2026, 7, 16, 1, 2, 3, TimeSpan.Zero);
        var update = OpenAIChatModelFactory.StreamingChatCompletionUpdate(
            completionId: "completion-1",
            contentUpdate: new ChatMessageContent(
            [
                ChatMessageContentPart.CreateTextPart("first"),
                ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes([1, 2]), "image/png"),
                ChatMessageContentPart.CreateTextPart("third"),
            ]),
            functionCallUpdate: null,
            toolCallUpdates: [],
            role: ChatMessageRole.Assistant,
            refusalUpdate: null,
            contentTokenLogProbabilities: [],
            refusalTokenLogProbabilities: [],
            finishReason: ChatFinishReason.Stop,
            createdAt: createdAt,
            model: "model-1",
            systemFingerprint: "fingerprint-1",
            usage: null);

        var result = AzureOpenAICompletionClient.CreateStreamingResponseUpdate(update);

        Assert.Equal("completion-1", result.ResponseId);
        Assert.Equal(createdAt, result.CreatedAt);
        Assert.Equal("model-1", result.ModelId);
        Assert.Equal("Stop", result.FinishReason.ToString());
        Assert.Equal("assistant", result.Role.ToString());
        Assert.Collection(
            result.Contents,
            content => Assert.Equal("first", Assert.IsType<TextContent>(content).Text),
            content => Assert.Equal(string.Empty, Assert.IsType<TextContent>(content).Text),
            content => Assert.Equal("third", Assert.IsType<TextContent>(content).Text));
    }

    /// <summary>
    /// Verifies Azure streaming update conversion leaves nullable metadata unset.
    /// </summary>
    [Fact]
    public void CreateStreamingResponseUpdate_WithoutNullableMetadata_LeavesValuesUnset()
    {
        var update = OpenAIChatModelFactory.StreamingChatCompletionUpdate(
            completionId: "completion-2",
            contentUpdate: new ChatMessageContent(),
            functionCallUpdate: null,
            toolCallUpdates: [],
            role: null,
            refusalUpdate: null,
            contentTokenLogProbabilities: [],
            refusalTokenLogProbabilities: [],
            finishReason: null,
            createdAt: default,
            model: null,
            systemFingerprint: null,
            usage: null);

        var result = AzureOpenAICompletionClient.CreateStreamingResponseUpdate(update);

        Assert.Equal("completion-2", result.ResponseId);
        Assert.Null(result.FinishReason);
        Assert.Null(result.Role);
        Assert.Empty(result.Contents);
    }

    /// <summary>
    /// Invokes a completion path and drains streaming output when requested.
    /// </summary>
    /// <param name="client">The completion client.</param>
    /// <param name="messages">The raw messages.</param>
    /// <param name="context">The completion context.</param>
    /// <param name="streaming">Whether to invoke the streaming path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task InvokeCompletionAsync(
        AzureOpenAICompletionClient client,
        IEnumerable<AIChatMessage> messages,
        AICompletionContext context,
        bool streaming,
        CancellationToken cancellationToken)
    {
        if (!streaming)
        {
            var response = await client.CompleteAsync(messages, context, cancellationToken);

            Assert.NotNull(response);

            return;
        }

        var updates = new List<Microsoft.Extensions.AI.ChatResponseUpdate>();

        await foreach (var update in client.CompleteStreamingAsync(messages, context, cancellationToken))
        {
            updates.Add(update);
        }

        Assert.NotEmpty(updates);
    }

    /// <summary>
    /// Creates a fully configured completion client backed by an in-memory HTTP handler.
    /// </summary>
    /// <param name="streaming">Whether the handler should return a streaming response.</param>
    /// <param name="completionServiceHandlers">The handlers that configure each request, or none.</param>
    /// <param name="clientFactory">The AI client factory that serves Responses API requests, or a default mock.</param>
    /// <param name="chatApi">The connection's <c>ChatApi</c> setting, or <see langword="null"/> to leave it unset.</param>
    /// <param name="streamingResponse">The server-sent events every streaming request is answered with, or <see langword="null"/> for a text answer.</param>
    /// <param name="completionResponse">The JSON every non-streaming request is answered with, or <see langword="null"/> for a text answer.</param>
    /// <returns>The disposable client context.</returns>
    private static CompletionClientContext CreateCompletionClient(
        bool streaming,
        IEnumerable<IAICompletionServiceHandler> completionServiceHandlers = null,
        IAIClientFactory clientFactory = null,
        string chatApi = null,
        string streamingResponse = null,
        string completionResponse = null)
    {
        AzureOpenAIClientFactory.ClearCache();

        var endpoint = new Uri("https://unit.test/");
        var handler = new RecordingHttpMessageHandler(
            streaming ? streamingResponse ?? CreateStreamingResponse() : completionResponse ?? CreateCompletionResponse(),
            streaming ? "text/event-stream" : "application/json");
        var httpClient = new HttpClient(handler);
        var azureOptions = new AzureClientOptions
        {
            EnableDefaultRetryPolicy = false,
        };
        var connection = new AIProviderConnection
        {
            Name = ConnectionName,
            ClientName = AzureOpenAIConstants.ClientName,
            Properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Endpoint"] = endpoint.AbsoluteUri,
                ["AuthenticationType"] = "ApiKey",
                ["ApiKey"] = ApiKey,
            },
        };

        if (chatApi is not null)
        {
            connection.Properties["ChatApi"] = chatApi;
        }

        var connectionEntry = AIProviderConnectionEntryFactory.Create(connection, []);

        _ = AzureOpenAIClientFactory.Create(
            connectionEntry,
            NullLoggerFactory.Instance,
            azureOptions);

        var cacheKey = Assert.Single(_clientCache.Keys);
        var sdkOptions = new AzureOpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        _clientCache[cacheKey] = new AzureOpenAIClient(
            endpoint,
            new ApiKeyCredential(ApiKey),
            sdkOptions);

        var deployment = new AIDeployment
        {
            Name = DeploymentName,
            ClientName = AzureOpenAIConstants.ClientName,
            ConnectionName = ConnectionName,
            ModelName = ModelName,
        };
        var deploymentManager = new Mock<IAIDeploymentManager>(MockBehavior.Strict);
        deploymentManager
            .Setup(manager => manager.ResolveSlotAsync(
                AIDeploymentSlotNames.Chat,
                It.IsAny<string>(),
                AzureOpenAIConstants.ClientName,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        var connectionStore = new Mock<IAIProviderConnectionStore>(MockBehavior.Strict);
        connectionStore
            .Setup(store => store.GetAsync(
                ConnectionName,
                AzureOpenAIConstants.ClientName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var optionsSnapshot = new Mock<IOptionsSnapshot<AzureClientOptions>>(MockBehavior.Strict);
        optionsSnapshot
            .SetupGet(options => options.Value)
            .Returns(azureOptions);

        var services = new ServiceCollection().BuildServiceProvider();
        var client = new AzureOpenAICompletionClient(
            services,
            NullLoggerFactory.Instance,
            completionServiceHandlers ?? [],
            [],
            new DefaultAIOptions
            {
                MaximumIterationsPerRequest = 1,
            },
            Mock.Of<ITemplateService>(),
            deploymentManager.Object,
            connectionStore.Object,
            Mock.Of<IDataProtectionProvider>(),
            optionsSnapshot.Object,
            clientFactory ?? Mock.Of<IAIClientFactory>());

        return new CompletionClientContext(client, handler, httpClient, services);
    }

    /// <summary>
    /// Creates the non-streaming OpenAI-compatible response payload.
    /// </summary>
    /// <returns>The JSON response payload.</returns>
    private static string CreateCompletionResponse()
    {
        return """
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "created": 1710000000,
              "model": "test-model",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "done",
                    "refusal": null
                  },
                  "finish_reason": "stop",
                  "logprobs": null
                }
              ],
              "usage": {
                "prompt_tokens": 1,
                "completion_tokens": 1,
                "total_tokens": 2
              }
            }
            """;
    }

    /// <summary>
    /// Creates the streaming OpenAI-compatible response payload.
    /// </summary>
    /// <returns>The server-sent event response payload.</returns>
    private static string CreateStreamingResponse()
    {
        return """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1710000000,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","content":"done"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1710000000,"model":"test-model","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}

            data: [DONE]

            """;
    }

    /// <summary>
    /// Creates a non-streaming response in which the model calls the <c>content-agent</c> tool.
    /// </summary>
    /// <returns>The JSON response payload.</returns>
    private static string CreateToolCallCompletionResponse()
    {
        return """
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "created": 1710000000,
              "model": "test-model",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": null,
                    "refusal": null,
                    "tool_calls": [
                      {
                        "id": "call-agent-1",
                        "type": "function",
                        "function": {
                          "name": "content-agent",
                          "arguments": "{\"prompt\":\"Delete item 7\"}"
                        }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls",
                  "logprobs": null
                }
              ],
              "usage": {
                "prompt_tokens": 1,
                "completion_tokens": 1,
                "total_tokens": 2
              }
            }
            """;
    }

    /// <summary>
    /// Creates a non-streaming response in which the model calls the <c>content-agent</c> and <c>list_items</c> tools
    /// in one round.
    /// </summary>
    /// <returns>The JSON response payload.</returns>
    private static string CreateMixedToolCallsCompletionResponse()
    {
        return """
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "created": 1710000000,
              "model": "test-model",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": null,
                    "refusal": null,
                    "tool_calls": [
                      {
                        "id": "call-agent-1",
                        "type": "function",
                        "function": {
                          "name": "content-agent",
                          "arguments": "{\"prompt\":\"Delete item 7\"}"
                        }
                      },
                      {
                        "id": "call-list-1",
                        "type": "function",
                        "function": {
                          "name": "list_items",
                          "arguments": "{}"
                        }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls",
                  "logprobs": null
                }
              ],
              "usage": {
                "prompt_tokens": 1,
                "completion_tokens": 1,
                "total_tokens": 2
              }
            }
            """;
    }

    /// <summary>
    /// Creates a streaming response in which the model calls the <c>content-agent</c> tool.
    /// </summary>
    /// <returns>The server-sent event response payload.</returns>
    private static string CreateStreamingToolCallResponse()
    {
        return """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1710000000,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","content":null,"tool_calls":[{"index":0,"id":"call-agent-1","type":"function","function":{"name":"content-agent","arguments":"{\"prompt\":\"Delete item 7\"}"}}]},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1710000000,"model":"test-model","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}

            data: [DONE]

            """;
    }

    /// <summary>
    /// Creates a strongly typed delegate for the private prompt-selection helper.
    /// </summary>
    /// <returns>The prompt-selection delegate.</returns>
    private static Func<AICompletionContext, List<SdkChatMessage>, List<SdkChatMessage>> CreateGetPromptsDelegate()
    {
        var method = typeof(AzureOpenAICompletionClient).GetMethod(
            "GetPrompts",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unable to find the prompt-selection helper.");

        return method.CreateDelegate<Func<AICompletionContext, List<SdkChatMessage>, List<SdkChatMessage>>>();
    }

    /// <summary>
    /// Gets the Azure client factory's cache for test transport substitution.
    /// </summary>
    /// <returns>The shared client cache.</returns>
    private static ConcurrentDictionary<string, AzureOpenAIClient> GetClientCache()
    {
        var field = typeof(AzureOpenAIClientFactory).GetField(
            "_clientCache",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unable to find the Azure OpenAI client cache.");

        return (ConcurrentDictionary<string, AzureOpenAIClient>)field.GetValue(null);
    }

    /// <summary>
    /// Represents an unsupported AI content subtype.
    /// </summary>
    private sealed class UnsupportedContent : AIContent
    {
    }

    /// <summary>
    /// Provides an enumerable that rejects a second enumeration.
    /// </summary>
    private sealed class SingleUseEnumerable : IEnumerable<AIChatMessage>
    {
        private readonly IEnumerable<AIChatMessage> _messages;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleUseEnumerable"/> class.
        /// </summary>
        /// <param name="messages">The messages to enumerate.</param>
        public SingleUseEnumerable(IEnumerable<AIChatMessage> messages)
        {
            _messages = messages;
        }

        /// <summary>
        /// Gets the number of generic enumerators requested.
        /// </summary>
        public int GetEnumeratorCount { get; private set; }

        /// <summary>
        /// Returns the single permitted enumerator.
        /// </summary>
        /// <returns>The message enumerator.</returns>
        public IEnumerator<AIChatMessage> GetEnumerator()
        {
            GetEnumeratorCount++;

            if (GetEnumeratorCount > 1)
            {
                throw new InvalidOperationException("The enumerable may only be enumerated once.");
            }

            return _messages.GetEnumerator();
        }

        /// <summary>
        /// Returns the single permitted non-generic enumerator.
        /// </summary>
        /// <returns>The message enumerator.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Provides a content list whose enumerator acquisition fails.
    /// </summary>
    private sealed class ThrowingEnumeratorList : DelegatingContentList
    {
        private readonly Exception _exception;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowingEnumeratorList"/> class.
        /// </summary>
        /// <param name="contents">The contents exposed by index operations.</param>
        /// <param name="exception">The exception to throw from enumeration.</param>
        public ThrowingEnumeratorList(
            IList<AIContent> contents,
            Exception exception)
            : base(contents)
        {
            _exception = exception;
        }

        /// <summary>
        /// Throws the configured exception instead of returning an enumerator.
        /// </summary>
        /// <returns>This method does not return.</returns>
        public override IEnumerator<AIContent> GetEnumerator()
        {
            throw _exception;
        }
    }

    /// <summary>
    /// Provides a content list that fails on a configured count access.
    /// </summary>
    private sealed class ThrowOnCountAccessList : DelegatingContentList
    {
        private readonly int _throwOnAccess;
        private readonly Exception _exception;
        private int _countAccesses;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThrowOnCountAccessList"/> class.
        /// </summary>
        /// <param name="contents">The contents to expose.</param>
        /// <param name="throwOnAccess">The one-based count access that should fail.</param>
        /// <param name="exception">The exception to throw.</param>
        public ThrowOnCountAccessList(
            IList<AIContent> contents,
            int throwOnAccess,
            Exception exception)
            : base(contents)
        {
            _throwOnAccess = throwOnAccess;
            _exception = exception;
        }

        /// <summary>
        /// Gets the content count until the configured access throws.
        /// </summary>
        public override int Count
        {
            get
            {
                _countAccesses++;

                if (_countAccesses == _throwOnAccess)
                {
                    throw _exception;
                }

                return base.Count;
            }
        }
    }

    /// <summary>
    /// Delegates mutable list operations to a backing content list.
    /// </summary>
    private class DelegatingContentList : IList<AIContent>
    {
        private readonly IList<AIContent> _contents;

        /// <summary>
        /// Initializes a new instance of the <see cref="DelegatingContentList"/> class.
        /// </summary>
        /// <param name="contents">The backing contents.</param>
        protected DelegatingContentList(IList<AIContent> contents)
        {
            _contents = contents;
        }

        /// <summary>
        /// Gets the number of content items.
        /// </summary>
        public virtual int Count => _contents.Count;

        /// <summary>
        /// Gets a value indicating whether the list is read-only.
        /// </summary>
        public bool IsReadOnly => _contents.IsReadOnly;

        /// <summary>
        /// Gets or sets a content item by index.
        /// </summary>
        /// <param name="index">The item index.</param>
        public AIContent this[int index]
        {
            get => _contents[index];
            set => _contents[index] = value;
        }

        /// <summary>
        /// Adds a content item.
        /// </summary>
        /// <param name="item">The content item.</param>
        public void Add(AIContent item)
        {
            _contents.Add(item);
        }

        /// <summary>
        /// Removes all content items.
        /// </summary>
        public void Clear()
        {
            _contents.Clear();
        }

        /// <summary>
        /// Determines whether the list contains an item.
        /// </summary>
        /// <param name="item">The content item.</param>
        /// <returns><see langword="true"/> when the item is present.</returns>
        public bool Contains(AIContent item)
        {
            return _contents.Contains(item);
        }

        /// <summary>
        /// Copies content items to an array.
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The starting destination index.</param>
        public void CopyTo(AIContent[] array, int arrayIndex)
        {
            _contents.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Returns the content enumerator.
        /// </summary>
        /// <returns>The content enumerator.</returns>
        public virtual IEnumerator<AIContent> GetEnumerator()
        {
            return _contents.GetEnumerator();
        }

        /// <summary>
        /// Gets the index of a content item.
        /// </summary>
        /// <param name="item">The content item.</param>
        /// <returns>The item index, or <c>-1</c>.</returns>
        public int IndexOf(AIContent item)
        {
            return _contents.IndexOf(item);
        }

        /// <summary>
        /// Inserts a content item.
        /// </summary>
        /// <param name="index">The insertion index.</param>
        /// <param name="item">The content item.</param>
        public void Insert(int index, AIContent item)
        {
            _contents.Insert(index, item);
        }

        /// <summary>
        /// Removes a content item.
        /// </summary>
        /// <param name="item">The content item.</param>
        /// <returns><see langword="true"/> when the item was removed.</returns>
        public bool Remove(AIContent item)
        {
            return _contents.Remove(item);
        }

        /// <summary>
        /// Removes a content item by index.
        /// </summary>
        /// <param name="index">The item index.</param>
        public void RemoveAt(int index)
        {
            _contents.RemoveAt(index);
        }

        /// <summary>
        /// Returns the non-generic content enumerator.
        /// </summary>
        /// <returns>The content enumerator.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Gives every request one function tool and the given reasoning effort.
    /// </summary>
    private sealed class ToolsAndReasoningHandler : IAICompletionServiceHandler
    {
        private readonly Microsoft.Extensions.AI.ReasoningEffort _effort;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolsAndReasoningHandler"/> class.
        /// </summary>
        /// <param name="effort">The reasoning effort to apply.</param>
        public ToolsAndReasoningHandler(Microsoft.Extensions.AI.ReasoningEffort effort)
        {
            _effort = effort;
        }

        /// <summary>
        /// Adds the tool and the reasoning effort to the request options.
        /// </summary>
        /// <param name="context">The configure context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task ConfigureAsync(CompletionServiceConfigureContext context, CancellationToken cancellationToken = default)
        {
            context.ChatOptions.Tools = [Microsoft.Extensions.AI.AIFunctionFactory.Create(() => "12:00", "current_time")];
            context.ChatOptions.Reasoning = new Microsoft.Extensions.AI.ReasoningOptions { Effort = _effort };

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Gives every request one tool.
    /// </summary>
    private sealed class SingleToolHandler : IAICompletionServiceHandler
    {
        private readonly Microsoft.Extensions.AI.AITool _tool;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleToolHandler"/> class.
        /// </summary>
        /// <param name="tool">The tool to offer.</param>
        public SingleToolHandler(Microsoft.Extensions.AI.AITool tool)
        {
            _tool = tool;
        }

        /// <summary>
        /// Adds the tool to the request options.
        /// </summary>
        /// <param name="context">The configure context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task ConfigureAsync(CompletionServiceConfigureContext context, CancellationToken cancellationToken = default)
        {
            context.ChatOptions.Tools = [_tool];

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Offers several tools on each request.
    /// </summary>
    private sealed class ToolsHandler : IAICompletionServiceHandler
    {
        private readonly Microsoft.Extensions.AI.AITool[] _tools;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolsHandler"/> class.
        /// </summary>
        /// <param name="tools">The tools to offer.</param>
        public ToolsHandler(params Microsoft.Extensions.AI.AITool[] tools)
        {
            _tools = tools;
        }

        /// <summary>
        /// Adds the tools to the request options.
        /// </summary>
        /// <param name="context">The configure context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task ConfigureAsync(CompletionServiceConfigureContext context, CancellationToken cancellationToken = default)
        {
            context.ChatOptions.Tools = [.. _tools];

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stands in for an agent: keeps the id of each call it answers and, when asked to, pauses for an approval the
    /// way an agent does.
    /// </summary>
    private sealed class PausingAgentProbe : Microsoft.Extensions.AI.AIFunction
    {
        /// <summary>
        /// Gets or sets a value indicating whether a call pauses for an approval.
        /// </summary>
        public bool Pauses { get; set; }

        /// <summary>
        /// Gets the id of each call, as the tool-calling loop made it known.
        /// </summary>
        public List<string> CallIds { get; } = [];

        /// <summary>
        /// Gets the name of the tool.
        /// </summary>
        public override string Name => "content-agent";

        /// <summary>
        /// Gets the description of the tool.
        /// </summary>
        public override string Description => "Lists and deletes items.";

        /// <summary>
        /// Keeps the id of the call and pauses when asked to.
        /// </summary>
        /// <param name="arguments">The arguments of the call.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the call.</returns>
        protected override ValueTask<object> InvokeCoreAsync(Microsoft.Extensions.AI.AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var callId = AIToolInvocation.GetCallId(arguments);

            CallIds.Add(callId);

            if (Pauses)
            {
                AgentToolApprovals.GetOrAdd(AIInvocationScope.Current).Pause(
                    new Microsoft.Extensions.AI.FunctionCallContent(callId, Name),
                    new ChatTurnAgentRun { CallId = callId, AgentName = Name });
            }

            return ValueTask.FromResult<object>(Pauses ? "paused" : "done");
        }
    }

    /// <summary>
    /// Captures the options of the last request and answers it with fixed text.
    /// </summary>
    private sealed class CapturingChatClient : Microsoft.Extensions.AI.IChatClient
    {
        /// <summary>
        /// Gets the options of the last request.
        /// </summary>
        public Microsoft.Extensions.AI.ChatOptions LastOptions { get; private set; }

        /// <summary>
        /// Gets the messages of the last request.
        /// </summary>
        public List<AIChatMessage> LastMessages { get; private set; }

        /// <summary>
        /// Captures the options and returns a fixed response.
        /// </summary>
        /// <param name="messages">The messages.</param>
        /// <param name="options">The options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The fixed response.</returns>
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<AIChatMessage> messages, Microsoft.Extensions.AI.ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            LastMessages = [.. messages];

            return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(new AIChatMessage(AIChatRole.Assistant, "done")));
        }

        /// <summary>
        /// Captures the options and streams a fixed update.
        /// </summary>
        /// <param name="messages">The messages.</param>
        /// <param name="options">The options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The fixed update.</returns>
        public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AIChatMessage> messages, Microsoft.Extensions.AI.ChatOptions options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            LastMessages = [.. messages];

            yield return new Microsoft.Extensions.AI.ChatResponseUpdate(AIChatRole.Assistant, "done");

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets no services.
        /// </summary>
        /// <param name="serviceType">The service type.</param>
        /// <param name="serviceKey">The service key.</param>
        /// <returns>Always <see langword="null"/>.</returns>
        public object GetService(Type serviceType, object serviceKey = null)
            => null;

        /// <summary>
        /// Releases nothing.
        /// </summary>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Captures requests and returns a fixed HTTP response.
    /// </summary>
    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly string _responseMediaType;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingHttpMessageHandler"/> class.
        /// </summary>
        /// <param name="responseBody">The response body.</param>
        /// <param name="responseMediaType">The response media type.</param>
        public RecordingHttpMessageHandler(
            string responseBody,
            string responseMediaType)
        {
            _responseBody = responseBody;
            _responseMediaType = responseMediaType;
        }

        /// <summary>
        /// Gets the captured request body.
        /// </summary>
        public string RequestBody { get; private set; }

        /// <summary>
        /// Gets the captured request URI.
        /// </summary>
        public Uri RequestUri { get; private set; }

        /// <summary>
        /// Gets the cancellation token passed to the HTTP transport.
        /// </summary>
        public CancellationToken CancellationToken { get; private set; }

        /// <summary>
        /// Gets how many requests the handler answered.
        /// </summary>
        public int RequestCount { get; private set; }

        /// <summary>
        /// Captures the request and returns the configured response.
        /// </summary>
        /// <param name="request">The HTTP request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The configured HTTP response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            RequestUri = request.RequestUri;
            CancellationToken = cancellationToken;
            RequestCount++;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responseBody,
                    Encoding.UTF8,
                    _responseMediaType),
            };
        }
    }

    /// <summary>
    /// Owns the completion client and its test transport resources.
    /// </summary>
    private sealed class CompletionClientContext : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ServiceProvider _services;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompletionClientContext"/> class.
        /// </summary>
        /// <param name="client">The completion client.</param>
        /// <param name="handler">The recording handler.</param>
        /// <param name="httpClient">The HTTP client.</param>
        /// <param name="services">The service provider.</param>
        public CompletionClientContext(
            AzureOpenAICompletionClient client,
            RecordingHttpMessageHandler handler,
            HttpClient httpClient,
            ServiceProvider services)
        {
            Client = client;
            Handler = handler;
            _httpClient = httpClient;
            _services = services;
        }

        /// <summary>
        /// Gets the completion client.
        /// </summary>
        public AzureOpenAICompletionClient Client { get; }

        /// <summary>
        /// Gets the recording HTTP handler.
        /// </summary>
        public RecordingHttpMessageHandler Handler { get; }

        /// <summary>
        /// Releases transport resources and clears the shared client cache.
        /// </summary>
        public void Dispose()
        {
            AzureOpenAIClientFactory.ClearCache();
            _httpClient.Dispose();
            _services.Dispose();
        }
    }
}
