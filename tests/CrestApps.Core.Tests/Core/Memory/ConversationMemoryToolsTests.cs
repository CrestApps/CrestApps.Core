using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.AI.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Memory;

public sealed class ConversationMemoryToolsTests
{
    [Fact]
    public async Task SearchConversation_SearchesOnlyTheMessagesOnTheInvocationScope()
    {
        using var scope = AIInvocationScope.Begin();
        AIInvocationScope.Current.Items[MemoryConstants.CompletionContextKeys.OlderConversationMessages] = new List<ChatMessage>
        {
            new(ChatRole.User, "The invoice number is INV-20931 for the Denver office."),
            new(ChatRole.Assistant, "Thanks, I noted invoice INV-20931."),
            new(ChatRole.User, "Let's talk about hiring."),
            new(ChatRole.Assistant, "Sure."),
        };

        var result = await InvokeAsync(new SearchConversationTool(), new() { ["query"] = "Denver office invoice" });

        Assert.Contains("INV-20931", result);
        Assert.DoesNotContain("hiring", result);
    }

    [Fact]
    public async Task SearchConversation_WithoutOlderMessages_SaysThereIsNothingToSearch()
    {
        using var scope = AIInvocationScope.Begin();

        var result = await InvokeAsync(new SearchConversationTool(), new() { ["query"] = "anything" });

        Assert.Contains("nothing earlier to search", result);
    }

    [Fact]
    public async Task SearchPastConversations_NeverReturnsAnotherUsersOrProfilesConversations()
    {
        // The store below ignores the user filter on purpose: the tool must still keep to the scope user.
        var store = new LeakyChatSessionStore(
            Session("own-old", "user-1", "profile-1", "Kitchen remodel budget", "My kitchen remodel budget is 25000 dollars."),
            Session("other-user", "user-2", "profile-1", "Someone else's kitchen", "My kitchen remodel budget is 99000 dollars."),
            Session("other-profile", "user-1", "profile-2", "Kitchen in another assistant", "Kitchen remodel budget for profile two."),
            Session("current", "user-1", "profile-1", "Current conversation", "Kitchen remodel budget again."));
        using var scope = BeginPastConversationsScope("user-1", "profile-1", "current");

        var result = await InvokeAsync(new SearchPastConversationsTool(), new() { ["query"] = "kitchen remodel budget" }, store);

        Assert.Contains("25000", result);
        Assert.DoesNotContain("99000", result);
        Assert.DoesNotContain("profile two", result);
        Assert.DoesNotContain("again", result);
        Assert.Equal("user-1", store.LastQuery.UserId);
        Assert.Equal("profile-1", store.LastQuery.ProfileId);
    }

    [Fact]
    public async Task ListRecentConversations_ListsOnlyTheScopeUsersConversations()
    {
        var store = new LeakyChatSessionStore(
            Session("own", "user-1", "profile-1", "Trip to Lisbon", "Plan a trip to Lisbon in May."),
            Session("other-user", "user-2", "profile-1", "Private medical question", "Something private."));
        store.Sessions["own"].Put(new AIConversationMemory { Summary = "Planning a May trip to Lisbon." });
        using var scope = BeginPastConversationsScope("user-1", "profile-1", null);

        var result = await InvokeAsync(new ListRecentConversationsTool(), [], store);

        Assert.Contains("Planning a May trip to Lisbon.", result);
        Assert.DoesNotContain("medical", result);
    }

    [Fact]
    public async Task PastConversationTools_WithoutAScope_RefuseToSearch()
    {
        using var scope = AIInvocationScope.Begin();
        var store = new LeakyChatSessionStore(Session("own", "user-1", "profile-1", "Anything", "Anything at all."));

        var search = await InvokeAsync(new SearchPastConversationsTool(), new() { ["query"] = "anything" }, store);
        var list = await InvokeAsync(new ListRecentConversationsTool(), [], store);

        Assert.Contains("only available to signed-in users", search);
        Assert.Contains("only available to signed-in users", list);
        Assert.Null(store.LastQuery);
    }

    private static AIInvocationScope.Scope BeginPastConversationsScope(string userId, string profileId, string currentSessionId)
    {
        var scope = AIInvocationScope.Begin();
        AIInvocationScope.Current.Items[MemoryConstants.CompletionContextKeys.PastConversationsScope] = new PastConversationsScope(userId, profileId, currentSessionId);

        return scope;
    }

    private static (AIChatSession Session, AIChatSessionPrompt[] Prompts) Session(string id, string userId, string profileId, string title, string userText)
    {
        var session = new AIChatSession
        {
            SessionId = id,
            UserId = userId,
            ProfileId = profileId,
            Title = title,
            CreatedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            LastActivityUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        return (session,
        [
            new AIChatSessionPrompt { ItemId = $"{id}-1", SessionId = id, Role = ChatRole.User, Content = userText, CreatedUtc = session.CreatedUtc },
            new AIChatSessionPrompt { ItemId = $"{id}-2", SessionId = id, Role = ChatRole.Assistant, Content = "Understood.", CreatedUtc = session.CreatedUtc.AddMinutes(1) },
        ]);
    }

    private static async Task<string> InvokeAsync(AIFunction tool, Dictionary<string, object> arguments, LeakyChatSessionStore store = null)
    {
        var services = new ServiceCollection().AddSingleton<ITextTokenizer, LuceneTextTokenizer>();

        if (store is not null)
        {
            services.AddSingleton<IAIChatSessionManager>(store).AddSingleton<IAIChatSessionPromptStore>(store.Prompts);
        }

        var result = await tool.InvokeAsync(new AIFunctionArguments(arguments) { Services = services.BuildServiceProvider() }, TestContext.Current.CancellationToken);

        return result?.ToString();
    }

    /// <summary>
    /// A session store that returns every session on every query, as a store that ignored the user filter would.
    /// </summary>
    private sealed class LeakyChatSessionStore : IAIChatSessionManager
    {
        public LeakyChatSessionStore(params (AIChatSession Session, AIChatSessionPrompt[] Prompts)[] sessions)
        {
            Sessions = sessions.ToDictionary(entry => entry.Session.SessionId, entry => entry.Session);
            Prompts = new FakePromptStore(sessions.SelectMany(entry => entry.Prompts).ToList());
        }

        public Dictionary<string, AIChatSession> Sessions { get; }

        public FakePromptStore Prompts { get; }

        public AIChatSessionQueryContext LastQuery { get; private set; }

        public Task<AIChatSessionResult> PageAsync(int page, int pageSize, AIChatSessionQueryContext context = null, CancellationToken cancellationToken = default)
        {
            LastQuery = context;

            return Task.FromResult(new AIChatSessionResult
            {
                Count = Sessions.Count,
                Sessions = Sessions.Values.Select(session => new AIChatSessionEntry
                {
                    SessionId = session.SessionId,
                    ProfileId = session.ProfileId,
                    UserId = session.UserId,
                    Title = session.Title,
                    CreatedUtc = session.CreatedUtc,
                    LastActivityUtc = session.LastActivityUtc,
                }).ToList(),
            });
        }

        public Task<AIChatSession> FindByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Sessions.GetValueOrDefault(id));

        public Task<AIChatSession> FindAsync(string id, CancellationToken cancellationToken = default)
            => FindByIdAsync(id, cancellationToken);

        public Task<AIChatSession> NewAsync(AIProfile profile, NewAIChatSessionContext context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveAsync(AIChatSession chatSession, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> DeleteAllAsync(string profileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakePromptStore : IAIChatSessionPromptStore
    {
        private readonly List<AIChatSessionPrompt> _prompts;

        public FakePromptStore(List<AIChatSessionPrompt> prompts)
        {
            _prompts = prompts;
        }

        public Task<IReadOnlyList<AIChatSessionPrompt>> GetPromptsAsync(string sessionId)
            => Task.FromResult<IReadOnlyList<AIChatSessionPrompt>>(_prompts.Where(prompt => prompt.SessionId == sessionId).ToList());

        public Task<int> DeleteAllPromptsAsync(string sessionId) => throw new NotSupportedException();

        public Task<int> CountAsync(string sessionId) => throw new NotSupportedException();

        public ValueTask<AIChatSessionPrompt> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<AIChatSessionPrompt>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<AIChatSessionPrompt>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<CrestApps.Core.Models.PageResult<AIChatSessionPrompt>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
            where TQuery : CrestApps.Core.Models.QueryContext
            => throw new NotSupportedException();

        public ValueTask CreateAsync(AIChatSessionPrompt entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask UpdateAsync(AIChatSessionPrompt entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(AIChatSessionPrompt entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
