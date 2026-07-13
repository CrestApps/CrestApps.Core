using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;
using SdkChatMessage = OpenAI.Chat.ChatMessage;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured Azure adapter history conversion with the bounded production converter for
/// dense text and sparse-eligibility histories. This class must remain unsealed because
/// BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class AzureOpenAICompletionClientHistoryBenchmarks
{
    private AICompletionContext _context;
    private List<AIChatMessage> _messages;

    /// <summary>
    /// Gets or sets the total number of raw messages.
    /// </summary>
    [Params(10, 1_000, 10_000)]
    public int MessageCount { get; set; }

    /// <summary>
    /// Gets or sets the number of converted history messages retained.
    /// </summary>
    [Params(10, 50)]
    public int PastMessagesCount { get; set; }

    /// <summary>
    /// Gets or sets the raw-message distribution.
    /// </summary>
    [Params(AzureHistoryProfile.DenseText, AzureHistoryProfile.SparseEligibility)]
    public AzureHistoryProfile Profile { get; set; }

    /// <summary>
    /// Creates stable inputs and verifies exact converted SDK message and content equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _messages = AzureOpenAIHistoryBenchmarkCases.CreateMessages(
            MessageCount,
            Profile,
            includeImages: false);
        _context = new AICompletionContext
        {
            PastMessagesCount = PastMessagesCount,
            SystemMessage = "benchmark-system",
        };

        AzureOpenAIHistoryBenchmarkCases.EnsureEquivalent(
            AzureOpenAIHistoryBenchmarkCases.Legacy(_messages, _context),
            AzureOpenAIHistoryBenchmarkCases.Current(_messages, _context));
    }

    /// <summary>
    /// Converts all eligible raw messages before selecting the configured tail.
    /// </summary>
    /// <returns>The converted prompts.</returns>
    [Benchmark(Baseline = true)]
    public List<SdkChatMessage> Legacy()
    {
        return AzureOpenAIHistoryBenchmarkCases.Legacy(_messages, _context);
    }

    /// <summary>
    /// Classifies a bounded eligible tail before creating retained SDK messages.
    /// </summary>
    /// <returns>The converted prompts.</returns>
    [Benchmark]
    public List<SdkChatMessage> Current()
    {
        return AzureOpenAIHistoryBenchmarkCases.Current(_messages, _context);
    }
}

/// <summary>
/// Compares image-heavy histories at reduced scales so each tenth eligible message can retain a
/// realistic 256 KB image without making the short-run suite impractical. This class must remain
/// unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class AzureOpenAICompletionClientImageHistoryBenchmarks
{
    private AICompletionContext _context;
    private List<AIChatMessage> _messages;

    /// <summary>
    /// Gets or sets the total number of raw messages.
    /// </summary>
    [Params(10, 100, 1_000)]
    public int MessageCount { get; set; }

    /// <summary>
    /// Gets or sets the number of converted history messages retained.
    /// </summary>
    [Params(10, 50)]
    public int PastMessagesCount { get; set; }

    /// <summary>
    /// Gets or sets the raw-message distribution.
    /// </summary>
    [Params(AzureHistoryProfile.DenseText, AzureHistoryProfile.SparseEligibility)]
    public AzureHistoryProfile Profile { get; set; }

    /// <summary>
    /// Creates stable inputs and verifies exact converted SDK message, content, media type, and byte equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _messages = AzureOpenAIHistoryBenchmarkCases.CreateMessages(
            MessageCount,
            Profile,
            includeImages: true);
        _context = new AICompletionContext
        {
            PastMessagesCount = PastMessagesCount,
            SystemMessage = "benchmark-system",
        };

        AzureOpenAIHistoryBenchmarkCases.EnsureEquivalent(
            AzureOpenAIHistoryBenchmarkCases.Legacy(_messages, _context),
            AzureOpenAIHistoryBenchmarkCases.Current(_messages, _context));
    }

    /// <summary>
    /// Converts and copies every eligible image before selecting the configured tail.
    /// </summary>
    /// <returns>The converted prompts.</returns>
    [Benchmark(Baseline = true)]
    public List<SdkChatMessage> Legacy()
    {
        return AzureOpenAIHistoryBenchmarkCases.Legacy(_messages, _context);
    }

    /// <summary>
    /// Copies image bytes at encounter time but creates eager SDK image data URIs only for retained messages.
    /// </summary>
    /// <returns>The converted prompts.</returns>
    [Benchmark]
    public List<SdkChatMessage> Current()
    {
        return AzureOpenAIHistoryBenchmarkCases.Current(_messages, _context);
    }
}

/// <summary>
/// Defines the benchmark history distributions.
/// </summary>
public enum AzureHistoryProfile
{
    /// <summary>
    /// Every raw message is eligible.
    /// </summary>
    DenseText,

    /// <summary>
    /// One raw message in ten is eligible.
    /// </summary>
    SparseEligibility,
}

/// <summary>
/// Provides captured legacy and production bounded-tail benchmark cases.
/// </summary>
internal static class AzureOpenAIHistoryBenchmarkCases
{
    private const int ImageSize = 256 * 1024;

    private static readonly Func<AICompletionContext, List<SdkChatMessage>, List<SdkChatMessage>> _getPrompts =
        CreateGetPromptsDelegate();

    /// <summary>
    /// Converts all eligible messages and then applies the production history tail.
    /// </summary>
    /// <param name="messages">The raw messages.</param>
    /// <param name="context">The completion context.</param>
    /// <returns>The converted prompts.</returns>
    public static List<SdkChatMessage> Legacy(
        IEnumerable<AIChatMessage> messages,
        AICompletionContext context)
    {
        var converted = new List<SdkChatMessage>();
        var currentPrompt = string.Empty;

        foreach (var message in messages)
        {
            if (message.Role == AIChatRole.User)
            {
                var userMessage = CreateUserMessageLegacy(message);

                if (userMessage != null)
                {
                    converted.Add(userMessage);
                    currentPrompt = message.Text;
                }
            }
            else if (message.Role == AIChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text))
            {
                converted.Add(new AssistantChatMessage(message.Text));
            }
        }

        _ = currentPrompt;

        return _getPrompts(context, converted);
    }

    /// <summary>
    /// Converts messages with the production bounded converter and applies system-message construction.
    /// </summary>
    /// <param name="messages">The raw messages.</param>
    /// <param name="context">The completion context.</param>
    /// <returns>The converted prompts.</returns>
    public static List<SdkChatMessage> Current(
        IEnumerable<AIChatMessage> messages,
        AICompletionContext context)
    {
        var converted = AzureOpenAIChatMessageConverter.Convert(
            messages,
            context.PastMessagesCount);

        return _getPrompts(context, converted);
    }

    /// <summary>
    /// Creates raw messages for the requested scale and distribution.
    /// </summary>
    /// <param name="messageCount">The total raw-message count.</param>
    /// <param name="profile">The raw-message distribution.</param>
    /// <param name="includeImages">Whether every tenth eligible message should carry a 256 KB image.</param>
    /// <returns>The benchmark messages.</returns>
    public static List<AIChatMessage> CreateMessages(
        int messageCount,
        AzureHistoryProfile profile,
        bool includeImages)
    {
        var messages = new List<AIChatMessage>(messageCount);
        var imageBytes = Enumerable
            .Range(0, ImageSize)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var eligibleIndex = 0;

        for (var index = 0; index < messageCount; index++)
        {
            if (profile == AzureHistoryProfile.SparseEligibility && index % 10 != 0)
            {
                messages.Add(CreateIneligibleMessage(index));

                continue;
            }

            messages.Add(CreateEligibleMessage(
                index,
                eligibleIndex,
                includeImages,
                imageBytes));
            eligibleIndex++;
        }

        return messages;
    }

    /// <summary>
    /// Verifies exact SDK message types, content ordering, text, media types, and image bytes.
    /// </summary>
    /// <param name="legacy">The captured production result.</param>
    /// <param name="candidate">The bounded-tail candidate result.</param>
    public static void EnsureEquivalent(
        List<SdkChatMessage> legacy,
        List<SdkChatMessage> candidate)
    {
        if (legacy.Count != candidate.Count)
        {
            throw new InvalidOperationException(
                $"Prompt implementations returned different counts: {legacy.Count} and {candidate.Count}.");
        }

        for (var messageIndex = 0; messageIndex < legacy.Count; messageIndex++)
        {
            var legacyMessage = legacy[messageIndex];
            var candidateMessage = candidate[messageIndex];

            if (legacyMessage.GetType() != candidateMessage.GetType() ||
                legacyMessage.Content.Count != candidateMessage.Content.Count)
            {
                throw new InvalidOperationException(
                    $"Prompt implementations returned different messages at index {messageIndex}.");
            }

            for (var contentIndex = 0; contentIndex < legacyMessage.Content.Count; contentIndex++)
            {
                var legacyContent = legacyMessage.Content[contentIndex];
                var candidateContent = candidateMessage.Content[contentIndex];

                if (legacyContent.Kind != candidateContent.Kind ||
                    legacyContent.Text != candidateContent.Text ||
                    legacyContent.ImageBytesMediaType != candidateContent.ImageBytesMediaType)
                {
                    throw new InvalidOperationException(
                        $"Prompt implementations returned different content at message {messageIndex}, part {contentIndex}.");
                }

                var legacyImage = legacyContent.ImageBytes;
                var candidateImage = candidateContent.ImageBytes;

                if ((legacyImage is null) != (candidateImage is null) ||
                    (legacyImage is not null &&
                        !legacyImage.ToMemory().Span.SequenceEqual(candidateImage.ToMemory().Span)))
                {
                    throw new InvalidOperationException(
                        $"Prompt implementations returned different image bytes at message {messageIndex}, part {contentIndex}.");
                }
            }
        }
    }

    /// <summary>
    /// Preserves the original production user-message conversion as the benchmark baseline.
    /// </summary>
    /// <param name="message">The raw user message.</param>
    /// <returns>The converted SDK user message, or <see langword="null"/>.</returns>
    private static UserChatMessage CreateUserMessageLegacy(AIChatMessage message)
    {
        if (message.Contents is not { Count: > 0 })
        {
            return string.IsNullOrWhiteSpace(message.Text)
                ? null
                : new UserChatMessage(message.Text);
        }

        var parts = new List<ChatMessageContentPart>();

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent textContent when !string.IsNullOrWhiteSpace(textContent.Text):
                    parts.Add(ChatMessageContentPart.CreateTextPart(textContent.Text));
                    break;
                case DataContent dataContent when dataContent.Data is { Length: > 0 } && !string.IsNullOrWhiteSpace(dataContent.MediaType):
                    parts.Add(ChatMessageContentPart.CreateImagePart(
                        BinaryData.FromBytes(dataContent.Data.ToArray()),
                        dataContent.MediaType));
                    break;
            }
        }

        if (parts.Count == 0)
        {
            return string.IsNullOrWhiteSpace(message.Text)
                ? null
                : new UserChatMessage(message.Text);
        }

        return new UserChatMessage(parts);
    }

    /// <summary>
    /// Creates an eligible text or image message.
    /// </summary>
    /// <param name="rawIndex">The raw source index.</param>
    /// <param name="eligibleIndex">The eligible-message index.</param>
    /// <param name="includeImages">Whether image messages are enabled.</param>
    /// <param name="imageBytes">The shared image source bytes.</param>
    /// <returns>The eligible message.</returns>
    private static AIChatMessage CreateEligibleMessage(
        int rawIndex,
        int eligibleIndex,
        bool includeImages,
        byte[] imageBytes)
    {
        if (includeImages && eligibleIndex % 10 == 0)
        {
            return new AIChatMessage(
                AIChatRole.User,
                [
                    new TextContent($"image-{rawIndex}"),
                    new DataContent(imageBytes, "image/png"),
                ]);
        }

        return new AIChatMessage(
            eligibleIndex % 2 == 0
                ? AIChatRole.User
                : AIChatRole.Assistant,
            $"message-{rawIndex}");
    }

    /// <summary>
    /// Creates one of the ineligible role or content shapes used by the sparse profile.
    /// </summary>
    /// <param name="index">The raw source index.</param>
    /// <returns>The ineligible message.</returns>
    private static AIChatMessage CreateIneligibleMessage(int index)
    {
        return (index % 10) switch
        {
            1 => new AIChatMessage(AIChatRole.System, $"system-{index}"),
            2 => new AIChatMessage(AIChatRole.Tool, $"tool-{index}"),
            3 => new AIChatMessage(new AIChatRole("observer"), $"unknown-{index}"),
            4 => new AIChatMessage(AIChatRole.User, (string)null),
            5 => new AIChatMessage(AIChatRole.Assistant, " \t"),
            6 => new AIChatMessage(AIChatRole.User, [new UnsupportedBenchmarkContent()]),
            7 => new AIChatMessage(
                AIChatRole.Assistant,
                [new DataContent(new byte[] { 1 }, "image/png")]),
            8 => new AIChatMessage(
                AIChatRole.User,
                [new DataContent(ReadOnlyMemory<byte>.Empty, "image/png")]),
            _ => new AIChatMessage(AIChatRole.User, string.Empty),
        };
    }

    /// <summary>
    /// Creates a strongly typed delegate for the production prompt-selection helper.
    /// </summary>
    /// <returns>The prompt-selection delegate.</returns>
    private static Func<AICompletionContext, List<SdkChatMessage>, List<SdkChatMessage>> CreateGetPromptsDelegate()
    {
        var method = typeof(AzureOpenAICompletionClient).GetMethod(
            "GetPrompts",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unable to find the prompt-selection helper.");

        return method.CreateDelegate<Func<AICompletionContext, List<SdkChatMessage>, List<SdkChatMessage>>>();
    }

    /// <summary>
    /// Represents unsupported content used by the sparse benchmark distribution.
    /// </summary>
    private sealed class UnsupportedBenchmarkContent : AIContent
    {
    }
}
