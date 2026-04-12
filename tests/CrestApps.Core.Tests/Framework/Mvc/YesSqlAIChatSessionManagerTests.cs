using System.Security.Claims;
using CrestApps.Core;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.Data.YesSql.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Moq;

namespace CrestApps.OrchardCore.Tests.Framework.Mvc;

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
            new Mock<global::YesSql.ISession>().Object,
            promptStore.Object,
            TimeProvider.System);

        var session = await manager.NewAsync(profile, new NewAIChatSessionContext());

        Assert.Equal("user-1", session.UserId);
        Assert.Equal("handoff", session.ResponseHandlerName);
        promptStore.Verify(store => store.CreateAsync(It.Is<AIChatSessionPrompt>(prompt =>
            prompt.SessionId == session.SessionId &&
            prompt.Role == ChatRole.Assistant &&
            prompt.Title == "Welcome" &&
            prompt.Content == "Hello there" &&
            !prompt.IsGeneratedPrompt &&
            prompt.CreatedUtc != default)),
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
            new Mock<global::YesSql.ISession>(MockBehavior.Strict).Object,
            promptStore.Object,
            TimeProvider.System);

        var session = await manager.NewAsync(new AIProfile
        {
            ItemId = "profile-1",
            Type = AIProfileType.Utility,
        }, new NewAIChatSessionContext());

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
            new Mock<global::YesSql.ISession>(MockBehavior.Strict).Object,
            promptStore.Object,
            TimeProvider.System);

        var session = await manager.NewAsync(profile, new NewAIChatSessionContext());

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
            new Mock<global::YesSql.ISession>(MockBehavior.Strict).Object,
            promptStore.Object,
            TimeProvider.System);

        var session = await manager.NewAsync(profile, new NewAIChatSessionContext());

        Assert.Equal("handoff", session.ResponseHandlerName);
        promptStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SaveAsync_UpdatesLastActivityAndPersistsSession()
    {
        var now = new DateTime(2026, 04, 08, 15, 00, 00, DateTimeKind.Utc);
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(provider => provider.GetUtcNow()).Returns(new DateTimeOffset(now));

        var sessionStore = new Mock<global::YesSql.ISession>();
        sessionStore
            .Setup(session => session.SaveAsync(It.IsAny<AIChatSession>()))
            .Returns(Task.CompletedTask);
        sessionStore
            .Setup(session => session.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            sessionStore.Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            timeProvider.Object);

        var chatSession = new AIChatSession
        {
            SessionId = "session-1",
        };

        await sessionManager.SaveAsync(chatSession);

        Assert.Equal(now, chatSession.LastActivityUtc);
        sessionStore.Verify(session => session.SaveAsync(chatSession), Times.Once);
        sessionStore.Verify(session => session.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_WhenSessionIsNull_ThrowsArgumentNullException()
    {
        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            new Mock<global::YesSql.ISession>(MockBehavior.Strict).Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentNullException>(() => sessionManager.SaveAsync(null!));
    }

    [Fact]
    public async Task FindByIdAsync_WhenIdIsEmpty_ThrowsArgumentException()
    {
        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            new Mock<global::YesSql.ISession>(MockBehavior.Strict).Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            TimeProvider.System);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sessionManager.FindByIdAsync(string.Empty));
    }

    [Fact]
    public async Task DeleteAsync_WhenSessionIdIsEmpty_ThrowsArgumentException()
    {
        var sessionManager = new YesSqlAIChatSessionManager(
            new Mock<IHttpContextAccessor>(MockBehavior.Strict).Object,
            new Mock<global::YesSql.ISession>(MockBehavior.Strict).Object,
            new Mock<IAIChatSessionPromptStore>(MockBehavior.Strict).Object,
            TimeProvider.System);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sessionManager.DeleteAsync(string.Empty));
    }
}
