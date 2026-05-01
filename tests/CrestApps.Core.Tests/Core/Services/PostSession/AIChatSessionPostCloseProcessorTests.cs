using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Parsing;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services.PostSession;

public sealed class AIChatSessionPostCloseProcessorTests
{
    private const string TestProviderName = "TestProvider";
    private const string TestConnectionName = "TestConnection";
    private const string TestDeploymentName = "gpt-4o";

    [Fact]
    public void QueueIfNeeded_WithRemainingPostSessionTasks_SetsPendingStatus()
    {
        var profile = CreateTaskProfile();
        var session = new AIChatSession
        {
            SessionId = "session-1",
            PostSessionProcessingStatus = PostSessionProcessingStatus.None,
        };

        var queued = AIChatSessionPostCloseProcessor.QueueIfNeeded(profile, session);

        Assert.True(queued);
        Assert.Equal(PostSessionProcessingStatus.Pending, session.PostSessionProcessingStatus);
    }

    [Fact]
    public void QueueIfNeeded_WhenNoRemainingWork_LeavesStatusUnchanged()
    {
        var profile = CreateTaskProfile();
        var session = new AIChatSession
        {
            SessionId = "session-1",
            IsPostSessionTasksProcessed = true,
            PostSessionProcessingStatus = PostSessionProcessingStatus.Completed,
        };

        var queued = AIChatSessionPostCloseProcessor.QueueIfNeeded(profile, session);

        Assert.False(queued);
        Assert.Equal(PostSessionProcessingStatus.Completed, session.PostSessionProcessingStatus);
    }

    [Fact]
    public async Task ProcessAsync_WhenTaskReturnsNoStructuredResult_PersistsAttemptErrorHistory()
    {
        var now = new DateTime(2026, 5, 1, 21, 0, 0, DateTimeKind.Utc);
        var processor = CreateProcessor(now, renderedPrompt: string.Empty);
        var profile = CreateTaskProfile();
        var session = CreateClosedSession();

        await processor.ProcessAsync(profile, session, CreatePrompts(), TestContext.Current.CancellationToken);

        var result = session.PostSessionResults["summary"];
        Assert.Equal(1, result.Attempts);
        Assert.Equal(PostSessionTaskResultStatus.Pending, result.Status);
        Assert.Equal("Task produced no result during attempt 1.", result.ErrorMessage);
        Assert.Null(result.ProcessedAtUtc);
        Assert.Single(result.AttemptHistory);
        Assert.Equal(1, result.AttemptHistory[0].AttemptNumber);
        Assert.Equal(PostSessionTaskResultStatus.Pending, result.AttemptHistory[0].Status);
        Assert.Equal("Task produced no result during attempt 1.", result.AttemptHistory[0].ErrorMessage);
        Assert.Equal(now, result.AttemptHistory[0].RecordedAtUtc);
    }

    [Fact]
    public async Task ProcessAsync_WhenMaxAttemptsReached_SetsTerminalProcessedTimestamp()
    {
        var now = new DateTime(2026, 5, 1, 21, 5, 0, DateTimeKind.Utc);
        var processor = CreateProcessor(now, renderedPrompt: string.Empty);
        var profile = CreateTaskProfile();
        var session = CreateClosedSession();
        session.PostSessionResults["summary"] = new PostSessionResult
        {
            Name = "summary",
            Status = PostSessionTaskResultStatus.Pending,
            Attempts = AIChatSessionPostCloseProcessor.MaxPostCloseAttempts - 1,
        };

        await processor.ProcessAsync(profile, session, CreatePrompts(), TestContext.Current.CancellationToken);

        var result = session.PostSessionResults["summary"];
        Assert.Equal(AIChatSessionPostCloseProcessor.MaxPostCloseAttempts, result.Attempts);
        Assert.Equal(PostSessionTaskResultStatus.Failed, result.Status);
        Assert.Equal($"Task produced no result after {AIChatSessionPostCloseProcessor.MaxPostCloseAttempts} attempt(s).", result.ErrorMessage);
        Assert.Equal(now, result.ProcessedAtUtc);
        Assert.Single(result.AttemptHistory);
        Assert.Equal(AIChatSessionPostCloseProcessor.MaxPostCloseAttempts, result.AttemptHistory[0].AttemptNumber);
        Assert.Equal(PostSessionTaskResultStatus.Failed, result.AttemptHistory[0].Status);
    }

    [Fact]
    public async Task ProcessAsync_WhenRetrySucceeds_PreservesEarlierAttemptHistory()
    {
        var now = new DateTime(2026, 5, 1, 21, 10, 0, DateTimeKind.Utc);
        var responseJson = "{\"tasks\":[{\"name\":\"summary\",\"value\":\"Summarized the conversation.\"}]}";
        var processor = CreateProcessor(now, renderedPrompt: "Rendered prompt", responseJson: responseJson);
        var profile = CreateTaskProfile();
        var session = CreateClosedSession();
        session.PostSessionResults["summary"] = new PostSessionResult
        {
            Name = "summary",
            Status = PostSessionTaskResultStatus.Pending,
            ErrorMessage = "Task produced no result during attempt 1.",
            Attempts = 1,
            AttemptHistory =
            [
                new PostSessionTaskAttempt
                {
                    AttemptNumber = 1,
                    Status = PostSessionTaskResultStatus.Pending,
                    ErrorMessage = "Task produced no result during attempt 1.",
                    RecordedAtUtc = now.AddMinutes(-5),
                },
            ],
        };

        await processor.ProcessAsync(profile, session, CreatePrompts(), TestContext.Current.CancellationToken);

        var result = session.PostSessionResults["summary"];
        Assert.Equal(2, result.Attempts);
        Assert.Equal(PostSessionTaskResultStatus.Succeeded, result.Status);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(now, result.ProcessedAtUtc);
        Assert.Single(result.AttemptHistory);
        Assert.Equal(1, result.AttemptHistory[0].AttemptNumber);
        Assert.Equal("Task produced no result during attempt 1.", result.AttemptHistory[0].ErrorMessage);
    }

    private static AIProfile CreateTaskProfile()
    {
        var profile = new AIProfile
        {
            ItemId = "profile-1",
            Type = AIProfileType.Chat,
            ChatDeploymentName = TestDeploymentName,
            UtilityDeploymentName = TestDeploymentName,
        };
        profile.AlterSettings<AIProfilePostSessionSettings>(settings =>
        {
            settings.EnablePostSessionProcessing = true;
            settings.PostSessionTasks =
            [
                new PostSessionTask
                {
                    Name = "summary",
                    Type = PostSessionTaskType.Semantic,
                    Instructions = "Summarize the conversation.",
                },
            ];
        });

        return profile;
    }

    private static AIChatSession CreateClosedSession()
    {
        return new AIChatSession
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
            Status = ChatSessionStatus.Closed,
        };
    }

    private static List<AIChatSessionPrompt> CreatePrompts()
    {
        return
        [
            new AIChatSessionPrompt
            {
                Role = ChatRole.User,
                Content = "I need help.",
                CreatedUtc = new DateTime(2026, 5, 1, 20, 0, 0, DateTimeKind.Utc),
            },
            new AIChatSessionPrompt
            {
                Role = ChatRole.Assistant,
                Content = "Sure, I can help.",
                CreatedUtc = new DateTime(2026, 5, 1, 20, 1, 0, DateTimeKind.Utc),
            },
        ];
    }

    private static AIChatSessionPostCloseProcessor CreateProcessor(
        DateTime now,
        string renderedPrompt,
        string responseJson = null)
    {
        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(timeProvider => timeProvider.GetUtcNow()).Returns(new DateTimeOffset(now));

        var postSessionService = CreatePostSessionService(timeProviderMock.Object, renderedPrompt, responseJson);

        return new AIChatSessionPostCloseProcessor(
            postSessionService,
            [],
            [],
            timeProviderMock.Object,
            NullLogger<AIChatSessionPostCloseProcessor>.Instance);
    }

    private static PostSessionProcessingService CreatePostSessionService(
        TimeProvider timeProvider,
        string renderedPrompt,
        string responseJson)
    {
        var mockChatClient = new Mock<IChatClient>();
        if (responseJson != null)
        {
            mockChatClient.Setup(client => client
                .GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseJson)));
        }

        var mockClientFactory = new Mock<IAIClientFactory>();
        mockClientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.Is<AIDeployment>(deployment =>
                deployment.ClientName == TestProviderName
                && deployment.ConnectionName == TestConnectionName
                && deployment.ModelName == TestDeploymentName)))
            .ReturnsAsync(mockChatClient.Object);

        var mockDeploymentManager = new Mock<IAIDeploymentManager>();
        mockDeploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(It.IsAny<AIDeploymentType>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new AIDeployment
            {
                ItemId = "deployment-1",
                Name = TestDeploymentName,
                ClientName = TestProviderName,
                ConnectionName = TestConnectionName,
                Type = AIDeploymentType.Chat,
            });

        var mockTemplateService = new Mock<ITemplateService>();
        mockTemplateService.Setup(service => service
            .RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(renderedPrompt);

        return new PostSessionProcessingService(
            mockClientFactory.Object,
            Mock.Of<IAIToolsService>(),
            mockTemplateService.Object,
            [new DefaultMarkdownTemplateParser()],
            new DefaultAIOptions
            {
                MaximumIterationsPerRequest = 10,
            },
            Mock.Of<IServiceProvider>(),
            timeProvider,
            NullLoggerFactory.Instance,
            mockDeploymentManager.Object);
    }
}
