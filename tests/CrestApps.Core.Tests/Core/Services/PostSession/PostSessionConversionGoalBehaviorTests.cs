using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Parsing;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services.PostSession;

/// <summary>
/// Verifies the exact conversion-goal evaluation and matching contract.
/// </summary>
public sealed class PostSessionConversionGoalBehaviorTests
{
    private const string TestProviderName = "TestProvider";
    private const string TestConnectionName = "TestConnection";
    private const string TestDeploymentName = "gpt-4o";

    /// <summary>
    /// Verifies that null and empty configured goal collections short-circuit before dependency use.
    /// </summary>
    /// <param name="useNullGoals">Whether to pass a null goal collection.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvaluateConversionGoalsAsync_WhenConfiguredGoalsAreNullOrEmpty_ReturnsNullWithoutDependencies(
        bool useNullGoals)
    {
        var harness = CreateHarness();
        var goals = useNullGoals ? null : new List<ConversionGoal>();

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        Assert.Null(results);
        Assert.Empty(harness.TemplateCalls);
        Assert.Empty(harness.ChatCancellationTokens);
        harness.ClientFactory.Verify(
            factory => factory.CreateChatClientAsync(
                It.IsAny<AIDeployment>(),
                It.IsAny<Action<ChatClientBuilder>>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a transcript without a user prompt short-circuits before dependency use.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenNoUserPromptExists_ReturnsNullWithoutDependencies()
    {
        var harness = CreateHarness();

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreateAssistantOnlyPrompts(),
            CreateGoals("goal"),
            TestContext.Current.CancellationToken);

        Assert.Null(results);
        Assert.Empty(harness.TemplateCalls);
        Assert.Empty(harness.ChatCancellationTokens);
    }

    /// <summary>
    /// Verifies that explicit null and empty returned goal collections map to a null result.
    /// </summary>
    /// <param name="responseJson">The structured response JSON.</param>
    [Theory]
    [InlineData("""{"goals":null}""")]
    [InlineData("""{"goals":[]}""")]
    public async Task EvaluateConversionGoalsAsync_WhenReturnedGoalsAreNullOrEmpty_ReturnsNull(
        string responseJson)
    {
        var harness = CreateHarness(responseJson);

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            CreateGoals("goal"),
            TestContext.Current.CancellationToken);

        Assert.Null(results);
    }

    /// <summary>
    /// Verifies that an earlier match short-circuits before a later null configured element.
    /// </summary>
    /// <param name="nullIndex">The index at which the null configured element is inserted.</param>
    /// <param name="returnedName">The matching response name before the null element.</param>
    [Theory]
    [InlineData(1, "first")]
    [InlineData(2, "second")]
    public async Task EvaluateConversionGoalsAsync_WhenMatchPrecedesNullConfiguredElement_ReturnsMatch(
        int nullIndex,
        string returnedName)
    {
        var goals = CreateGoals("first", "second");
        var harness = CreateHarness(
            CreateResponseJson(new ReturnedGoal(returnedName, 5, "Matched before null.")),
            onChatEvaluation: () => goals.Insert(nullIndex, null));

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal(returnedName, result.Name);
        Assert.Equal(5, result.Score);
    }

    /// <summary>
    /// Verifies that a match after a null configured element retains the legacy null-reference failure.
    /// </summary>
    /// <param name="nullIndex">The index at which the null configured element is inserted.</param>
    /// <param name="returnedName">The matching response name after the null element.</param>
    [Theory]
    [InlineData(0, "first")]
    [InlineData(1, "second")]
    public async Task EvaluateConversionGoalsAsync_WhenMatchFollowsNullConfiguredElement_ThrowsNullReferenceException(
        int nullIndex,
        string returnedName)
    {
        var goals = CreateGoals("first", "second");
        var harness = CreateHarness(
            CreateResponseJson(new ReturnedGoal(returnedName, 5, "Match follows null.")),
            onChatEvaluation: () => goals.Insert(nullIndex, null));

        await Assert.ThrowsAsync<NullReferenceException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                goals,
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that an unmatched response reaches a null configured element at any list position.
    /// </summary>
    /// <param name="nullIndex">The index at which the null configured element is inserted.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EvaluateConversionGoalsAsync_WhenUnmatchedResponseReachesNullConfiguredElement_ThrowsNullReferenceException(
        int nullIndex)
    {
        var goals = CreateGoals("first", "second");
        var harness = CreateHarness(
            CreateResponseJson(new ReturnedGoal("unknown", 5, "No match.")),
            onChatEvaluation: () => goals.Insert(nullIndex, null));

        await Assert.ThrowsAsync<NullReferenceException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                goals,
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that a null response name matches a null-name goal before a later null element.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenNullNameMatchPrecedesNullConfiguredElement_ReturnsMatch()
    {
        var goals = CreateGoals(null, "after");
        var harness = CreateHarness(
            CreateResponseJson(new ReturnedGoal(null, 5, "Null name matched.")),
            onChatEvaluation: () => goals.Insert(1, null));

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Null(result.Name);
        Assert.Equal(5, result.Score);
    }

    /// <summary>
    /// Verifies that a null response name reaching a null element before a null-name goal throws.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenNullNameMatchFollowsNullConfiguredElement_ThrowsNullReferenceException()
    {
        var goals = CreateGoals("before", null);
        var harness = CreateHarness(
            CreateResponseJson(new ReturnedGoal(null, 5, "Null name follows null.")),
            onChatEvaluation: () => goals.Insert(1, null));

        await Assert.ThrowsAsync<NullReferenceException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                goals,
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that no returned goals bypass configured-goal scanning after a null element is introduced.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenNoGoalsAreReturned_DoesNotScanNullConfiguredElement()
    {
        var goals = CreateGoals("first", "second");
        var harness = CreateHarness(
            """{"goals":[]}""",
            onChatEvaluation: () => goals.Insert(1, null));

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        Assert.Null(results);
    }

    /// <summary>
    /// Verifies ordinal case-insensitive matching while retaining the configured name casing.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WithCaseVariantName_MatchesOrdinalIgnoreCase()
    {
        var harness = CreateHarness(CreateResponseJson(
            new ReturnedGoal("qualifiedlead", 7, "The lead supplied contact details.")));
        var goals =
            new List<ConversionGoal>
            {
                new()
                {
                    Name = "QualifiedLead",
                    Description = "The user becomes a qualified lead.",
                    MinScore = 0,
                    MaxScore = 10,
                },
            };

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal("QualifiedLead", result.Name);
        Assert.Equal(7, result.Score);
        Assert.Equal(10, result.MaxScore);
        Assert.Equal("The lead supplied contact details.", result.Reasoning);
    }

    /// <summary>
    /// Verifies that duplicate configured names use the first ordinal-ignore-case match.
    /// </summary>
    /// <param name="firstName">The first configured name.</param>
    /// <param name="returnedName">The returned result name.</param>
    [Theory]
    [InlineData("Goal", "goal")]
    [InlineData(null, null)]
    public async Task EvaluateConversionGoalsAsync_WithDuplicateConfiguredNames_UsesFirstConfiguredGoal(
        string firstName,
        string returnedName)
    {
        var harness = CreateHarness(CreateResponseJson(
            new ReturnedGoal(returnedName, 100, "Matched duplicate goals.")));
        var goals =
            new List<ConversionGoal>
            {
                new()
                {
                    Name = firstName,
                    Description = "First configured goal.",
                    MinScore = 2,
                    MaxScore = 4,
                },
                new()
                {
                    Name = firstName?.ToUpperInvariant(),
                    Description = "Second configured goal.",
                    MinScore = 10,
                    MaxScore = 20,
                },
            };

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal(firstName, result.Name);
        Assert.Equal(4, result.Score);
        Assert.Equal(4, result.MaxScore);
    }

    /// <summary>
    /// Verifies that duplicate returned goals are retained and never overwrite earlier results.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WithDuplicateReturnedGoals_PreservesEveryResultInResponseOrder()
    {
        var harness = CreateHarness(CreateResponseJson(
            new ReturnedGoal("SECOND", 2, "First second result."),
            new ReturnedGoal("first", 3, "First result."),
            new ReturnedGoal("second", 4, "Second second result.")));
        var goals =
            new List<ConversionGoal>
            {
                new()
                {
                    Name = "first",
                    MaxScore = 10,
                },
                new()
                {
                    Name = "second",
                    MaxScore = 10,
                },
            };

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        Assert.Collection(
            results,
            result =>
            {
                Assert.Equal("second", result.Name);
                Assert.Equal(2, result.Score);
                Assert.Equal("First second result.", result.Reasoning);
            },
            result =>
            {
                Assert.Equal("first", result.Name);
                Assert.Equal(3, result.Score);
                Assert.Equal("First result.", result.Reasoning);
            },
            result =>
            {
                Assert.Equal("second", result.Name);
                Assert.Equal(4, result.Score);
                Assert.Equal("Second second result.", result.Reasoning);
            });
    }

    /// <summary>
    /// Verifies that configured goals are projected in configuration order while mapped results use response order.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_UsesConfiguredPromptOrderAndResponseResultOrder()
    {
        var harness = CreateHarness(CreateResponseJson(
            new ReturnedGoal("third", 3, "Third."),
            new ReturnedGoal("first", 1, "First.")));
        var goals =
            new List<ConversionGoal>
            {
                new()
                {
                    Name = "first",
                    Description = "First configured.",
                    MinScore = 0,
                    MaxScore = 10,
                },
                new()
                {
                    Name = "second",
                    Description = "Second configured.",
                    MinScore = 1,
                    MaxScore = 11,
                },
                new()
                {
                    Name = "third",
                    Description = "Third configured.",
                    MinScore = 2,
                    MaxScore = 12,
                },
            };

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        var promptCall = Assert.Single(
            harness.TemplateCalls,
            call => call.TemplateId == AITemplateIds.ConversionGoalEvaluationPrompt);
        var projectedGoals = Assert.IsType<List<Dictionary<string, object>>>(promptCall.Arguments["goals"]);
        Assert.Equal(["first", "second", "third"], projectedGoals.Select(goal => goal["Name"]));
        Assert.Equal(["third", "first"], results.Select(result => result.Name));
    }

    /// <summary>
    /// Verifies exact null, empty, whitespace, and non-trimming name behavior.
    /// </summary>
    /// <param name="configuredName">The configured goal name.</param>
    /// <param name="returnedName">The returned result name.</param>
    /// <param name="shouldMatch">Whether the names should match.</param>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", "", true)]
    [InlineData("   ", "   ", true)]
    [InlineData(" Goal ", " goal ", true)]
    [InlineData(" Goal ", "goal", false)]
    [InlineData(null, "", false)]
    [InlineData("", null, false)]
    public async Task EvaluateConversionGoalsAsync_WithNullOrWhitespaceNames_UsesExactOrdinalIgnoreCaseEquality(
        string configuredName,
        string returnedName,
        bool shouldMatch)
    {
        var harness = CreateHarness(CreateResponseJson(
            new ReturnedGoal(returnedName, 5, "Name behavior.")));
        var goals =
            new List<ConversionGoal>
            {
                new()
                {
                    Name = configuredName,
                    MinScore = 0,
                    MaxScore = 10,
                },
            };

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        if (shouldMatch)
        {
            var result = Assert.Single(results);
            Assert.Equal(configuredName, result.Name);
        }
        else
        {
            Assert.Empty(results);
        }
    }

    /// <summary>
    /// Verifies that unmatched results are skipped and an all-unmatched response returns an empty list.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WithUnmatchedResults_SkipsThemAndReturnsEmptyList()
    {
        var harness = CreateHarness(CreateResponseJson(
            new ReturnedGoal("unknown-one", 1, "Unknown."),
            new ReturnedGoal("unknown-two", 2, "Unknown.")));

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            CreateGoals("configured"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    /// <summary>
    /// Verifies configured score bounds, maximum score, configured name, and returned reasoning mapping.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_MapsScoreBoundsMaximumAndReasoningExactly()
    {
        var harness = CreateHarness(CreateResponseJson(
            new ReturnedGoal("goal", -100, "Below."),
            new ReturnedGoal("goal", 5, null),
            new ReturnedGoal("goal", 100, "Above.")));
        var goals =
            new List<ConversionGoal>
            {
                new()
                {
                    Name = "Goal",
                    MinScore = 2,
                    MaxScore = 8,
                },
            };

        var results = await harness.Service.EvaluateConversionGoalsAsync(
            CreateProfile(),
            CreatePrompts(),
            goals,
            TestContext.Current.CancellationToken);

        Assert.Collection(
            results,
            result =>
            {
                Assert.Equal("Goal", result.Name);
                Assert.Equal(2, result.Score);
                Assert.Equal(8, result.MaxScore);
                Assert.Equal("Below.", result.Reasoning);
            },
            result =>
            {
                Assert.Equal("Goal", result.Name);
                Assert.Equal(5, result.Score);
                Assert.Equal(8, result.MaxScore);
                Assert.Null(result.Reasoning);
            },
            result =>
            {
                Assert.Equal("Goal", result.Name);
                Assert.Equal(8, result.Score);
                Assert.Equal(8, result.MaxScore);
                Assert.Equal("Above.", result.Reasoning);
            });
    }

    /// <summary>
    /// Verifies that a null returned goal element retains the existing null-reference failure.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenReturnedGoalElementIsNull_ThrowsNullReferenceException()
    {
        var harness = CreateHarness("""{"goals":[null]}""");

        await Assert.ThrowsAsync<NullReferenceException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                CreateGoals("goal"),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that an invalid configured score range retains the existing clamp exception.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenConfiguredScoreRangeIsInvalid_ThrowsArgumentException()
    {
        var harness = CreateHarness(CreateResponseJson(
            new ReturnedGoal("goal", 5, "Invalid range.")));
        var goals =
            new List<ConversionGoal>
            {
                new()
                {
                    Name = "goal",
                    MinScore = 10,
                    MaxScore = 1,
                },
            };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                goals,
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that cancellation from prompt rendering propagates with the original token.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenPromptRenderingIsCanceled_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var expected = new OperationCanceledException(source.Token);
        var harness = CreateHarness(templateException: expected);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                CreateGoals("goal"),
                source.Token));

        Assert.Same(expected, exception);
        Assert.Contains(
            harness.TemplateCalls,
            call => call.TemplateId == AITemplateIds.ConversionGoalEvaluationPrompt
                && call.CancellationToken == source.Token);
        Assert.Empty(harness.ChatCancellationTokens);
    }

    /// <summary>
    /// Verifies that cancellation from the chat client propagates with the original token.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenChatEvaluationIsCanceled_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var expected = new OperationCanceledException(source.Token);
        var harness = CreateHarness(chatException: expected);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                CreateGoals("goal"),
                source.Token));

        Assert.Same(expected, exception);
        Assert.Equal([source.Token], harness.ChatCancellationTokens);
    }

    /// <summary>
    /// Verifies that prompt rendering errors propagate unchanged.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenPromptRenderingFails_PropagatesOriginalError()
    {
        var expected = new NotSupportedException("Template failure.");
        var harness = CreateHarness(templateException: expected);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                CreateGoals("goal"),
                TestContext.Current.CancellationToken));

        Assert.Same(expected, exception);
        Assert.Empty(harness.ChatCancellationTokens);
    }

    /// <summary>
    /// Verifies that chat client errors propagate unchanged.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenChatEvaluationFails_PropagatesOriginalError()
    {
        var expected = new NotSupportedException("Chat failure.");
        var harness = CreateHarness(chatException: expected);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                CreateGoals("goal"),
                TestContext.Current.CancellationToken));

        Assert.Same(expected, exception);
    }

    /// <summary>
    /// Verifies the existing missing-client exception.
    /// </summary>
    [Fact]
    public async Task EvaluateConversionGoalsAsync_WhenChatClientCannotBeCreated_ThrowsInvalidOperationException()
    {
        var harness = CreateHarness(configureChatClient: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.EvaluateConversionGoalsAsync(
                CreateProfile(),
                CreatePrompts(),
                CreateGoals("goal"),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "Unable to create a chat client for conversion goal evaluation on profile 'test-profile-id'.",
            exception.Message);
        Assert.Empty(harness.TemplateCalls);
    }

    /// <summary>
    /// Creates a configured AI profile.
    /// </summary>
    /// <returns>The configured profile.</returns>
    private static AIProfile CreateProfile()
    {
        return new AIProfile
        {
            ItemId = "test-profile-id",
            Type = AIProfileType.Chat,
            ChatDeploymentName = TestDeploymentName,
            UtilityDeploymentName = TestDeploymentName,
        };
    }

    /// <summary>
    /// Creates a transcript containing a user and assistant prompt.
    /// </summary>
    /// <returns>The prompts.</returns>
    private static List<AIChatSessionPrompt> CreatePrompts()
    {
        return
        [
            new AIChatSessionPrompt
            {
                Role = ChatRole.User,
                Content = "I need help choosing a plan.",
            },
            new AIChatSessionPrompt
            {
                Role = ChatRole.Assistant,
                Content = "I can compare the available plans.",
            },
        ];
    }

    /// <summary>
    /// Creates a transcript without a user prompt.
    /// </summary>
    /// <returns>The prompts.</returns>
    private static List<AIChatSessionPrompt> CreateAssistantOnlyPrompts()
    {
        return
        [
            new AIChatSessionPrompt
            {
                Role = ChatRole.Assistant,
                Content = "How can I help?",
            },
        ];
    }

    /// <summary>
    /// Creates configured goals with default score bounds.
    /// </summary>
    /// <param name="names">The goal names.</param>
    /// <returns>The goals.</returns>
    private static List<ConversionGoal> CreateGoals(params string[] names)
    {
        return names
            .Select(name => new ConversionGoal
            {
                Name = name,
                Description = $"Evaluate {name}.",
                MinScore = 0,
                MaxScore = 10,
            })
            .ToList();
    }

    /// <summary>
    /// Serializes returned goal records into the structured response shape.
    /// </summary>
    /// <param name="goals">The returned goals.</param>
    /// <returns>The response JSON.</returns>
    private static string CreateResponseJson(params ReturnedGoal[] goals)
    {
        return JsonSerializer.Serialize(new
        {
            goals,
        });
    }

    /// <summary>
    /// Creates the service and records dependency calls for assertions.
    /// </summary>
    /// <param name="responseJson">The chat response JSON.</param>
    /// <param name="userPrompt">The rendered user prompt.</param>
    /// <param name="templateException">The optional user-prompt rendering exception.</param>
    /// <param name="chatException">The optional chat evaluation exception.</param>
    /// <param name="configureChatClient">Whether a chat client can be resolved.</param>
    /// <param name="onChatEvaluation">The optional action to run when chat evaluation starts.</param>
    /// <returns>The test harness.</returns>
    private static ConversionGoalTestHarness CreateHarness(
        string responseJson = """{"goals":[]}""",
        string userPrompt = "Rendered conversion goal prompt.",
        Exception templateException = null,
        Exception chatException = null,
        bool configureChatClient = true,
        Action onChatEvaluation = null)
    {
        var templateCalls = new List<TemplateCall>();
        var chatCancellationTokens = new List<CancellationToken>();
        var templateService = new Mock<ITemplateService>();
        var templateSetup = templateService.Setup(service => service.RenderAsync(
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<CancellationToken>()));
        templateSetup.Callback<string, IDictionary<string, object>, CancellationToken>(
            (templateId, arguments, cancellationToken) =>
                templateCalls.Add(new TemplateCall(templateId, arguments, cancellationToken)));
        templateSetup.Returns<string, IDictionary<string, object>, CancellationToken>(
            (templateId, _, _) =>
            {
                if (templateException is not null
                    && templateId == AITemplateIds.ConversionGoalEvaluationPrompt)
                {
                    return Task.FromException<string>(templateException);
                }

                return Task.FromResult(
                    templateId == AITemplateIds.ConversionGoalEvaluationPrompt
                        ? userPrompt
                        : "Conversion goal system prompt.");
            });

        var chatClient = new Mock<IChatClient>();
        var chatSetup = chatClient.Setup(client => client.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()));
        chatSetup.Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>(
            (_, _, cancellationToken) =>
            {
                chatCancellationTokens.Add(cancellationToken);
                onChatEvaluation?.Invoke();
            });

        if (chatException is null)
        {
            chatSetup.ReturnsAsync(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, responseJson)));
        }
        else
        {
            chatSetup.ThrowsAsync(chatException);
        }

        var clientFactory = new Mock<IAIClientFactory>();
        var deploymentManager = new Mock<IAIDeploymentManager>();

        if (configureChatClient)
        {
            deploymentManager.Setup(manager => manager.ResolveOrDefaultAsync(
                It.IsAny<AIDeploymentPurpose>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync(new AIDeployment
                {
                    ItemId = "test-deployment-id",
                    Name = TestDeploymentName,
                    ClientName = TestProviderName,
                    ConnectionName = TestConnectionName,
                    Purpose = AIDeploymentPurpose.Chat,
                });
            clientFactory.Setup(factory => factory.CreateChatClientAsync(
                It.IsAny<AIDeployment>(),
                It.IsAny<Action<ChatClientBuilder>>()))
                .ReturnsAsync(chatClient.Object);
        }

        var service = new PostSessionProcessingService(
            clientFactory.Object,
            Mock.Of<IToolRegistry>(),
            templateService.Object,
            [new DefaultMarkdownTemplateParser()],
            new DefaultAIOptions(),
            Mock.Of<IServiceProvider>(),
            TimeProvider.System,
            NullLoggerFactory.Instance,
            deploymentManager.Object);

        return new ConversionGoalTestHarness(
            service,
            clientFactory,
            templateCalls,
            chatCancellationTokens);
    }

    /// <summary>
    /// Represents a goal returned by the structured AI response.
    /// </summary>
    /// <param name="Name">The returned goal name.</param>
    /// <param name="Score">The returned score.</param>
    /// <param name="Reasoning">The returned reasoning.</param>
    private sealed record ReturnedGoal(
        string Name,
        int Score,
        string Reasoning);

    /// <summary>
    /// Records one template rendering call.
    /// </summary>
    /// <param name="TemplateId">The template identifier.</param>
    /// <param name="Arguments">The template arguments.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    private sealed record TemplateCall(
        string TemplateId,
        IDictionary<string, object> Arguments,
        CancellationToken CancellationToken);

    /// <summary>
    /// Holds the service and recorded dependency interactions.
    /// </summary>
    private sealed class ConversionGoalTestHarness
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConversionGoalTestHarness"/> class.
        /// </summary>
        /// <param name="service">The service under test.</param>
        /// <param name="clientFactory">The client factory mock.</param>
        /// <param name="templateCalls">The recorded template calls.</param>
        /// <param name="chatCancellationTokens">The recorded chat cancellation tokens.</param>
        public ConversionGoalTestHarness(
            PostSessionProcessingService service,
            Mock<IAIClientFactory> clientFactory,
            List<TemplateCall> templateCalls,
            List<CancellationToken> chatCancellationTokens)
        {
            Service = service;
            ClientFactory = clientFactory;
            TemplateCalls = templateCalls;
            ChatCancellationTokens = chatCancellationTokens;
        }

        /// <summary>
        /// Gets the service under test.
        /// </summary>
        public PostSessionProcessingService Service { get; }

        /// <summary>
        /// Gets the client factory mock.
        /// </summary>
        public Mock<IAIClientFactory> ClientFactory { get; }

        /// <summary>
        /// Gets the recorded template calls.
        /// </summary>
        public List<TemplateCall> TemplateCalls { get; }

        /// <summary>
        /// Gets the recorded chat cancellation tokens.
        /// </summary>
        public List<CancellationToken> ChatCancellationTokens { get; }
    }
}
