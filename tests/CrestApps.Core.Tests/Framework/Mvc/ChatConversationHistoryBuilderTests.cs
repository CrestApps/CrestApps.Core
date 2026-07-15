using System.Collections;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Verifies the shared chat conversation-history compatibility contract.
/// </summary>
public sealed class ChatConversationHistoryBuilderTests
{
    private static readonly DateTime _originUtc =
        new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Verifies an empty stored history includes the newly created user prompt.
    /// </summary>
    [Fact]
    public void Build_WithEmptyHistory_IncludesNewPrompt()
    {
        AssertForBoth(
            [],
            Prompt("new", ChatRole.User, "new prompt", 0),
            (ChatRole.User, "new prompt"));
    }

    /// <summary>
    /// Verifies a single stored prompt with the new identifier is not appended twice.
    /// </summary>
    [Fact]
    public void Build_WithSingleStoredNewPrompt_DoesNotAppendDuplicate()
    {
        AssertForBoth(
            [Prompt("new", ChatRole.User, "stored prompt", 0)],
            Prompt("new", ChatRole.User, "new prompt", 1),
            (ChatRole.User, "stored prompt"));
    }

    /// <summary>
    /// Verifies unordered histories retain stable timestamp ordering, generated-message filtering,
    /// and appended-new-prompt tie behavior.
    /// </summary>
    [Fact]
    public void Build_WithManyUnorderedPrompts_PreservesLegacyOrderingAndFiltering()
    {
        AssertForBoth(
            [
                Prompt("later", ChatRole.Assistant, "later existing", 2),
                Prompt("early-1", ChatRole.User, "early first", 1),
                Prompt("generated", ChatRole.Assistant, "generated", 1, isGenerated: true),
                Prompt("early-2", ChatRole.System, "early second", 1),
                Prompt("latest", ChatRole.Assistant, "latest", 3),
            ],
            Prompt("new", ChatRole.User, "new equal", 2),
            (ChatRole.User, "early first"),
            (ChatRole.System, "early second"),
            (ChatRole.Assistant, "later existing"),
            (ChatRole.User, "new equal"),
            (ChatRole.Assistant, "latest"));
    }

    /// <summary>
    /// Verifies the absent new prompt is inserted at its stable timestamp position.
    /// </summary>
    /// <param name="newOffset">The new prompt timestamp offset.</param>
    /// <param name="expectedTexts">The expected comma-delimited text order.</param>
    [Theory]
    [InlineData(-1, "new,first,last")]
    [InlineData(0, "first,new,last")]
    [InlineData(1, "first,new,last")]
    [InlineData(3, "first,last,new")]
    public void Build_WithEarlierEqualMiddleOrLaterNewPrompt_InsertsAtStablePosition(
        int newOffset,
        string expectedTexts)
    {
        var histories = BuildBoth(
            [
                Prompt("first", ChatRole.User, "first", 0),
                Prompt("last", ChatRole.Assistant, "last", 2),
            ],
            Prompt("new", ChatRole.User, "new", newOffset));
        var expected = expectedTexts.Split(',');

        foreach (var history in histories)
        {
            Assert.Equal(expected, history.Select(message => message.Text));
        }
    }

    /// <summary>
    /// Verifies every stored duplicate identifier is retained and the supplied new prompt is omitted
    /// when any stored identifier matches it.
    /// </summary>
    [Fact]
    public void Build_WithDuplicateStoredNewPromptIds_RetainsStoredDuplicatesOnly()
    {
        AssertForBoth(
            [
                Prompt("new", ChatRole.User, "first duplicate", 0),
                Prompt("other", ChatRole.Assistant, "other", 1),
                Prompt("new", ChatRole.Assistant, "second duplicate", 2),
            ],
            Prompt("new", ChatRole.User, "must not be appended", -1),
            (ChatRole.User, "first duplicate"),
            (ChatRole.Assistant, "other"),
            (ChatRole.Assistant, "second duplicate"));
    }

    /// <summary>
    /// Verifies a generated stored prompt with the new identifier still suppresses insertion before
    /// generated prompts are filtered from the resulting history.
    /// </summary>
    [Fact]
    public void Build_WithGeneratedStoredNewPromptId_FiltersStoredPromptWithoutAppendingNewPrompt()
    {
        AssertForBoth(
            [
                Prompt("new", ChatRole.Assistant, "generated duplicate", 0, isGenerated: true),
                Prompt("other", ChatRole.User, "visible", 1),
            ],
            Prompt("new", ChatRole.User, "must not be appended", 2),
            (ChatRole.User, "visible"));
    }

    /// <summary>
    /// Verifies an absent generated new prompt participates in inclusion semantics but is filtered
    /// from the resulting conversation history.
    /// </summary>
    [Fact]
    public void Build_WithGeneratedNewPrompt_DoesNotProjectIt()
    {
        AssertForBoth(
            [Prompt("stored", ChatRole.User, "stored", 0)],
            Prompt("new", ChatRole.Assistant, "generated new", 1, isGenerated: true),
            (ChatRole.User, "stored"));
    }

    /// <summary>
    /// Verifies null text uses the chat message's empty-text normalization while empty and whitespace
    /// values remain included.
    /// </summary>
    [Fact]
    public void Build_WithNullEmptyAndBlankText_PreservesLegacyChatMessageMapping()
    {
        AssertForBoth(
            [
                Prompt("null", ChatRole.User, null, 0),
                Prompt("empty", ChatRole.Assistant, string.Empty, 1),
                Prompt("blank", ChatRole.System, " \t", 2),
            ],
            Prompt("new", ChatRole.User, null, 3),
            (ChatRole.User, string.Empty),
            (ChatRole.Assistant, string.Empty),
            (ChatRole.System, " \t"),
            (ChatRole.User, string.Empty));
    }

    /// <summary>
    /// Verifies list, iterator, and single-use inputs produce identical results while forward-only
    /// inputs are enumerated exactly once.
    /// </summary>
    [Fact]
    public void Build_WithListIteratorAndSingleUseInputs_EnumeratesForwardOnlyInputsOnce()
    {
        var interactionPrompts = CreateInteractionPrompts(
        [
            Prompt("first", ChatRole.User, "first", 0),
            Prompt("last", ChatRole.Assistant, "last", 2),
        ]);
        var interactionNewPrompt = CreateInteractionPrompt(
            Prompt("new", ChatRole.User, "new", 1));

        AssertInputShapes(
            interactionPrompts,
            interactionNewPrompt,
            static (prompts, newPrompt) =>
                ChatConversationHistoryBuilder.Build(prompts, newPrompt));

        var sessionPrompts = CreateSessionPrompts(
        [
            Prompt("first", ChatRole.User, "first", 0),
            Prompt("last", ChatRole.Assistant, "last", 2),
        ]);
        var sessionNewPrompt = CreateSessionPrompt(
            Prompt("new", ChatRole.User, "new", 1));

        AssertInputShapes(
            sessionPrompts,
            sessionNewPrompt,
            static (prompts, newPrompt) =>
                ChatConversationHistoryBuilder.Build(prompts, newPrompt));
    }

    /// <summary>
    /// Verifies a null source preserves the legacy LINQ argument failure.
    /// </summary>
    [Fact]
    public void Build_WithNullSource_ThrowsArgumentNullExceptionForSource()
    {
        var interactionException = Assert.Throws<ArgumentNullException>(
            () => ChatConversationHistoryBuilder.Build(
                (IEnumerable<ChatInteractionPrompt>)null!,
                CreateInteractionPrompt(Prompt("new", ChatRole.User, "new", 0))));
        var sessionException = Assert.Throws<ArgumentNullException>(
            () => ChatConversationHistoryBuilder.Build(
                (IEnumerable<AIChatSessionPrompt>)null!,
                CreateSessionPrompt(Prompt("new", ChatRole.User, "new", 0))));

        Assert.Equal("source", interactionException.ParamName);
        Assert.Equal("source", sessionException.ParamName);
    }

    /// <summary>
    /// Verifies null stored entries preserve the legacy null-reference failure.
    /// </summary>
    [Fact]
    public void Build_WithNullEntry_ThrowsNullReferenceException()
    {
        Assert.Throws<NullReferenceException>(
            () => ChatConversationHistoryBuilder.Build(
                new ChatInteractionPrompt[]
                {
                    CreateInteractionPrompt(Prompt("first", ChatRole.User, "first", 0)),
                    null!,
                },
                CreateInteractionPrompt(Prompt("new", ChatRole.User, "new", 1))));
        Assert.Throws<NullReferenceException>(
            () => ChatConversationHistoryBuilder.Build(
                new AIChatSessionPrompt[]
                {
                    CreateSessionPrompt(Prompt("first", ChatRole.User, "first", 0)),
                    null!,
                },
                CreateSessionPrompt(Prompt("new", ChatRole.User, "new", 1))));
    }

    /// <summary>
    /// Verifies enumerator-acquisition exceptions propagate by identity.
    /// </summary>
    [Fact]
    public void Build_WhenGetEnumeratorThrows_PropagatesExactException()
    {
        var interactionException = new InvalidOperationException("interaction-get-enumerator");
        var interactionPrompts =
            new GetEnumeratorThrowingEnumerable<ChatInteractionPrompt>(interactionException);
        var actualInteractionException = Assert.Throws<InvalidOperationException>(
            () => ChatConversationHistoryBuilder.Build(
                interactionPrompts,
                CreateInteractionPrompt(Prompt("new", ChatRole.User, "new", 0))));

        Assert.Same(interactionException, actualInteractionException);
        Assert.Equal(1, interactionPrompts.GetEnumeratorCount);

        var sessionException = new InvalidOperationException("session-get-enumerator");
        var sessionPrompts =
            new GetEnumeratorThrowingEnumerable<AIChatSessionPrompt>(sessionException);
        var actualSessionException = Assert.Throws<InvalidOperationException>(
            () => ChatConversationHistoryBuilder.Build(
                sessionPrompts,
                CreateSessionPrompt(Prompt("new", ChatRole.User, "new", 0))));

        Assert.Same(sessionException, actualSessionException);
        Assert.Equal(1, sessionPrompts.GetEnumeratorCount);
    }

    /// <summary>
    /// Verifies iteration exceptions propagate by identity and dispose the source iterator.
    /// </summary>
    [Fact]
    public void Build_WhenMoveNextThrows_PropagatesExactExceptionAndDisposesIterator()
    {
        AssertMoveNextException(
            CreateInteractionPrompt(Prompt("first", ChatRole.User, "first", 0)),
            CreateInteractionPrompt(Prompt("new", ChatRole.User, "new", 1)),
            static (prompts, newPrompt) =>
                ChatConversationHistoryBuilder.Build(prompts, newPrompt));
        AssertMoveNextException(
            CreateSessionPrompt(Prompt("first", ChatRole.User, "first", 0)),
            CreateSessionPrompt(Prompt("new", ChatRole.User, "new", 1)),
            static (prompts, newPrompt) =>
                ChatConversationHistoryBuilder.Build(prompts, newPrompt));
    }

    /// <summary>
    /// Verifies input shapes for one prompt model.
    /// </summary>
    /// <typeparam name="TPrompt">The prompt model type.</typeparam>
    /// <param name="prompts">The source prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <param name="build">The history builder.</param>
    private static void AssertInputShapes<TPrompt>(
        List<TPrompt> prompts,
        TPrompt newPrompt,
        Func<IEnumerable<TPrompt>, TPrompt, List<ChatMessage>> build)
    {
        var listHistory = build(prompts, newPrompt);
        var iteratorEnumerationCount = 0;

        IEnumerable<TPrompt> CreateIterator()
        {
            iteratorEnumerationCount++;

            foreach (var prompt in prompts)
            {
                yield return prompt;
            }
        }

        var iteratorHistory = build(CreateIterator(), newPrompt);
        var singleUsePrompts = new SingleUseEnumerable<TPrompt>(prompts);
        var singleUseHistory = build(singleUsePrompts, newPrompt);

        Assert.Equal(["first", "new", "last"], listHistory.Select(message => message.Text));
        Assert.Equal(["first", "new", "last"], iteratorHistory.Select(message => message.Text));
        Assert.Equal(["first", "new", "last"], singleUseHistory.Select(message => message.Text));
        Assert.Equal(1, iteratorEnumerationCount);
        Assert.Equal(1, singleUsePrompts.GetEnumeratorCount);
    }

    /// <summary>
    /// Verifies an iteration failure for one prompt model.
    /// </summary>
    /// <typeparam name="TPrompt">The prompt model type.</typeparam>
    /// <param name="firstPrompt">The first yielded prompt.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <param name="build">The history builder.</param>
    private static void AssertMoveNextException<TPrompt>(
        TPrompt firstPrompt,
        TPrompt newPrompt,
        Func<IEnumerable<TPrompt>, TPrompt, List<ChatMessage>> build)
    {
        var expectedException = new InvalidOperationException("move-next");
        var enumerationCount = 0;
        var isDisposed = false;

        IEnumerable<TPrompt> CreateThrowingIterator()
        {
            enumerationCount++;

            try
            {
                yield return firstPrompt;

                throw expectedException;
            }
            finally
            {
                isDisposed = true;
            }
        }

        var actualException = Assert.Throws<InvalidOperationException>(
            () => build(CreateThrowingIterator(), newPrompt));

        Assert.Same(expectedException, actualException);
        Assert.Equal(1, enumerationCount);
        Assert.True(isDisposed);
    }

    /// <summary>
    /// Builds and verifies both prompt model variants.
    /// </summary>
    /// <param name="prompts">The stored prompt specifications.</param>
    /// <param name="newPrompt">The newly created prompt specification.</param>
    /// <param name="expected">The expected role and text sequence.</param>
    private static void AssertForBoth(
        IReadOnlyList<PromptSpecification> prompts,
        PromptSpecification newPrompt,
        params (ChatRole Role, string Text)[] expected)
    {
        foreach (var history in BuildBoth(prompts, newPrompt))
        {
            Assert.Equal(expected.Length, history.Count);

            for (var index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index].Role, history[index].Role);
                Assert.Equal(expected[index].Text, history[index].Text);
            }
        }
    }

    /// <summary>
    /// Builds history for both stored prompt models.
    /// </summary>
    /// <param name="prompts">The stored prompt specifications.</param>
    /// <param name="newPrompt">The newly created prompt specification.</param>
    /// <returns>The two projected histories.</returns>
    private static List<ChatMessage>[] BuildBoth(
        IReadOnlyList<PromptSpecification> prompts,
        PromptSpecification newPrompt)
    {
        return
        [
            ChatConversationHistoryBuilder.Build(
                CreateInteractionPrompts(prompts),
                CreateInteractionPrompt(newPrompt)),
            ChatConversationHistoryBuilder.Build(
                CreateSessionPrompts(prompts),
                CreateSessionPrompt(newPrompt)),
        ];
    }

    /// <summary>
    /// Creates a prompt specification.
    /// </summary>
    /// <param name="itemId">The prompt identifier.</param>
    /// <param name="role">The prompt role.</param>
    /// <param name="text">The prompt text.</param>
    /// <param name="createdOffset">The creation timestamp offset in minutes.</param>
    /// <param name="isGenerated">Whether the prompt is generated.</param>
    /// <returns>The prompt specification.</returns>
    private static PromptSpecification Prompt(
        string itemId,
        ChatRole role,
        string text,
        int createdOffset,
        bool isGenerated = false)
    {
        return new(
            itemId,
            role,
            text,
            _originUtc.AddMinutes(createdOffset),
            isGenerated);
    }

    /// <summary>
    /// Creates chat interaction prompts.
    /// </summary>
    /// <param name="prompts">The prompt specifications.</param>
    /// <returns>The prompts.</returns>
    private static List<ChatInteractionPrompt> CreateInteractionPrompts(
        IEnumerable<PromptSpecification> prompts)
    {
        return prompts.Select(CreateInteractionPrompt).ToList();
    }

    /// <summary>
    /// Creates one chat interaction prompt.
    /// </summary>
    /// <param name="prompt">The prompt specification.</param>
    /// <returns>The prompt.</returns>
    private static ChatInteractionPrompt CreateInteractionPrompt(
        PromptSpecification prompt)
    {
        return new()
        {
            ItemId = prompt.ItemId,
            ChatInteractionId = "interaction",
            Role = prompt.Role,
            Text = prompt.Text,
            CreatedUtc = prompt.CreatedUtc,
            IsGeneratedPrompt = prompt.IsGenerated,
        };
    }

    /// <summary>
    /// Creates chat session prompts.
    /// </summary>
    /// <param name="prompts">The prompt specifications.</param>
    /// <returns>The prompts.</returns>
    private static List<AIChatSessionPrompt> CreateSessionPrompts(
        IEnumerable<PromptSpecification> prompts)
    {
        return prompts.Select(CreateSessionPrompt).ToList();
    }

    /// <summary>
    /// Creates one chat session prompt.
    /// </summary>
    /// <param name="prompt">The prompt specification.</param>
    /// <returns>The prompt.</returns>
    private static AIChatSessionPrompt CreateSessionPrompt(
        PromptSpecification prompt)
    {
        return new()
        {
            ItemId = prompt.ItemId,
            SessionId = "session",
            Role = prompt.Role,
            Content = prompt.Text,
            CreatedUtc = prompt.CreatedUtc,
            IsGeneratedPrompt = prompt.IsGenerated,
        };
    }

    /// <summary>
    /// Describes a stored prompt independently of its concrete model.
    /// </summary>
    /// <param name="ItemId">The prompt identifier.</param>
    /// <param name="Role">The prompt role.</param>
    /// <param name="Text">The prompt text.</param>
    /// <param name="CreatedUtc">The creation timestamp.</param>
    /// <param name="IsGenerated">Whether the prompt is generated.</param>
    private sealed record PromptSpecification(
        string ItemId,
        ChatRole Role,
        string Text,
        DateTime CreatedUtc,
        bool IsGenerated);

    /// <summary>
    /// Provides an enumerable that rejects a second enumeration.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    private sealed class SingleUseEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _items;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleUseEnumerable{T}"/> class.
        /// </summary>
        /// <param name="items">The items to enumerate.</param>
        public SingleUseEnumerable(IEnumerable<T> items)
        {
            _items = items;
        }

        /// <summary>
        /// Gets the number of generic enumerators requested.
        /// </summary>
        public int GetEnumeratorCount { get; private set; }

        /// <summary>
        /// Returns the single permitted generic enumerator.
        /// </summary>
        /// <returns>The item enumerator.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCount++;

            if (GetEnumeratorCount > 1)
            {
                throw new InvalidOperationException(
                    "The enumerable may only be enumerated once.");
            }

            return _items.GetEnumerator();
        }

        /// <summary>
        /// Returns the single permitted non-generic enumerator.
        /// </summary>
        /// <returns>The item enumerator.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Provides an enumerable whose enumerator acquisition always fails.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    private sealed class GetEnumeratorThrowingEnumerable<T> : IEnumerable<T>
    {
        private readonly Exception _exception;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetEnumeratorThrowingEnumerable{T}"/> class.
        /// </summary>
        /// <param name="exception">The exception to throw.</param>
        public GetEnumeratorThrowingEnumerable(Exception exception)
        {
            _exception = exception;
        }

        /// <summary>
        /// Gets the number of generic enumerators requested.
        /// </summary>
        public int GetEnumeratorCount { get; private set; }

        /// <summary>
        /// Throws the configured exception.
        /// </summary>
        /// <returns>This member does not return.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCount++;

            throw _exception;
        }

        /// <summary>
        /// Returns the failing non-generic enumerator.
        /// </summary>
        /// <returns>This member does not return.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
