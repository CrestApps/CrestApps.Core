using System.Collections;
using System.Reflection;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Verifies the exact prompt-selection compatibility contract.
/// </summary>
public sealed class NamedAICompletionClientPromptTests
{
    private static readonly Func<IEnumerable<ChatMessage>, AICompletionContext, List<ChatMessage>> _getPrompts =
        CreateGetPromptsDelegate();

    /// <summary>
    /// Verifies the unusual legacy count threshold and integer-boundary behavior.
    /// </summary>
    /// <param name="pastMessagesCount">The configured past-message count.</param>
    /// <param name="expectedIndexes">The expected source-message indexes.</param>
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
    public void GetPrompts_WithPastMessagesCount_PreservesLegacyThreshold(
        int? pastMessagesCount,
        string expectedIndexes)
    {
        var messages = Enumerable
            .Range(0, 5)
            .Select(index => new ChatMessage(ChatRole.User, $"message-{index}"))
            .ToList();
        var context = new AICompletionContext
        {
            PastMessagesCount = pastMessagesCount,
        };

        var prompts = _getPrompts(messages, context);
        var indexes = expectedIndexes.Split(',').Select(int.Parse).ToArray();

        Assert.Equal(indexes.Length, prompts.Count);

        for (var index = 0; index < indexes.Length; index++)
        {
            Assert.Same(messages[indexes[index]], prompts[index]);
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
        var context = new AICompletionContext
        {
            SystemMessage = systemMessage,
        };

        var prompts = _getPrompts([], context);

        if (!isIncluded)
        {
            Assert.Empty(prompts);

            return;
        }

        var prompt = Assert.Single(prompts);

        Assert.Equal(ChatRole.System, prompt.Role);
        Assert.Equal(systemMessage, prompt.Text);
    }

    /// <summary>
    /// Verifies role filtering and the distinction between absent, empty, whitespace, and non-text content.
    /// </summary>
    [Fact]
    public void GetPrompts_WithRolesAndContentShapes_PreservesEligibilityRules()
    {
        var system = new ChatMessage(ChatRole.System, "system");
        var tool = new ChatMessage(ChatRole.Tool, "tool");
        var unknown = new ChatMessage(new ChatRole("observer"), "unknown");
        var nullText = new ChatMessage(ChatRole.User, (string)null);
        var nullContents = new ChatMessage(ChatRole.Assistant, (IList<AIContent>)null);
        var emptyContents = new ChatMessage(ChatRole.User, []);
        var emptyText = new ChatMessage(ChatRole.User, string.Empty);
        var emptyTextContent = new ChatMessage(ChatRole.Assistant, [new TextContent(string.Empty)]);
        var nullTextContent = new ChatMessage(ChatRole.User, [new TextContent(null!)]);
        var whitespaceText = new ChatMessage(ChatRole.Assistant, " \t");
        var dataContent = new ChatMessage(
            ChatRole.User,
            [new DataContent(new byte[] { 1 }, "application/octet-stream")]);
        var messages = new List<ChatMessage>
        {
            system,
            tool,
            unknown,
            nullText,
            nullContents,
            emptyContents,
            emptyText,
            emptyTextContent,
            nullTextContent,
            whitespaceText,
            dataContent,
        };

        var prompts = _getPrompts(messages, new AICompletionContext());

        Assert.Equal(5, prompts.Count);
        Assert.Same(emptyText, prompts[0]);
        Assert.Same(emptyTextContent, prompts[1]);
        Assert.Same(nullTextContent, prompts[2]);
        Assert.Same(whitespaceText, prompts[3]);
        Assert.Same(dataContent, prompts[4]);
    }

    /// <summary>
    /// Verifies that the tail count applies after role and content filtering.
    /// </summary>
    [Fact]
    public void GetPrompts_WithMixedEligibility_SelectsTailOfEligibleMessages()
    {
        var first = new ChatMessage(ChatRole.User, "first");
        var second = new ChatMessage(ChatRole.Assistant, "second");
        var third = new ChatMessage(ChatRole.User, " ");
        var fourth = new ChatMessage(
            ChatRole.Assistant,
            [new DataContent(new byte[] { 2 }, "application/octet-stream")]);
        var messages = new List<ChatMessage>
        {
            first,
            new(ChatRole.System, "ignored-system"),
            new(ChatRole.User, (string)null),
            second,
            new(ChatRole.Tool, "ignored-tool"),
            third,
            new(new ChatRole("observer"), "ignored-unknown"),
            fourth,
        };
        var context = new AICompletionContext
        {
            PastMessagesCount = 2,
        };

        var prompts = _getPrompts(messages, context);

        Assert.Equal(2, prompts.Count);
        Assert.Same(third, prompts[0]);
        Assert.Same(fourth, prompts[1]);
    }

    /// <summary>
    /// Verifies stable ordering, duplicate-reference preservation, and system-message prepending.
    /// </summary>
    [Fact]
    public void GetPrompts_WithDuplicateReferences_PreservesEveryOccurrenceAndOrder()
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
        var context = new AICompletionContext
        {
            SystemMessage = "system",
        };

        var prompts = _getPrompts(messages, context);

        Assert.Equal(6, prompts.Count);
        Assert.Equal(ChatRole.System, prompts[0].Role);
        Assert.Equal("system", prompts[0].Text);
        Assert.Same(first, prompts[1]);
        Assert.Same(duplicate, prompts[2]);
        Assert.Same(duplicate, prompts[3]);
        Assert.Same(last, prompts[4]);
        Assert.Same(duplicate, prompts[5]);
    }

    /// <summary>
    /// Verifies direct list input preserves source identities and order.
    /// </summary>
    [Fact]
    public void GetPrompts_WithListInput_PreservesExpectedTail()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "second"),
            new(ChatRole.User, "third"),
        };
        var context = new AICompletionContext
        {
            PastMessagesCount = 2,
        };

        var prompts = _getPrompts(messages, context);

        Assert.Equal(2, prompts.Count);
        Assert.Same(messages[1], prompts[0]);
        Assert.Same(messages[2], prompts[1]);
    }

    /// <summary>
    /// Verifies iterator input is consumed eagerly and exactly once.
    /// </summary>
    [Fact]
    public void GetPrompts_WithIteratorInput_EnumeratesOnceBeforeReturning()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "second"),
            new(ChatRole.User, "third"),
        };
        var enumerationCount = 0;
        var yieldedCount = 0;

        IEnumerable<ChatMessage> CreateIterator()
        {
            enumerationCount++;

            foreach (var message in messages)
            {
                yieldedCount++;

                yield return message;
            }
        }

        var prompts = _getPrompts(
            CreateIterator(),
            new AICompletionContext
            {
                PastMessagesCount = 2,
            });

        Assert.Equal(1, enumerationCount);
        Assert.Equal(messages.Count, yieldedCount);
        Assert.Equal(2, prompts.Count);
        Assert.Same(messages[1], prompts[0]);
        Assert.Same(messages[2], prompts[1]);
    }

    /// <summary>
    /// Verifies an enumerable that permits only one enumeration remains supported.
    /// </summary>
    [Fact]
    public void GetPrompts_WithSingleUseEnumerable_EnumeratesExactlyOnce()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "second"),
            new(ChatRole.User, "third"),
        };
        var enumerable = new SingleUseEnumerable(messages);

        var prompts = _getPrompts(
            enumerable,
            new AICompletionContext
            {
                PastMessagesCount = 2,
            });

        Assert.Equal(1, enumerable.GetEnumeratorCount);
        Assert.Equal(2, prompts.Count);
        Assert.Same(messages[1], prompts[0]);
        Assert.Same(messages[2], prompts[1]);
    }

    /// <summary>
    /// Verifies a null enumerable fails synchronously when prompt selection is invoked.
    /// </summary>
    [Fact]
    public void GetPrompts_WithNullEnumerable_ThrowsImmediately()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => _getPrompts(null!, new AICompletionContext()));

        Assert.Equal("source", exception.ParamName);
    }

    /// <summary>
    /// Verifies an exception thrown while obtaining the source enumerator propagates synchronously.
    /// </summary>
    [Fact]
    public void GetPrompts_WhenGetEnumeratorThrows_PropagatesExactException()
    {
        var expectedException = new InvalidOperationException("get-enumerator");
        var messages = new GetEnumeratorThrowingEnumerable(expectedException);

        var exception = Assert.Throws<InvalidOperationException>(
            () => _getPrompts(
                messages,
                new AICompletionContext
                {
                    PastMessagesCount = 2,
                }));

        Assert.Same(expectedException, exception);
        Assert.Equal(1, messages.GetEnumeratorCount);
    }

    /// <summary>
    /// Verifies an exception raised during iteration propagates synchronously after one enumeration and disposes the iterator.
    /// </summary>
    [Fact]
    public void GetPrompts_WhenMoveNextThrows_PropagatesExactExceptionAndDisposesIterator()
    {
        var expectedException = new InvalidOperationException("move-next");
        var first = new ChatMessage(ChatRole.User, "first");
        var enumerationCount = 0;
        var isDisposed = false;

        IEnumerable<ChatMessage> CreateThrowingIterator()
        {
            enumerationCount++;

            try
            {
                yield return first;

                throw expectedException;
            }
            finally
            {
                isDisposed = true;
            }
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => _getPrompts(
                CreateThrowingIterator(),
                new AICompletionContext
                {
                    PastMessagesCount = 2,
                }));

        Assert.Same(expectedException, exception);
        Assert.Equal(1, enumerationCount);
        Assert.True(isDisposed);
    }

    /// <summary>
    /// Verifies a null message element raises the existing null-reference failure during eager filtering.
    /// </summary>
    [Fact]
    public void GetPrompts_WithNullMessageElement_ThrowsDuringInvocation()
    {
        var messages = new ChatMessage[]
        {
            new(ChatRole.User, "first"),
            null!,
            new(ChatRole.Assistant, "last"),
        };

        Assert.Throws<NullReferenceException>(
            () => _getPrompts(
                messages,
                new AICompletionContext
                {
                    PastMessagesCount = 2,
                }));
    }

    /// <summary>
    /// Creates a strongly typed delegate for the private production prompt-selection helper.
    /// </summary>
    /// <returns>The prompt-selection delegate.</returns>
    private static Func<IEnumerable<ChatMessage>, AICompletionContext, List<ChatMessage>> CreateGetPromptsDelegate()
    {
        var method = typeof(NamedAICompletionClient).GetMethod(
            "GetPrompts",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unable to find the prompt-selection helper.");

        return method.CreateDelegate<Func<IEnumerable<ChatMessage>, AICompletionContext, List<ChatMessage>>>();
    }

    /// <summary>
    /// Provides an enumerable that rejects a second enumeration.
    /// </summary>
    private sealed class SingleUseEnumerable : IEnumerable<ChatMessage>
    {
        private readonly IEnumerable<ChatMessage> _messages;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleUseEnumerable"/> class.
        /// </summary>
        /// <param name="messages">The messages to enumerate.</param>
        public SingleUseEnumerable(IEnumerable<ChatMessage> messages)
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
        public IEnumerator<ChatMessage> GetEnumerator()
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
    /// Provides an enumerable whose enumerator acquisition always fails.
    /// </summary>
    private sealed class GetEnumeratorThrowingEnumerable : IEnumerable<ChatMessage>
    {
        private readonly Exception _exception;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetEnumeratorThrowingEnumerable"/> class.
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
        /// Throws the configured exception instead of returning an enumerator.
        /// </summary>
        /// <returns>This method does not return.</returns>
        public IEnumerator<ChatMessage> GetEnumerator()
        {
            GetEnumeratorCount++;

            throw _exception;
        }

        /// <summary>
        /// Throws the configured exception instead of returning a non-generic enumerator.
        /// </summary>
        /// <returns>This method does not return.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
