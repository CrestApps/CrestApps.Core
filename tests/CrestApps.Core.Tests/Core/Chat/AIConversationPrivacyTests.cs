using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Privacy;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Chat;

public sealed class AIConversationPrivacyTests
{
    private static readonly DateTime _now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Write to jane.doe+orders@example.co.uk today", "Write to [EMAIL] today")]
    [InlineData("Call me at (702) 555-0147 or +44 20 7946 0958.", "Call me at [PHONE] or [PHONE].")]
    [InlineData("My card is 4111 1111 1111 1111.", "My card is [CARD].")]
    [InlineData("SSN 123-45-6789 please", "SSN [ID NUMBER] please")]
    [InlineData("Order 4471 shipped on 2026-10-27 for $1,250.00 at 14:30.", "Order 4471 shipped on 2026-10-27 for $1,250.00 at 14:30.")]
    [InlineData("Tracking 1234 5678 9012 3456 is not a card or a phone.", "Tracking 1234 5678 9012 3456 is not a card or a phone.")]
    public void Redactor_ReplacesWhatPatternsRecognize(string text, string expected)
    {
        Assert.Equal(expected, AIPersonalDataRedactor.RedactPatterns(text));
    }

    [Fact]
    public void Redactor_ReplacesFoundDetailsAsWholeWordsLongestFirst()
    {
        var terms = new[]
        {
            new AIPersonalDataTerm { Text = "Al", Kind = "name" },
            new AIPersonalDataTerm { Text = "Al Rivera", Kind = "name" },
            new AIPersonalDataTerm { Text = "12 Elm Street", Kind = "address" },
            new AIPersonalDataTerm { Text = "x", Kind = "name" },
            new AIPersonalDataTerm { Text = "M-55120", Kind = "id" },
        };

        Assert.Equal(
            "[NAME] also said [NAME] lives at [ADDRESS], member [ID NUMBER], email [EMAIL].",
            AIPersonalDataRedactor.Redact("Al Rivera also said al lives at 12 elm street, member M-55120, email al@example.com.", terms));

        Assert.Equal("[DATE OF BIRTH]", AIPersonalDataRedactor.GetPlaceholder("birthdate"));
        Assert.Null(AIPersonalDataRedactor.Redact(null, terms));
    }

    [Fact]
    public void Privacy_AlsoRedactsThePartsOfAFoundName()
    {
        var terms = AIConversationPrivacyService.WithNameParts(
        [
            new AIPersonalDataTerm { Text = "Marcus Bell", Kind = "name" },
            new AIPersonalDataTerm { Text = "18 Willow Lane", Kind = "address" },
            new AIPersonalDataTerm { Text = "al de la Cruz", Kind = "name" },
            new AIPersonalDataTerm { Text = " ", Kind = "name" },
        ]);

        Assert.Equal(["Marcus Bell", "18 Willow Lane", "al de la Cruz", "Marcus", "Bell", "Cruz"], terms.Select(term => term.Text));
        Assert.Equal(
            "Hello [NAME], I see [NAME] lives at [ADDRESS].",
            AIPersonalDataRedactor.Redact("Hello Marcus, I see Marcus Bell lives at 18 Willow Lane.", terms));
    }

    [Fact]
    public void Privacy_RedactsAndStripsEverythingTheSessionKeeps()
    {
        var terms = new[] { new AIPersonalDataTerm { Text = "Dana Smith", Kind = "name" } };

        var redacted = NewSession("s1");

        var text = AIConversationPrivacyService.CollectText(redacted, [new AIChatSessionPrompt { Content = "My number is 702 555 0147." }], ["Dana Smith was helped."]);
        Assert.Contains("Hi, I'm Dana Smith", text);
        Assert.Contains("My number is", text);
        Assert.Contains("Dana Smith was helped.", text);

        AIConversationPrivacyService.Redact(redacted, terms);

        Assert.Equal("Hi, I'm [NAME]", redacted.Title);
        Assert.Equal("[NAME] ([EMAIL]) wants a refund.", redacted.PostSessionResults["Summary"].Value);
        Assert.Equal(["evenings", "[PHONE]"], redacted.ExtractedData["Phone"].Values);

        var stripped = NewSession("s2");
        AIConversationPrivacyService.Strip(stripped);

        Assert.Null(stripped.Title);
        Assert.Null(stripped.PostSessionResults["Summary"].Value);
        Assert.Equal("Summary", stripped.PostSessionResults["Summary"].Name);
        Assert.Empty(stripped.ExtractedData["Phone"].Values);
    }

    [Fact]
    public void Privacy_WaitsForAConversationToEndAndBeProcessed()
    {
        Assert.False(AIConversationPrivacyService.IsFinished(new AIChatSession { Status = ChatSessionStatus.Active }));
        Assert.False(AIConversationPrivacyService.IsFinished(new AIChatSession { Status = ChatSessionStatus.Closed, PostSessionProcessingStatus = PostSessionProcessingStatus.Pending }));
        Assert.True(AIConversationPrivacyService.IsFinished(new AIChatSession { Status = ChatSessionStatus.Closed, PostSessionProcessingStatus = PostSessionProcessingStatus.Completed }));
        Assert.True(AIConversationPrivacyService.IsFinished(new AIChatSession { Status = ChatSessionStatus.Abandoned }));
    }

    [Fact]
    public async Task Apply_RedactsTheTranscriptAndWhatHandlersKeep_Once()
    {
        var sessions = new InMemorySessionStore();
        var session = NewSession("s1");
        session.RemoteAddress = "203.0.113.7";
        sessions.Add(session);

        var prompts = new InMemoryPromptStore();
        prompts.Add(new AIChatSessionPrompt { ItemId = "p1", SessionId = "s1", Content = "Email me at dana@example.com" });
        prompts.Add(new AIChatSessionPrompt { ItemId = "p2", SessionId = "s1", Content = "Thanks!" });

        var handler = new NotesHandler("Agent noted dana@example.com.");
        var service = NewService(sessions, prompts, [handler]);

        Assert.True(await service.ApplyAsync(session, AIDataStorageMode.RedactPersonalData, TestContext.Current.CancellationToken));

        Assert.Equal("Email me at [EMAIL]", prompts.Prompts["p1"].Content);
        Assert.Equal(["p1"], prompts.Updated);
        // Without a utility deployment only the patterns are recognized.
        Assert.Equal("Dana Smith ([EMAIL]) wants a refund.", session.PostSessionResults["Summary"].Value);
        Assert.Equal("Agent noted [EMAIL].", handler.Notes);
        Assert.Contains("Agent noted dana@example.com.", handler.CollectedText);
        Assert.Null(session.RemoteAddress);
        Assert.True(session.TryGet<AIConversationPrivacyMetadata>(out var metadata));
        Assert.Equal(AIDataStorageMode.RedactPersonalData, metadata.Mode);
        Assert.Equal(_now, metadata.AppliedUtc);
        Assert.Same(session, sessions.Saved.Single());

        // A conversation the mode was applied to is left alone.
        Assert.False(await service.ApplyAsync(session, AIDataStorageMode.MetadataOnly, TestContext.Current.CancellationToken));
        Assert.False(await service.ApplyAsync(NewSession("s3"), AIDataStorageMode.Everything, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_MetadataOnly_DeletesTheTranscriptAndClearsWhatHandlersKeep()
    {
        var sessions = new InMemorySessionStore();
        var session = NewSession("s1");
        sessions.Add(session);

        var prompts = new InMemoryPromptStore();
        prompts.Add(new AIChatSessionPrompt { ItemId = "p1", SessionId = "s1", Content = "Hi" });

        var handler = new NotesHandler("Agent noted something.");
        var service = NewService(sessions, prompts, [handler]);

        Assert.True(await service.ApplyAsync(session, AIDataStorageMode.MetadataOnly, TestContext.Current.CancellationToken));

        Assert.Empty(prompts.Prompts);
        Assert.Null(session.Title);
        Assert.Null(handler.Notes);
        Assert.True(session.TryGet<AIConversationPrivacyMetadata>(out var metadata));
        Assert.Equal(AIDataStorageMode.MetadataOnly, metadata.Mode);
    }

    [Fact]
    public async Task Process_AppliesTheModeInOrderAndStopsAtAConversationStillGoingOn()
    {
        var sessions = new InMemorySessionStore();
        sessions.Add(NewSession("old", lastActivityUtc: _now.AddDays(-3)));
        sessions.Add(NewSession("first", lastActivityUtc: _now.AddHours(-5)));
        sessions.Add(NewSession("second", lastActivityUtc: _now.AddHours(-4)));
        sessions.Add(NewSession("active", lastActivityUtc: _now.AddHours(-3), status: ChatSessionStatus.Active));
        sessions.Add(NewSession("after", lastActivityUtc: _now.AddHours(-2)));
        sessions.Add(NewSession("recent", lastActivityUtc: _now.AddMinutes(-5)));

        var service = NewService(sessions, new InMemoryPromptStore(), []);
        var profile = new AIProfile { ItemId = "profile1" };
        profile.AlterSettings<AIProfileDataPrivacySettings>(settings =>
        {
            settings.StorageMode = AIDataStorageMode.MetadataOnly;
            settings.StorageModeSinceUtc = _now.AddDays(-1);
        });

        var result = await service.ProcessAsync(profile, null, TestContext.Current.CancellationToken);

        // The conversation from before the mode was chosen is kept; the walk stops at the one still going on.
        Assert.Equal(2, result.Applied);
        Assert.Equal(_now.AddHours(-4), result.Cursor);
        Assert.Equal(["first", "second"], sessions.Saved.Select(session => session.SessionId));
        Assert.Equal(0, result.Deleted);

        // Keeping everything tells the host to forget the cursor.
        profile.AlterSettings<AIProfileDataPrivacySettings>(settings => settings.StorageMode = AIDataStorageMode.Everything);
        Assert.Null((await service.ProcessAsync(profile, result.Cursor, TestContext.Current.CancellationToken)).Cursor);
    }

    [Fact]
    public async Task Process_DeletesConversationsPastTheRetention()
    {
        var sessions = new InMemorySessionStore();
        sessions.Add(NewSession("expired", lastActivityUtc: _now.AddDays(-40)));
        sessions.Add(NewSession("kept", lastActivityUtc: _now.AddDays(-10)));

        var sessionHandler = new Mock<IAIChatSessionHandler>();
        var privacyHandler = new NotesHandler(null);
        var service = NewService(sessions, new InMemoryPromptStore(), [privacyHandler], [sessionHandler.Object]);
        var profile = new AIProfile { ItemId = "profile1" };
        profile.AlterSettings<AIProfileDataPrivacySettings>(settings => settings.RetentionDays = 30);

        var result = await service.ProcessAsync(profile, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Deleted);
        Assert.Equal(["expired"], sessions.Deleted);
        Assert.Equal(["expired"], privacyHandler.DeletedSessions);
        sessionHandler.Verify(handler => handler.DeletingAsync(It.Is<DeletingContext<AIChatSession>>(context => context.Model.SessionId == "expired"), It.IsAny<CancellationToken>()), Times.Once);
        sessionHandler.Verify(handler => handler.DeletedAsync(It.Is<DeletedContext<AIChatSession>>(context => context.Model.SessionId == "expired"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(await service.DeleteAsync("missing", TestContext.Current.CancellationToken));
    }

    private static AIConversationPrivacyService NewService(
        InMemorySessionStore sessions,
        InMemoryPromptStore prompts,
        IEnumerable<IAIConversationPrivacyHandler> privacyHandlers,
        IEnumerable<IAIChatSessionHandler> sessionHandlers = null)
    {
        // Without a utility deployment only the patterns are used, so no model is asked.
        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.ResolveSlotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AIDeployment)null);

        return new AIConversationPrivacyService(
            sessions,
            prompts.Object,
            sessionHandlers ?? [],
            privacyHandlers,
            deploymentManager.Object,
            null,
            Options.Create(new AIConversationPrivacyOptions()),
            new FixedTimeProvider(new DateTimeOffset(_now)),
            NullLogger<AIConversationPrivacyService>.Instance);
    }

    private static AIChatSession NewSession(string sessionId, DateTime? lastActivityUtc = null, ChatSessionStatus status = ChatSessionStatus.Closed)
    {
        var session = new AIChatSession
        {
            SessionId = sessionId,
            ProfileId = "profile1",
            Title = "Hi, I'm Dana Smith",
            Status = status,
            LastActivityUtc = lastActivityUtc ?? _now.AddHours(-1),
        };
        session.PostSessionResults["Summary"] = new PostSessionResult { Name = "Summary", Value = "Dana Smith (dana@example.com) wants a refund." };
        session.ExtractedData["Phone"] = new ExtractedFieldState { Values = ["evenings", "+1 702 555 0147"] };

        return session;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class NotesHandler : AIConversationPrivacyHandlerBase
    {
        public NotesHandler(string notes)
        {
            Notes = notes;
        }

        public string Notes { get; private set; }

        public string CollectedText { get; private set; }

        public List<string> DeletedSessions { get; } = [];

        public override Task CollectingAsync(AIConversationPrivacyContext context, CancellationToken cancellationToken)
        {
            context.Texts.Add(Notes);
            CollectedText = string.Join('\n', context.Texts);

            return Task.CompletedTask;
        }

        public override Task ApplyingAsync(AIConversationPrivacyContext context, CancellationToken cancellationToken)
        {
            Notes = context.Mode == AIDataStorageMode.MetadataOnly
                ? null
                : context.Redact(Notes);

            return Task.CompletedTask;
        }

        public override Task DeletingAsync(AIChatSession session, CancellationToken cancellationToken)
        {
            DeletedSessions.Add(session.SessionId);

            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySessionStore : IAIChatSessionStore
    {
        private readonly List<AIChatSession> _sessions = [];

        public List<AIChatSession> Saved { get; } = [];

        public List<string> Deleted { get; } = [];

        public void Add(AIChatSession session)
        {
            _sessions.Add(session);
        }

        public Task<AIChatSession> FindByIdAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_sessions.FirstOrDefault(session => session.SessionId == sessionId));
        }

        public Task<IReadOnlyList<AIChatSession>> GetInactiveActiveSessionsAsync(string profileId, DateTime cutoffUtc, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AIChatSession>> GetClosedSessionsAsync(string profileId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AIChatSession>> ListByLastActivityAsync(string profileId, DateTime? afterUtc, DateTime beforeUtc, int count, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AIChatSession> sessions = _sessions
                .Where(session => session.ProfileId == profileId && (afterUtc is null || session.LastActivityUtc > afterUtc) && session.LastActivityUtc < beforeUtc)
                .OrderBy(session => session.LastActivityUtc)
                .Take(count)
                .ToList();

            return Task.FromResult(sessions);
        }

        public Task SaveAsync(AIChatSession chatSession, CancellationToken cancellationToken = default)
        {
            Saved.Add(chatSession);

            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Deleted.Add(sessionId);

            return Task.FromResult(_sessions.RemoveAll(session => session.SessionId == sessionId) > 0);
        }
    }

    private sealed class InMemoryPromptStore
    {
        private readonly Mock<IAIChatSessionPromptStore> _mock = new();

        public InMemoryPromptStore()
        {
            _mock
                .Setup(store => store.GetPromptsAsync(It.IsAny<string>()))
                .ReturnsAsync((string sessionId) => Prompts.Values.Where(prompt => prompt.SessionId == sessionId).ToList());
            _mock
                .Setup(store => store.DeleteAllPromptsAsync(It.IsAny<string>()))
                .ReturnsAsync((string sessionId) =>
                {
                    var ids = Prompts.Values.Where(prompt => prompt.SessionId == sessionId).Select(prompt => prompt.ItemId).ToList();
                    ids.ForEach(id => Prompts.Remove(id));

                    return ids.Count;
                });
            _mock
                .Setup(store => store.UpdateAsync(It.IsAny<AIChatSessionPrompt>(), It.IsAny<CancellationToken>()))
                .Callback((AIChatSessionPrompt prompt, CancellationToken _) => Updated.Add(prompt.ItemId))
                .Returns(ValueTask.CompletedTask);
        }

        public Dictionary<string, AIChatSessionPrompt> Prompts { get; } = [];

        public List<string> Updated { get; } = [];

        public IAIChatSessionPromptStore Object => _mock.Object;

        public void Add(AIChatSessionPrompt prompt)
        {
            Prompts[prompt.ItemId] = prompt;
        }
    }
}
