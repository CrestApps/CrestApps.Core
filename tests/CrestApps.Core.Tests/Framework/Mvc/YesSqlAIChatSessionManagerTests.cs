using System.Linq.Expressions;
using System.Security.Claims;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using CrestApps.Core.Data.YesSql.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlAIChatSessionManagerTests
{
    [Fact]
    public async Task NewAsync_WithInitialPrompt_ShouldCreateAssistantPromptThatParticipatesInHistory()
    {
        var promptStore = new Mock<IAIChatSessionPromptStore>();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(accessor => accessor.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
            ], "Test")),
        });

        var profile = new AIProfile
        {
            ItemId = "profile-1",
            Type = AIProfileType.Chat,
            PromptSubject = "Welcome",
        };
        profile.Put(new AIProfileMetadata
        {
            InitialPrompt = "Hello there",
        });
        profile.AlterSettings<ResponseHandlerProfileSettings>(settings =>
        {
            settings.InitialResponseHandlerName = "handoff";
        });

        var manager = new YesSqlAIChatSessionManager(
            httpContextAccessor.Object,
            new Mock<YesSql.ISession>().Object,
            promptStore.Object,
            [],
            TimeProvider.System,
            Options.Create(new YesSqlStoreOptions()));

        var session = await manager.NewAsync(profile, new NewAIChatSessionContext(), TestContext.Current.CancellationToken);

        Assert.Equal("user-1", session.UserId);
        Assert.Equal("handoff", session.ResponseHandlerName);
        promptStore.Verify(store => store.CreateAsync(It.Is<AIChatSessionPrompt>(prompt =>
            prompt.SessionId == session.SessionId &&
            prompt.Role == ChatRole.Assistant &&
            prompt.Title == "Welcome" &&
            prompt.Content == "Hello there" &&
            !prompt.IsGeneratedPrompt &&
            prompt.CreatedUtc != default), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NewAsync_WhenIdentityHasNoNameIdentifier_UsesIdentityName()
    {
        var promptStore = new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "friendly-name"),
            ], "Test")),
        };
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(accessor => accessor.HttpContext).Returns(httpContext);

        var manager = new YesSqlAIChatSessionManager(
            httpContextAccessor.Object,
            new Mock<YesSql.ISession>(MockBehavior.Strict).Object,
            promptStore.Object,
            [],
            TimeProvider.System,
            Options.Create(new YesSqlStoreOptions()));

        var session = await manager.NewAsync(new AIProfile
        {
            ItemId = "profile-1",
            Type = AIProfileType.Utility,
        }, new NewAIChatSessionContext(), TestContext.Current.CancellationToken);

        Assert.Equal("friendly-name", session.UserId);
        Assert.Null(session.ResponseHandlerName);
        promptStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NewAsync_WhenProfileIsNotChat_DoesNotCreatePromptOrAssignInitialHandler()
    {
        var promptStore = new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict);

        var profile = new AIProfile
        {
            ItemId = "profile-1",
            Type = AIProfileType.Utility,
            PromptSubject = "Ignored",
        };
        profile.Put(new AIProfileMetadata
        {
            InitialPrompt = "This should not be added",
        });
        profile.AlterSettings<ResponseHandlerProfileSettings>(settings =>
        {
            settings.InitialResponseHandlerName = "handoff";
        });

        var manager = new YesSqlAIChatSessionManager(
            Mock.Of<IHttpContextAccessor>(accessor => accessor.HttpContext == null),
            new Mock<YesSql.ISession>(MockBehavior.Strict).Object,
            promptStore.Object,
            [],
            TimeProvider.System,
            Options.Create(new YesSqlStoreOptions()));

        var session = await manager.NewAsync(profile, new NewAIChatSessionContext(), TestContext.Current.CancellationToken);

        Assert.Equal("profile-1", session.ProfileId);
        Assert.Null(session.ResponseHandlerName);
        promptStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NewAsync_WhenInitialPromptIsBlank_DoesNotCreateAssistantPrompt()
    {
        var promptStore = new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict);

        var profile = new AIProfile
        {
            ItemId = "profile-1",
            Type = AIProfileType.Chat,
        };
        profile.Put(new AIProfileMetadata
        {
            InitialPrompt = "   ",
        });
        profile.AlterSettings<ResponseHandlerProfileSettings>(settings =>
        {
            settings.InitialResponseHandlerName = "handoff";
        });

        var manager = new YesSqlAIChatSessionManager(
            Mock.Of<IHttpContextAccessor>(accessor => accessor.HttpContext == null),
            new Mock<YesSql.ISession>(MockBehavior.Strict).Object,
            promptStore.Object,
            [],
            TimeProvider.System,
            Options.Create(new YesSqlStoreOptions()));

        var session = await manager.NewAsync(profile, new NewAIChatSessionContext(), TestContext.Current.CancellationToken);

        Assert.Equal("handoff", session.ResponseHandlerName);
        promptStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SaveAsync_UpdatesLastActivityAndPersistsSession()
    {
        var now = new DateTime(2026, 04, 08, 15, 00, 00, DateTimeKind.Utc);
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(provider => provider.GetUtcNow()).Returns(new DateTimeOffset(now));

        var sessionStore = new Mock<YesSql.ISession>();
        var query = new Mock<YesSql.IQuery>();
        var typedQuery = new Mock<YesSql.IQuery<AIChatSession>>();
        var indexedQuery = new Mock<YesSql.IQuery<AIChatSession, AIChatSessionIndex>>();
        sessionStore.Setup(session => session.Query("AI")).Returns(query.Object);
        query.Setup(sessionQuery => sessionQuery.For<AIChatSession>(It.IsAny<bool>())).Returns(typedQuery.Object);
        typedQuery.Setup(sessionQuery => sessionQuery.With<AIChatSessionIndex>()).Returns(indexedQuery.Object);
        typedQuery
            .Setup(sessionQuery => sessionQuery.With<AIChatSessionIndex>(It.IsAny<Expression<Func<AIChatSessionIndex, bool>>>()))
            .Returns(indexedQuery.Object);
        indexedQuery
            .Setup(sessionQuery => sessionQuery.Where(It.IsAny<Expression<Func<AIChatSessionIndex, bool>>>()))
            .Returns(indexedQuery.Object);
        indexedQuery
            .Setup(sessionQuery => sessionQuery.FirstOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AIChatSession)null!);
        sessionStore
            .Setup(session => session.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<bool>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            sessionStore.Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            [],
            timeProvider.Object,
            Options.Create(new YesSqlStoreOptions()));

        var chatSession = new AIChatSession
        {
            SessionId = "session-1",
        };

        await sessionManager.SaveAsync(chatSession, TestContext.Current.CancellationToken);

        Assert.Equal(now, chatSession.LastActivityUtc);
        sessionStore.Verify(session => session.SaveAsync(chatSession, false, "AI"), Times.Once);
        sessionStore.Verify(session => session.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies detached updates preserve the persisted shape while isolating mutable containers.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithStoredSession_CopiesMutableContainersBeforeStagingUpdate()
    {
        var now = new DateTime(2026, 04, 08, 15, 00, 00, DateTimeKind.Utc);
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(provider => provider.GetUtcNow()).Returns(new DateTimeOffset(now));
        var document = new ChatDocumentInfo
        {
            DocumentId = "document-1",
            FileName = "example.txt",
            ContentType = "text/plain",
            FileSize = 42,
        };
        var extractedField = new ExtractedFieldState
        {
            Values = ["value-1"],
            LastExtractedUtc = now.AddMinutes(-1),
        };
        var postSessionResult = new PostSessionResult
        {
            Name = "summary",
            Value = "Completed",
            Status = PostSessionTaskResultStatus.Succeeded,
            Attempts = 1,
            ProcessedAtUtc = now.AddMinutes(-1),
        };
        var chatSession = new AIChatSession
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
            Title = "Updated session",
            UserId = "user-1",
            ClientId = "client-1",
            Documents = [document],
            CreatedUtc = now.AddHours(-1),
            ModifiedUtc = now.AddMinutes(-2),
            ClosedAtUtc = now.AddMinutes(-1),
            Status = ChatSessionStatus.Closed,
            ResponseHandlerName = "handler-1",
            ExtractedData = new Dictionary<string, ExtractedFieldState>(StringComparer.OrdinalIgnoreCase)
            {
                ["CustomerName"] = extractedField,
            },
            PostSessionResults = new Dictionary<string, PostSessionResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["Summary"] = postSessionResult,
            },
            PostSessionProcessingStatus = PostSessionProcessingStatus.Completed,
            PostSessionProcessingAttempts = 2,
            PostSessionProcessingLastAttemptUtc = now.AddMinutes(-1),
            IsPostSessionTasksProcessed = true,
            IsAnalyticsRecorded = true,
            IsConversionGoalsEvaluated = true,
            Properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tenant"] = "tenant-1",
            },
        };
        var storedSession = new AIChatSession
        {
            SessionId = chatSession.SessionId,
            Documents =
            [
                new ChatDocumentInfo
                {
                    DocumentId = "old-document",
                },
            ],
            ExtractedData =
            {
                ["OldField"] = new ExtractedFieldState(),
            },
            PostSessionResults =
            {
                ["OldTask"] = new PostSessionResult(),
            },
            Properties =
            {
                ["OldProperty"] = true,
            },
        };
        var sessionStore = CreateSessionStore(storedSession);
        AIChatSession stagedSession = null;
        sessionStore
            .Setup(session => session.SaveAsync(
                It.IsAny<AIChatSession>(),
                It.IsAny<bool>(),
                It.IsAny<string>()))
            .Callback<object, bool, string>((session, _, _) => stagedSession = (AIChatSession)session)
            .Returns(Task.CompletedTask);
        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            sessionStore.Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            [],
            timeProvider.Object,
            Options.Create(new YesSqlStoreOptions()));

        await sessionManager.SaveAsync(chatSession, TestContext.Current.CancellationToken);

        Assert.Same(storedSession, stagedSession);
        Assert.Equal(now, storedSession.LastActivityUtc);
        Assert.Equal(chatSession.Title, storedSession.Title);
        Assert.Equal(chatSession.PostSessionProcessingStatus, storedSession.PostSessionProcessingStatus);
        Assert.NotSame(chatSession.Documents, storedSession.Documents);
        Assert.NotSame(chatSession.ExtractedData, storedSession.ExtractedData);
        Assert.NotSame(chatSession.PostSessionResults, storedSession.PostSessionResults);
        Assert.NotSame(chatSession.Properties, storedSession.Properties);
        Assert.Same(document, Assert.Single(storedSession.Documents));
        Assert.Same(extractedField, storedSession.ExtractedData["CustomerName"]);
        Assert.Same(postSessionResult, storedSession.PostSessionResults["Summary"]);
        Assert.Same(EqualityComparer<string>.Default, storedSession.ExtractedData.Comparer);
        Assert.Same(EqualityComparer<string>.Default, storedSession.PostSessionResults.Comparer);
        var storedProperties = Assert.IsType<Dictionary<string, object>>(storedSession.Properties);
        Assert.Same(EqualityComparer<string>.Default, storedProperties.Comparer);

        chatSession.Documents.Clear();
        chatSession.ExtractedData.Clear();
        chatSession.PostSessionResults.Clear();
        chatSession.Properties.Clear();

        Assert.Single(storedSession.Documents);
        Assert.Single(storedSession.ExtractedData);
        Assert.Single(storedSession.PostSessionResults);
        Assert.Single(storedSession.Properties);
    }

    [Fact]
    public async Task SaveAsync_WhenSessionIsNull_ThrowsArgumentNullException()
    {
        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            new Mock<YesSql.ISession>(MockBehavior.Strict).Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            [],
            TimeProvider.System,
            Options.Create(new YesSqlStoreOptions()));

        await Assert.ThrowsAsync<ArgumentNullException>(() => sessionManager.SaveAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindByIdAsync_WhenIdIsEmpty_ThrowsArgumentException()
    {
        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            new Mock<YesSql.ISession>(MockBehavior.Strict).Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            [],
            TimeProvider.System,
            Options.Create(new YesSqlStoreOptions()));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sessionManager.FindByIdAsync(string.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_WhenSessionIdIsEmpty_ThrowsArgumentException()
    {
        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            new Mock<YesSql.ISession>(MockBehavior.Strict).Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            [],
            TimeProvider.System,
            Options.Create(new YesSqlStoreOptions()));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sessionManager.DeleteAsync(string.Empty, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Creates a mocked YesSql session whose chat-session query returns the supplied stored document.
    /// </summary>
    /// <param name="storedSession">The stored chat-session document.</param>
    /// <returns>The configured YesSql session mock.</returns>
    private static Mock<YesSql.ISession> CreateSessionStore(AIChatSession storedSession)
    {
        var sessionStore = new Mock<YesSql.ISession>();
        var query = new Mock<YesSql.IQuery>();
        var typedQuery = new Mock<YesSql.IQuery<AIChatSession>>();
        var indexedQuery = new Mock<YesSql.IQuery<AIChatSession, AIChatSessionIndex>>();
        sessionStore.Setup(session => session.Query("AI")).Returns(query.Object);
        query.Setup(sessionQuery => sessionQuery.For<AIChatSession>(It.IsAny<bool>())).Returns(typedQuery.Object);
        typedQuery.Setup(sessionQuery => sessionQuery.With<AIChatSessionIndex>()).Returns(indexedQuery.Object);
        typedQuery
            .Setup(sessionQuery => sessionQuery.With<AIChatSessionIndex>(
                It.IsAny<Expression<Func<AIChatSessionIndex, bool>>>()))
            .Returns(indexedQuery.Object);
        indexedQuery
            .Setup(sessionQuery => sessionQuery.Where(
                It.IsAny<Expression<Func<AIChatSessionIndex, bool>>>()))
            .Returns(indexedQuery.Object);
        indexedQuery
            .Setup(sessionQuery => sessionQuery.FirstOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedSession);

        return sessionStore;
    }
}
