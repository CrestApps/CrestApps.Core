using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured prompt-selection implementation with the production implementation across
/// bounded and unbounded history settings. This class must remain unsealed because BenchmarkDotNet
/// generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class NamedAICompletionClientPromptsBenchmarks
{
    private static readonly Func<IEnumerable<ChatMessage>, AICompletionContext, List<ChatMessage>> _legacy =
        GetPromptsLegacy;
    private static readonly Func<IEnumerable<ChatMessage>, AICompletionContext, List<ChatMessage>> _current =
        CreateGetPromptsDelegate();

    private AICompletionContext _context;
    private List<ChatMessage> _messages;

    /// <summary>
    /// Gets or sets the total number of source messages.
    /// </summary>
    [Params(10, 1_000, 100_000)]
    public int MessageCount { get; set; }

    /// <summary>
    /// Gets or sets the configured number of past messages.
    /// </summary>
    [Params(1, 2, 20, 200)]
    public int PastMessagesCount { get; set; }

    /// <summary>
    /// Gets or sets the eligible-message density.
    /// </summary>
    [Params(PromptEligibility.Dense, PromptEligibility.Sparse)]
    public PromptEligibility Eligibility { get; set; }

    /// <summary>
    /// Gets or sets whether the source is a list or a forward-only iterator.
    /// </summary>
    [Params(PromptInputKind.List, PromptInputKind.Iterator)]
    public PromptInputKind InputKind { get; set; }

    /// <summary>
    /// Creates immutable benchmark inputs and verifies exact output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _messages = CreateMessages(MessageCount, Eligibility);
        _context = new AICompletionContext
        {
            PastMessagesCount = PastMessagesCount,
            SystemMessage = "benchmark-system",
        };

        var legacy = _legacy(GetInput(), _context);
        var current = _current(GetInput(), _context);

        EnsureEquivalent(legacy, current);
    }

    /// <summary>
    /// Selects prompts using the captured materialize-all implementation.
    /// </summary>
    /// <returns>The selected prompts.</returns>
    [Benchmark(Baseline = true)]
    public List<ChatMessage> Legacy()
    {
        return _legacy(GetInput(), _context);
    }

    /// <summary>
    /// Selects prompts using the production implementation.
    /// </summary>
    /// <returns>The selected prompts.</returns>
    [Benchmark]
    public List<ChatMessage> Current()
    {
        return _current(GetInput(), _context);
    }

    /// <summary>
    /// Preserves the original production prompt-selection implementation as the benchmark baseline.
    /// </summary>
    /// <param name="messages">The source messages.</param>
    /// <param name="context">The completion context.</param>
    /// <returns>The selected prompts.</returns>
    private static List<ChatMessage> GetPromptsLegacy(
        IEnumerable<ChatMessage> messages,
        AICompletionContext context)
    {
        var chatMessages = messages.Where(static message =>
            (message.Role == ChatRole.User || message.Role == ChatRole.Assistant) &&
            (!string.IsNullOrEmpty(message.Text) || message.Contents is { Count: > 0 }));

        var prompts = new List<ChatMessage>();
        var systemMessage = context.SystemMessage;

        if (!string.IsNullOrEmpty(systemMessage))
        {
            prompts.Add(new ChatMessage(ChatRole.System, systemMessage));
        }

        var materializedMessages = chatMessages.ToList();

        if (context.PastMessagesCount > 1)
        {
            var skip = GetTotalMessagesToSkip(
                materializedMessages.Count,
                context.PastMessagesCount.Value);

            prompts.AddRange(materializedMessages.Skip(skip).Take(context.PastMessagesCount.Value));
        }
        else
        {
            prompts.AddRange(materializedMessages);
        }

        return prompts;
    }

    /// <summary>
    /// Gets the source enumerable for the configured input kind.
    /// </summary>
    /// <returns>The list or a fresh iterator over the same messages.</returns>
    private IEnumerable<ChatMessage> GetInput()
    {
        return InputKind == PromptInputKind.List
            ? _messages
            : EnumerateMessages();
    }

    /// <summary>
    /// Enumerates the benchmark messages through a forward-only iterator.
    /// </summary>
    /// <returns>The message iterator.</returns>
    private IEnumerable<ChatMessage> EnumerateMessages()
    {
        foreach (var message in _messages)
        {
            yield return message;
        }
    }

    /// <summary>
    /// Creates source messages with the requested eligibility density.
    /// </summary>
    /// <param name="messageCount">The total message count.</param>
    /// <param name="eligibility">The eligible-message density.</param>
    /// <returns>The source messages.</returns>
    private static List<ChatMessage> CreateMessages(
        int messageCount,
        PromptEligibility eligibility)
    {
        var messages = new List<ChatMessage>(messageCount);

        for (var index = 0; index < messageCount; index++)
        {
            if (eligibility == PromptEligibility.Dense)
            {
                var role = index % 2 == 0
                    ? ChatRole.User
                    : ChatRole.Assistant;

                messages.Add(new ChatMessage(role, $"message-{index}"));

                continue;
            }

            messages.Add(CreateSparseMessage(index));
        }

        return messages;
    }

    /// <summary>
    /// Creates a sparse-distribution message with one eligible message per ten source messages.
    /// </summary>
    /// <param name="index">The source index.</param>
    /// <returns>The sparse-distribution message.</returns>
    private static ChatMessage CreateSparseMessage(int index)
    {
        return (index % 10) switch
        {
            0 => new ChatMessage(
                index % 20 == 0
                    ? ChatRole.User
                    : ChatRole.Assistant,
                $"eligible-{index}"),
            1 or 6 => new ChatMessage(ChatRole.System, $"system-{index}"),
            2 or 7 => new ChatMessage(ChatRole.Tool, $"tool-{index}"),
            3 or 8 => new ChatMessage(new ChatRole("observer"), $"unknown-{index}"),
            _ => new ChatMessage(
                index % 2 == 0
                    ? ChatRole.User
                    : ChatRole.Assistant,
                (string)null),
        };
    }

    /// <summary>
    /// Computes the original number of materialized messages to skip.
    /// </summary>
    /// <param name="totalMessages">The total eligible-message count.</param>
    /// <param name="pastMessageCount">The requested tail count.</param>
    /// <returns>The number of messages to skip.</returns>
    private static int GetTotalMessagesToSkip(int totalMessages, int pastMessageCount)
    {
        if (pastMessageCount > 0 && totalMessages > pastMessageCount)
        {
            return totalMessages - pastMessageCount;
        }

        return 0;
    }

    /// <summary>
    /// Verifies exact values, ordering, content identity, and source-message identity.
    /// </summary>
    /// <param name="legacy">The legacy result.</param>
    /// <param name="current">The production result.</param>
    private static void EnsureEquivalent(
        List<ChatMessage> legacy,
        List<ChatMessage> current)
    {
        if (legacy.Count != current.Count)
        {
            throw new InvalidOperationException(
                $"Prompt implementations returned different counts: {legacy.Count} and {current.Count}.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            var legacyMessage = legacy[index];
            var currentMessage = current[index];

            if (legacyMessage.Role != currentMessage.Role ||
                legacyMessage.Text != currentMessage.Text ||
                legacyMessage.Contents.Count != currentMessage.Contents.Count)
            {
                throw new InvalidOperationException(
                    $"Prompt implementations returned different values at index {index}.");
            }

            if (index > 0 && !ReferenceEquals(legacyMessage, currentMessage))
            {
                throw new InvalidOperationException(
                    $"Prompt implementations returned different source-message references at index {index}.");
            }

            for (var contentIndex = 0; contentIndex < legacyMessage.Contents.Count; contentIndex++)
            {
                if (index > 0 &&
                    !ReferenceEquals(
                        legacyMessage.Contents[contentIndex],
                        currentMessage.Contents[contentIndex]))
                {
                    throw new InvalidOperationException(
                        $"Prompt implementations returned different content references at index {index}, content {contentIndex}.");
                }
            }
        }
    }

    /// <summary>
    /// Creates a strongly typed delegate for the private production prompt-selection helper.
    /// </summary>
    /// <returns>The production prompt-selection delegate.</returns>
    private static Func<IEnumerable<ChatMessage>, AICompletionContext, List<ChatMessage>> CreateGetPromptsDelegate()
    {
        var method = typeof(NamedAICompletionClient).GetMethod(
            "GetPrompts",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unable to find the prompt-selection helper.");

        return method.CreateDelegate<Func<IEnumerable<ChatMessage>, AICompletionContext, List<ChatMessage>>>();
    }

    /// <summary>
    /// Defines the eligible-message density.
    /// </summary>
    public enum PromptEligibility
    {
        /// <summary>
        /// Every source message is eligible.
        /// </summary>
        Dense,

        /// <summary>
        /// One in ten source messages is eligible.
        /// </summary>
        Sparse,
    }

    /// <summary>
    /// Defines the source enumerable shape.
    /// </summary>
    public enum PromptInputKind
    {
        /// <summary>
        /// Uses the materialized list directly.
        /// </summary>
        List,

        /// <summary>
        /// Uses a fresh forward-only iterator.
        /// </summary>
        Iterator,
    }
}
