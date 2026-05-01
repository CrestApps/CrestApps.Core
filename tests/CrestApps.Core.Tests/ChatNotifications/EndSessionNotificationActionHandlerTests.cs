using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.ChatNotifications;

public sealed class EndSessionNotificationActionHandlerTests
{
    // ───────────────────────────────────────────────────────────────
    // AIChatSession path — session found
    // ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_AIChatSession_ClosesSessionAndShowsSessionEndedNotification()
    {
        // Arrange
        var now = new DateTime(2026, 3, 14, 22, 0, 0, DateTimeKind.Utc);
        var session = new AIChatSession
        {
            SessionId = "session-1",
            Status = ChatSessionStatus.Active,
        };

        var sessionManagerMock = new Mock<IAIChatSessionManager>();
        sessionManagerMock.Setup(m => m.FindByIdAsync("session-1")).ReturnsAsync(session);
        sessionManagerMock.Setup(m => m.SaveAsync(session)).Returns(Task.CompletedTask).Verifiable();
        var profileManagerMock = new Mock<IAIProfileManager>();
        profileManagerMock.Setup(m => m.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((AIProfile)null);

        ChatNotification captured = null;
        var senderMock = new Mock<IChatNotificationSender>();
        senderMock.Setup(s => s
            .SendAsync("session-1", ChatContextType.AIChatSession, It.IsAny<ChatNotification>()))
            .Callback<string, ChatContextType, ChatNotification>((_, _, n) => captured = n)
            .Returns(Task.CompletedTask);

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(now));

        var services = BuildServiceProvider(
            sessionManager: sessionManagerMock.Object,
            profileManager: profileManagerMock.Object,
            notificationSender: senderMock.Object,
            timeProvider: timeProviderMock.Object);
        var context = CreateContext("session-1", ChatContextType.AIChatSession, services);
        var handler = new EndSessionNotificationActionHandler();

        // Act
        await handler.HandleAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(ChatSessionStatus.Closed, session.Status);
        Assert.Equal(now, session.ClosedAtUtc);
        sessionManagerMock.Verify();
        Assert.NotNull(captured);
        Assert.Equal(ChatNotificationTypes.SessionEnded, captured.Type);
        Assert.True(captured.Dismissible);
    }

    // ───────────────────────────────────────────────────────────────
    // AIChatSession path — session not found
    // ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_AIChatSession_SessionNotFound_DoesNotSendSessionEndedNotification()
    {
        // Arrange
        var profileManagerMock = new Mock<IAIProfileManager>();
        var sessionManagerMock = new Mock<IAIChatSessionManager>();
        sessionManagerMock.Setup(m => m.FindByIdAsync("missing")).ReturnsAsync((AIChatSession)null);

        var senderMock = new Mock<IChatNotificationSender>();

        var services = BuildServiceProvider(
            sessionManager: sessionManagerMock.Object,
            profileManager: profileManagerMock.Object,
            notificationSender: senderMock.Object);
        var context = CreateContext("missing", ChatContextType.AIChatSession, services);
        var handler = new EndSessionNotificationActionHandler();

        // Act
        await handler.HandleAsync(context, CancellationToken.None);

        // Assert: SaveAsync should not be called when session is not found.
        sessionManagerMock.Verify(m => m.SaveAsync(It.IsAny<AIChatSession>()), Times.Never);
        // Assert: Notification is not sent when session is not found (early return).
        senderMock.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<ChatContextType>(), It.IsAny<ChatNotification>()), Times.Never);
    }

    // ───────────────────────────────────────────────────────────────
    // ChatInteraction path — only shows notification (no close logic)
    // ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task HandleAsync_ChatInteraction_ShowsSessionEndedNotification()
    {
        // Arrange
        ChatNotification captured = null;
        var senderMock = new Mock<IChatNotificationSender>();
        senderMock.Setup(s => s
            .SendAsync("i1", ChatContextType.ChatInteraction, It.IsAny<ChatNotification>()))
            .Callback<string, ChatContextType, ChatNotification>((_, _, n) => captured = n)
            .Returns(Task.CompletedTask);

        var services = BuildServiceProvider(notificationSender: senderMock.Object);
        var context = CreateContext("i1", ChatContextType.ChatInteraction, services);
        var handler = new EndSessionNotificationActionHandler();

        // Act
        await handler.HandleAsync(context, CancellationToken.None);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(ChatNotificationTypes.SessionEnded, captured.Type);
    }

    [Fact]
    public async Task HandleAsync_AIChatSession_WithPendingPostCloseWork_QueuesProcessing()
    {
        var profile = new AIProfile
        {
            ItemId = "profile-1",
            Type = AIProfileType.Chat,
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

        var session = new AIChatSession
        {
            SessionId = "session-queued",
            ProfileId = profile.ItemId,
            Status = ChatSessionStatus.Active,
        };

        var sessionManagerMock = new Mock<IAIChatSessionManager>();
        sessionManagerMock.Setup(m => m.FindByIdAsync("session-queued")).ReturnsAsync(session);
        sessionManagerMock.Setup(m => m.SaveAsync(session)).Returns(Task.CompletedTask).Verifiable();

        var profileManagerMock = new Mock<IAIProfileManager>();
        profileManagerMock.Setup(m => m.FindByIdAsync(profile.ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);

        ChatNotification captured = null;
        var senderMock = new Mock<IChatNotificationSender>();
        senderMock.Setup(s => s
            .SendAsync("session-queued", ChatContextType.AIChatSession, It.IsAny<ChatNotification>()))
            .Callback<string, ChatContextType, ChatNotification>((_, _, n) => captured = n)
            .Returns(Task.CompletedTask);

        var services = BuildServiceProvider(
            sessionManager: sessionManagerMock.Object,
            profileManager: profileManagerMock.Object,
            notificationSender: senderMock.Object);
        var context = CreateContext("session-queued", ChatContextType.AIChatSession, services);
        var handler = new EndSessionNotificationActionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.Equal(ChatSessionStatus.Closed, session.Status);
        Assert.Equal(PostSessionProcessingStatus.Pending, session.PostSessionProcessingStatus);
        sessionManagerMock.Verify();
        Assert.NotNull(captured);
    }

    // ───────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────
    private static ChatNotificationActionContext CreateContext(string sessionId, ChatContextType chatType, IServiceProvider services)
    {
        return new ChatNotificationActionContext
        {
            SessionId = sessionId,
            NotificationType = ChatNotificationTypes.SessionEnded,
            ActionName = ChatNotificationActionNames.EndSession,
            ChatType = chatType,
            ConnectionId = "conn-1",
            Services = services,
        };
    }

    private static ServiceProvider BuildServiceProvider(
        IAIChatSessionManager sessionManager = null,
        IAIProfileManager profileManager = null,
        IChatNotificationSender notificationSender = null,
        TimeProvider timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(PassthroughStringLocalizer<>));
        if (sessionManager is not null)
        {
            services.AddSingleton(sessionManager);
        }

        if (profileManager is not null)
        {
            services.AddSingleton(profileManager);
        }

        if (notificationSender is not null)
        {
            services.AddSingleton(notificationSender);
        }

        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        return services.BuildServiceProvider();
    }

    private sealed class PassthroughStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name]
        {
            get
            {
                return new(name, name);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                return new(name, string.Format(name, arguments));
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return [];
        }
    }
}
