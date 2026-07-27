using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured materialize-all-before-tail prompt construction with the production
/// bounded-ring implementation of <see cref="ClaudeOrchestrator"/> across bounded and unbounded
/// history settings for dense and sparse eligibility. This class must remain unsealed because
/// BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class ClaudeOrchestratorPromptsBenchmarks
{
    private const string SystemMessage = "benchmark-system";
    private const string UserMessage = "benchmark-user";

    private static readonly Func<OrchestrationContext, List<ChatMessage>> _legacy = BuildPromptsLegacy;
    private static readonly Func<OrchestrationContext, List<ChatMessage>> _current = CreateBuildPromptsDelegate();

    private OrchestrationContext _context;
    private List<ChatMessage> _messages;

    /// <summary>
    /// Gets or sets the total number of source messages.
    /// </summary>
    [Params(10, 1_000, 10_000)]
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
    /// Creates immutable benchmark inputs and verifies exact output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _messages = CreateMessages(MessageCount, Eligibility);
        _context = CreateContext(_messages, PastMessagesCount);

        EnsureEquivalent(_legacy(_context), _current(_context));
    }

    /// <summary>
    /// Constructs prompts using the captured materialize-all-before-tail implementation.
    /// </summary>
    /// <returns>The constructed prompts.</returns>
    [Benchmark(Baseline = true)]
    public List<ChatMessage> Legacy()
    {
        return _legacy(_context);
    }

    /// <summary>
    /// Constructs prompts using the production bounded-ring implementation.
    /// </summary>
    /// <returns>The constructed prompts.</returns>
    [Benchmark]
    public List<ChatMessage> Current()
    {
        return _current(_context);
    }

    /// <summary>
    /// Preserves the original materialize-all-before-tail prompt construction as the benchmark baseline.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <returns>The constructed prompts.</returns>
    private static List<ChatMessage> BuildPromptsLegacy(OrchestrationContext context)
    {
        var prompts = new List<ChatMessage>();
        var systemMessage = context.CompletionContext.SystemMessage;

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            prompts.Add(new ChatMessage(ChatRole.System, systemMessage));
        }

        var history = context.ConversationHistory?
            .Where(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            .Where(message => !string.IsNullOrWhiteSpace(message.Text))
            .ToList() ?? [];

        if (context.CompletionContext.PastMessagesCount > 1)
        {
            var count = Math.Min(history.Count, context.CompletionContext.PastMessagesCount.Value);
            prompts.AddRange(history.Skip(Math.Max(0, history.Count - count)));
        }
        else
        {
            prompts.AddRange(history);
        }

        if (prompts.Count == 0 || prompts[^1].Text != context.UserMessage)
        {
            prompts.Add(new ChatMessage(ChatRole.User, context.UserMessage));
        }

        return prompts;
    }

    /// <summary>
    /// Creates an orchestration context that carries the benchmark history and settings.
    /// </summary>
    /// <param name="messages">The conversation history.</param>
    /// <param name="pastMessagesCount">The configured number of past messages.</param>
    /// <returns>The orchestration context.</returns>
    private static OrchestrationContext CreateContext(List<ChatMessage> messages, int pastMessagesCount)
    {
        return new OrchestrationContext
        {
            UserMessage = UserMessage,
            ConversationHistory = messages,
            CompletionContext = new AICompletionContext
            {
                SystemMessage = SystemMessage,
                PastMessagesCount = pastMessagesCount,
            },
        };
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
    /// Verifies exact values, ordering, and history source-message identity.
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

            if (index > 0 &&
                index < legacy.Count - 1 &&
                !ReferenceEquals(legacyMessage, currentMessage))
            {
                throw new InvalidOperationException(
                    $"Prompt implementations returned different source-message references at index {index}.");
            }
        }
    }

    /// <summary>
    /// Creates a strongly typed delegate for the private production prompt-construction helper.
    /// </summary>
    /// <returns>The production prompt-construction delegate.</returns>
    private static Func<OrchestrationContext, List<ChatMessage>> CreateBuildPromptsDelegate()
    {
        var method = typeof(ClaudeOrchestrator).GetMethod(
            "BuildPrompts",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unable to find the prompt-construction helper.");

        return method.CreateDelegate<Func<OrchestrationContext, List<ChatMessage>>>();
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
}
