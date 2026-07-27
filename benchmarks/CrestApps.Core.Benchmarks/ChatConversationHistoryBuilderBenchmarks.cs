using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured hub conversation-history construction with the shared production helper.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class ChatConversationHistoryBuilderBenchmarks
{
    private static readonly DateTime _originUtc =
        new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    private AIChatSessionPrompt[] _chatSessionPrompts;
    private AIChatSessionPrompt _chatSessionNewPrompt;
    private ChatInteractionPrompt[] _interactionPrompts;
    private ChatInteractionPrompt _interactionNewPrompt;

    /// <summary>
    /// Gets or sets the prompt model used by the hub path.
    /// </summary>
    [Params(PromptHistoryModel.ChatInteraction, PromptHistoryModel.AIChatSession)]
    public PromptHistoryModel Model { get; set; }

    /// <summary>
    /// Gets or sets the number of stored prompts.
    /// </summary>
    [Params(10, 100, 1_000, 10_000)]
    public int PromptCount { get; set; }

    /// <summary>
    /// Gets or sets the generated-prompt distribution.
    /// </summary>
    [Params(GeneratedPromptDistribution.None, GeneratedPromptDistribution.Quarter)]
    public GeneratedPromptDistribution GeneratedPrompts { get; set; }

    /// <summary>
    /// Gets or sets whether the stored result already contains the newly created prompt identifier.
    /// </summary>
    [Params(false, true)]
    public bool NewPromptPresent { get; set; }

    /// <summary>
    /// Gets or sets whether the store result follows or violates its creation-time ordering contract.
    /// </summary>
    [Params(PromptHistoryOrdering.Ordered, PromptHistoryOrdering.Unordered)]
    public PromptHistoryOrdering Ordering { get; set; }

    /// <summary>
    /// Creates stable benchmark inputs and verifies exact output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var specifications = CreatePromptSpecifications();
        var newPromptId = NewPromptPresent
            ? specifications[^1].ItemId
            : "new-prompt";
        var newPromptSpecification = new PromptSpecification(
            newPromptId,
            ChatRole.User,
            "new prompt",
            _originUtc.AddMinutes(PromptCount + 1),
            IsGenerated: false);

        _interactionPrompts = specifications
            .Select(CreateInteractionPrompt)
            .ToArray();
        _interactionNewPrompt = CreateInteractionPrompt(newPromptSpecification);
        _chatSessionPrompts = specifications
            .Select(CreateChatSessionPrompt)
            .ToArray();
        _chatSessionNewPrompt = CreateChatSessionPrompt(newPromptSpecification);

        EnsureEquivalent(Legacy(), Current());
    }

    /// <summary>
    /// Builds history using the captured per-hub materialize, search, stable sort, filter, and
    /// projection pipeline.
    /// </summary>
    /// <returns>The projected conversation history.</returns>
    [Benchmark(Baseline = true)]
    public List<ChatMessage> Legacy()
    {
        return Model switch
        {
            PromptHistoryModel.ChatInteraction => BuildLegacy(
                _interactionPrompts,
                _interactionNewPrompt),
            PromptHistoryModel.AIChatSession => BuildLegacy(
                _chatSessionPrompts,
                _chatSessionNewPrompt),
            _ => throw new InvalidOperationException(
                $"Unsupported prompt history model '{Model}'."),
        };
    }

    /// <summary>
    /// Builds history using the shared production helper.
    /// </summary>
    /// <returns>The projected conversation history.</returns>
    [Benchmark]
    public List<ChatMessage> Current()
    {
        return Model switch
        {
            PromptHistoryModel.ChatInteraction =>
                ChatConversationHistoryBuilder.Build(
                    _interactionPrompts,
                    _interactionNewPrompt),
            PromptHistoryModel.AIChatSession =>
                ChatConversationHistoryBuilder.Build(
                    _chatSessionPrompts,
                    _chatSessionNewPrompt),
            _ => throw new InvalidOperationException(
                $"Unsupported prompt history model '{Model}'."),
        };
    }

    /// <summary>
    /// Preserves the original chat interaction hub implementation as the benchmark baseline.
    /// </summary>
    /// <param name="prompts">The stored prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <returns>The projected conversation history.</returns>
    private static List<ChatMessage> BuildLegacy(
        IEnumerable<ChatInteractionPrompt> prompts,
        ChatInteractionPrompt newPrompt)
    {
        var conversationHistorySource = prompts.ToList();

        if (!conversationHistorySource.Any(prompt =>
            prompt.ItemId == newPrompt.ItemId))
        {
            conversationHistorySource.Add(newPrompt);
        }

        return conversationHistorySource
            .OrderBy(prompt => prompt.CreatedUtc)
            .Where(prompt => !prompt.IsGeneratedPrompt)
            .Select(prompt => new ChatMessage(prompt.Role, prompt.Text))
            .ToList();
    }

    /// <summary>
    /// Preserves the original AI chat session hub implementation as the benchmark baseline.
    /// </summary>
    /// <param name="prompts">The stored prompts.</param>
    /// <param name="newPrompt">The newly created prompt.</param>
    /// <returns>The projected conversation history.</returns>
    private static List<ChatMessage> BuildLegacy(
        IEnumerable<AIChatSessionPrompt> prompts,
        AIChatSessionPrompt newPrompt)
    {
        var conversationHistorySource = prompts.ToList();

        if (!conversationHistorySource.Any(prompt =>
            prompt.ItemId == newPrompt.ItemId))
        {
            conversationHistorySource.Add(newPrompt);
        }

        return conversationHistorySource
            .OrderBy(prompt => prompt.CreatedUtc)
            .Where(prompt => !prompt.IsGeneratedPrompt)
            .Select(prompt => new ChatMessage(prompt.Role, prompt.Content))
            .ToList();
    }

    /// <summary>
    /// Creates ordered or deliberately unordered prompt specifications with equal-timestamp pairs.
    /// </summary>
    /// <returns>The benchmark prompt specifications.</returns>
    private PromptSpecification[] CreatePromptSpecifications()
    {
        var generatedCount = GeneratedPrompts == GeneratedPromptDistribution.Quarter
            ? (int)Math.Round(PromptCount * 0.25, MidpointRounding.AwayFromZero)
            : 0;
        var specifications = new PromptSpecification[PromptCount];

        for (var index = 0; index < specifications.Length; index++)
        {
            specifications[index] = new PromptSpecification(
                $"prompt-{index:D5}",
                index % 2 == 0
                    ? ChatRole.User
                    : ChatRole.Assistant,
                $"prompt text {index}",
                _originUtc.AddMinutes(index / 2),
                IsGenerated: index < generatedCount);
        }

        if (Ordering == PromptHistoryOrdering.Ordered)
        {
            return specifications;
        }

        var unordered = new PromptSpecification[specifications.Length];

        for (var index = 0; index < unordered.Length; index++)
        {
            unordered[index] = specifications[(index * 37) % specifications.Length];
        }

        return unordered;
    }

    /// <summary>
    /// Creates a chat interaction prompt.
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
    /// Creates an AI chat session prompt.
    /// </summary>
    /// <param name="prompt">The prompt specification.</param>
    /// <returns>The prompt.</returns>
    private static AIChatSessionPrompt CreateChatSessionPrompt(
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
    /// Verifies exact message count, stable order, role, and text equivalence.
    /// </summary>
    /// <param name="legacy">The captured legacy output.</param>
    /// <param name="current">The production output.</param>
    private static void EnsureEquivalent(
        List<ChatMessage> legacy,
        List<ChatMessage> current)
    {
        if (legacy.Count != current.Count)
        {
            throw new InvalidOperationException(
                $"History implementations returned different counts: {legacy.Count} and {current.Count}.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            if (legacy[index].Role != current[index].Role ||
                legacy[index].Text != current[index].Text)
            {
                throw new InvalidOperationException(
                    $"History implementations returned different messages at index {index}.");
            }
        }
    }

    /// <summary>
    /// Describes one benchmark prompt independently of its stored model.
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
    /// Defines the hub prompt model under test.
    /// </summary>
    public enum PromptHistoryModel
    {
        /// <summary>
        /// Uses <see cref="ChatInteractionPrompt"/>.
        /// </summary>
        ChatInteraction,

        /// <summary>
        /// Uses <see cref="AIChatSessionPrompt"/>.
        /// </summary>
        AIChatSession,
    }

    /// <summary>
    /// Defines the generated-prompt distribution.
    /// </summary>
    public enum GeneratedPromptDistribution
    {
        /// <summary>
        /// No stored prompts are generated.
        /// </summary>
        None,

        /// <summary>
        /// Approximately one quarter of stored prompts are generated.
        /// </summary>
        Quarter,
    }

    /// <summary>
    /// Defines whether the store result follows its public ordering contract.
    /// </summary>
    public enum PromptHistoryOrdering
    {
        /// <summary>
        /// Prompts are ordered by creation time.
        /// </summary>
        Ordered,

        /// <summary>
        /// Prompts deliberately violate creation-time ordering.
        /// </summary>
        Unordered,
    }
}
