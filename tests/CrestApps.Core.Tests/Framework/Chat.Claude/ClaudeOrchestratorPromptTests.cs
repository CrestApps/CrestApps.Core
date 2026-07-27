using System.Reflection;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Framework.Chat.Claude;

/// <summary>
/// Verifies the exact <see cref="ClaudeOrchestrator"/> prompt-construction contract, including
/// role and text eligibility, the bounded-history threshold, integer boundaries, final-user
/// suppression, duplicate preservation, and null or error timing.
/// </summary>
public sealed class ClaudeOrchestratorPromptTests
{
    private static readonly Func<OrchestrationContext, List<ChatMessage>> _buildPrompts =
        CreateBuildPromptsDelegate();

    /// <summary>
    /// Verifies the system prompt, retained history, and appended user message are emitted in order.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithSystemHistoryAndUser_EmitsSystemThenHistoryThenUser()
    {
        var user = new ChatMessage(ChatRole.User, "history-user");
        var assistant = new ChatMessage(ChatRole.Assistant, "history-assistant");
        var context = CreateContext(
            [user, assistant],
            "current-user",
            systemMessage: "system");

        var prompts = _buildPrompts(context);

        Assert.Equal(4, prompts.Count);
        Assert.Equal(ChatRole.System, prompts[0].Role);
        Assert.Equal("system", prompts[0].Text);
        Assert.Same(user, prompts[1]);
        Assert.Same(assistant, prompts[2]);
        Assert.Equal(ChatRole.User, prompts[3].Role);
        Assert.Equal("current-user", prompts[3].Text);
    }

    /// <summary>
    /// Verifies null, empty, and whitespace system-message handling.
    /// </summary>
    /// <param name="systemMessage">The configured system message.</param>
    /// <param name="isIncluded">Whether a system prompt should be emitted.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("\t", false)]
    [InlineData("system", true)]
    public void BuildPrompts_WithSystemMessage_UsesWhitespaceFiltering(
        string systemMessage,
        bool isIncluded)
    {
        var context = CreateContext([], "user-message", systemMessage: systemMessage);

        var prompts = _buildPrompts(context);

        if (!isIncluded)
        {
            var onlyUser = Assert.Single(prompts);

            Assert.Equal(ChatRole.User, onlyUser.Role);
            Assert.Equal("user-message", onlyUser.Text);

            return;
        }

        Assert.Equal(2, prompts.Count);
        Assert.Equal(ChatRole.System, prompts[0].Role);
        Assert.Equal(systemMessage, prompts[0].Text);
        Assert.Equal(ChatRole.User, prompts[1].Role);
        Assert.Equal("user-message", prompts[1].Text);
    }

    /// <summary>
    /// Verifies the bounded-history threshold and integer-boundary behavior for the retained tail.
    /// </summary>
    /// <param name="pastMessagesCount">The configured past-message count.</param>
    /// <param name="expectedIndexes">The expected retained source-message indexes.</param>
    [Theory]
    [InlineData(null, "0,1,2,3,4")]
    [InlineData(int.MinValue, "0,1,2,3,4")]
    [InlineData(-1, "0,1,2,3,4")]
    [InlineData(0, "0,1,2,3,4")]
    [InlineData(1, "0,1,2,3,4")]
    [InlineData(2, "3,4")]
    [InlineData(3, "2,3,4")]
    [InlineData(10, "0,1,2,3,4")]
    [InlineData(int.MaxValue, "0,1,2,3,4")]
    public void BuildPrompts_WithPastMessagesCount_PreservesTailThreshold(
        int? pastMessagesCount,
        string expectedIndexes)
    {
        var messages = Enumerable
            .Range(0, 5)
            .Select(index => new ChatMessage(ChatRole.User, $"message-{index}"))
            .ToList();
        var context = CreateContext(messages, "current-user", pastMessagesCount: pastMessagesCount);

        var prompts = _buildPrompts(context);
        var indexes = expectedIndexes.Split(',').Select(int.Parse).ToArray();

        Assert.Equal(indexes.Length + 1, prompts.Count);

        for (var index = 0; index < indexes.Length; index++)
        {
            Assert.Same(messages[indexes[index]], prompts[index]);
        }

        var appended = prompts[^1];

        Assert.Equal(ChatRole.User, appended.Role);
        Assert.Equal("current-user", appended.Text);
    }

    /// <summary>
    /// Verifies only user and assistant messages with non-whitespace text remain eligible, and
    /// that content-only or whitespace messages are excluded.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithRolesAndContentShapes_KeepsOnlyNonBlankUserAndAssistantText()
    {
        var system = new ChatMessage(ChatRole.System, "system");
        var tool = new ChatMessage(ChatRole.Tool, "tool");
        var unknown = new ChatMessage(new ChatRole("observer"), "unknown");
        var nullText = new ChatMessage(ChatRole.User, (string)null);
        var emptyText = new ChatMessage(ChatRole.User, string.Empty);
        var whitespaceText = new ChatMessage(ChatRole.Assistant, " \t");
        var emptyTextContent = new ChatMessage(ChatRole.Assistant, [new TextContent(string.Empty)]);
        var dataContent = new ChatMessage(
            ChatRole.User,
            [new DataContent(new byte[] { 1 }, "application/octet-stream")]);
        var user = new ChatMessage(ChatRole.User, "keep-user");
        var assistant = new ChatMessage(ChatRole.Assistant, "keep-assistant");
        var messages = new List<ChatMessage>
        {
            system,
            tool,
            unknown,
            nullText,
            emptyText,
            whitespaceText,
            emptyTextContent,
            dataContent,
            user,
            assistant,
        };

        var prompts = _buildPrompts(CreateContext(messages, "final-user"));

        Assert.Equal(3, prompts.Count);
        Assert.Same(user, prompts[0]);
        Assert.Same(assistant, prompts[1]);
        Assert.Equal(ChatRole.User, prompts[2].Role);
        Assert.Equal("final-user", prompts[2].Text);
    }

    /// <summary>
    /// Verifies the bounded tail is selected after role and text filtering.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithMixedEligibility_SelectsTailOfEligibleMessages()
    {
        var first = new ChatMessage(ChatRole.User, "first");
        var second = new ChatMessage(ChatRole.Assistant, "second");
        var third = new ChatMessage(ChatRole.User, "third");
        var messages = new List<ChatMessage>
        {
            first,
            new(ChatRole.System, "ignored-system"),
            new(ChatRole.User, (string)null),
            second,
            new(ChatRole.Tool, "ignored-tool"),
            new(ChatRole.Assistant, " "),
            new(new ChatRole("observer"), "ignored-unknown"),
            third,
        };
        var context = CreateContext(messages, "final-user", pastMessagesCount: 2);

        var prompts = _buildPrompts(context);

        Assert.Equal(3, prompts.Count);
        Assert.Same(second, prompts[0]);
        Assert.Same(third, prompts[1]);
        Assert.Equal(ChatRole.User, prompts[2].Role);
        Assert.Equal("final-user", prompts[2].Text);
    }

    /// <summary>
    /// Verifies the final user message is suppressed when the last retained message text matches it.
    /// </summary>
    [Fact]
    public void BuildPrompts_WhenLastHistoryMatchesUserMessage_SuppressesDuplicate()
    {
        var earlier = new ChatMessage(ChatRole.Assistant, "earlier");
        var lastUser = new ChatMessage(ChatRole.User, "same-user");
        var context = CreateContext([earlier, lastUser], "same-user");

        var prompts = _buildPrompts(context);

        Assert.Equal(2, prompts.Count);
        Assert.Same(earlier, prompts[0]);
        Assert.Same(lastUser, prompts[1]);
    }

    /// <summary>
    /// Verifies the final user message is appended when the last retained message text differs from it.
    /// </summary>
    [Fact]
    public void BuildPrompts_WhenLastHistoryDiffersFromUserMessage_AppendsUserMessage()
    {
        var lastUser = new ChatMessage(ChatRole.User, "old-user");
        var context = CreateContext([lastUser], "new-user");

        var prompts = _buildPrompts(context);

        Assert.Equal(2, prompts.Count);
        Assert.Same(lastUser, prompts[0]);
        Assert.Equal(ChatRole.User, prompts[1].Role);
        Assert.Equal("new-user", prompts[1].Text);
    }

    /// <summary>
    /// Verifies suppression compares against the last eligible message after blank entries are filtered.
    /// </summary>
    [Fact]
    public void BuildPrompts_WhenTrailingBlankFilteredAndEligibleMatchesUser_SuppressesDuplicate()
    {
        var keep = new ChatMessage(ChatRole.User, "keep");
        var trailingBlank = new ChatMessage(ChatRole.Assistant, "   ");
        var context = CreateContext([keep, trailingBlank], "keep");

        var prompts = _buildPrompts(context);

        Assert.Same(keep, Assert.Single(prompts));
    }

    /// <summary>
    /// Verifies suppression within a bounded tail keeps the retained reference without duplication.
    /// </summary>
    [Fact]
    public void BuildPrompts_WhenBoundedTailLastMatchesUser_SuppressesDuplicate()
    {
        var m0 = new ChatMessage(ChatRole.User, "m0");
        var m1 = new ChatMessage(ChatRole.Assistant, "m1");
        var m2 = new ChatMessage(ChatRole.User, "m2");
        var context = CreateContext([m0, m1, m2], "m2", pastMessagesCount: 2);

        var prompts = _buildPrompts(context);

        Assert.Equal(2, prompts.Count);
        Assert.Same(m1, prompts[0]);
        Assert.Same(m2, prompts[1]);
    }

    /// <summary>
    /// Verifies suppression compares text only, ignoring role, when the system message matches the user message.
    /// </summary>
    [Fact]
    public void BuildPrompts_WhenSystemMessageEqualsUserMessageWithoutHistory_SuppressesUserMessage()
    {
        var context = CreateContext([], "shared", systemMessage: "shared");

        var prompts = _buildPrompts(context);

        var only = Assert.Single(prompts);

        Assert.Equal(ChatRole.System, only.Role);
        Assert.Equal("shared", only.Text);
    }

    /// <summary>
    /// Verifies an empty history without a system message yields only the appended user message.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithEmptyHistoryAndNoSystem_AppendsOnlyUserMessage()
    {
        var context = CreateContext([], "only-user");

        var prompts = _buildPrompts(context);

        var only = Assert.Single(prompts);

        Assert.Equal(ChatRole.User, only.Role);
        Assert.Equal("only-user", only.Text);
    }

    /// <summary>
    /// Verifies a null conversation history is tolerated and yields only the appended user message.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithNullConversationHistory_AppendsOnlyUserMessage()
    {
        var context = CreateContext(null, "only-user", systemMessage: "system");

        var prompts = _buildPrompts(context);

        Assert.Equal(2, prompts.Count);
        Assert.Equal(ChatRole.System, prompts[0].Role);
        Assert.Equal("system", prompts[0].Text);
        Assert.Equal(ChatRole.User, prompts[1].Role);
        Assert.Equal("only-user", prompts[1].Text);
    }

    /// <summary>
    /// Verifies stable ordering and duplicate-reference preservation across the system, history, and user prompts.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithDuplicateReferences_PreservesEveryOccurrenceAndOrder()
    {
        var first = new ChatMessage(ChatRole.User, "first");
        var duplicate = new ChatMessage(ChatRole.Assistant, "duplicate");
        var last = new ChatMessage(ChatRole.User, "last");
        var messages = new List<ChatMessage>
        {
            first,
            duplicate,
            duplicate,
            last,
            duplicate,
        };
        var context = CreateContext(messages, "final-user", systemMessage: "system");

        var prompts = _buildPrompts(context);

        Assert.Equal(7, prompts.Count);
        Assert.Equal(ChatRole.System, prompts[0].Role);
        Assert.Equal("system", prompts[0].Text);
        Assert.Same(first, prompts[1]);
        Assert.Same(duplicate, prompts[2]);
        Assert.Same(duplicate, prompts[3]);
        Assert.Same(last, prompts[4]);
        Assert.Same(duplicate, prompts[5]);
        Assert.Equal(ChatRole.User, prompts[6].Role);
        Assert.Equal("final-user", prompts[6].Text);
    }

    /// <summary>
    /// Verifies duplicate references are preserved when they fall entirely inside the bounded tail.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithDuplicatesInBoundedTail_PreservesEveryOccurrence()
    {
        var earlier = new ChatMessage(ChatRole.User, "earlier");
        var duplicate = new ChatMessage(ChatRole.Assistant, "duplicate");
        var messages = new List<ChatMessage>
        {
            earlier,
            duplicate,
            duplicate,
            duplicate,
        };
        var context = CreateContext(messages, "final-user", pastMessagesCount: 3);

        var prompts = _buildPrompts(context);

        Assert.Equal(4, prompts.Count);
        Assert.Same(duplicate, prompts[0]);
        Assert.Same(duplicate, prompts[1]);
        Assert.Same(duplicate, prompts[2]);
        Assert.Equal(ChatRole.User, prompts[3].Role);
        Assert.Equal("final-user", prompts[3].Text);
    }

    /// <summary>
    /// Verifies a null history element raises the null-reference failure synchronously on the bounded path.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithNullMessageElementBounded_ThrowsDuringInvocation()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            null!,
            new(ChatRole.Assistant, "last"),
        };
        var context = CreateContext(messages, "final-user", pastMessagesCount: 2);

        Assert.Throws<NullReferenceException>(() => _buildPrompts(context));
    }

    /// <summary>
    /// Verifies a null history element raises the null-reference failure synchronously on the unbounded path.
    /// </summary>
    [Fact]
    public void BuildPrompts_WithNullMessageElementUnbounded_ThrowsDuringInvocation()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            null!,
        };
        var context = CreateContext(messages, "final-user", pastMessagesCount: 1);

        Assert.Throws<NullReferenceException>(() => _buildPrompts(context));
    }

    /// <summary>
    /// Creates an orchestration context with the supplied prompt-construction inputs.
    /// </summary>
    /// <param name="conversationHistory">The conversation history.</param>
    /// <param name="userMessage">The current user message.</param>
    /// <param name="systemMessage">The optional system message.</param>
    /// <param name="pastMessagesCount">The optional past-message count.</param>
    /// <returns>The orchestration context.</returns>
    private static OrchestrationContext CreateContext(
        IList<ChatMessage> conversationHistory,
        string userMessage,
        string systemMessage = null,
        int? pastMessagesCount = null)
    {
        return new OrchestrationContext
        {
            UserMessage = userMessage,
            ConversationHistory = conversationHistory,
            CompletionContext = new AICompletionContext
            {
                SystemMessage = systemMessage,
                PastMessagesCount = pastMessagesCount,
            },
        };
    }

    /// <summary>
    /// Creates a strongly typed delegate for the private prompt-construction helper.
    /// </summary>
    /// <returns>The prompt-construction delegate.</returns>
    private static Func<OrchestrationContext, List<ChatMessage>> CreateBuildPromptsDelegate()
    {
        var method = typeof(ClaudeOrchestrator).GetMethod(
            "BuildPrompts",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unable to find the prompt-construction helper.");

        return method.CreateDelegate<Func<OrchestrationContext, List<ChatMessage>>>();
    }
}
