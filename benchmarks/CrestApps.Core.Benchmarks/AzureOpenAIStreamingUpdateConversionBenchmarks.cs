using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured Azure streaming update conversion with the production converter. This
/// class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class AzureOpenAIStreamingUpdateConversionBenchmarks
{
    private StreamingChatCompletionUpdate[] _updates;

    /// <summary>
    /// Gets or sets the number of text content parts in each streaming update.
    /// </summary>
    [Params(1, 8, 64)]
    public int ContentPartCount { get; set; }

    /// <summary>
    /// Gets or sets the number of streaming updates converted per operation.
    /// </summary>
    [Params(1_000)]
    public int UpdateCount { get; set; }

    /// <summary>
    /// Creates stable streaming updates and verifies legacy/current equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _updates = CreateUpdates(UpdateCount, ContentPartCount);

        var legacy = Legacy();
        var current = Current();

        EnsureEquivalent(legacy, current);
    }

    /// <summary>
    /// Converts streaming update content with the legacy LINQ projection chain.
    /// </summary>
    /// <returns>The converted updates.</returns>
    [Benchmark(Baseline = true)]
    public ChatResponseUpdate[] Legacy()
    {
        var results = new ChatResponseUpdate[_updates.Length];

        for (var index = 0; index < _updates.Length; index++)
        {
            results[index] = CreateLegacyStreamingResponseUpdate(_updates[index]);
        }

        return results;
    }

    /// <summary>
    /// Converts streaming update content with the production converter.
    /// </summary>
    /// <returns>The converted updates.</returns>
    [Benchmark]
    public ChatResponseUpdate[] Current()
    {
        var results = new ChatResponseUpdate[_updates.Length];

        for (var index = 0; index < _updates.Length; index++)
        {
            results[index] = AzureOpenAICompletionClient.CreateStreamingResponseUpdate(_updates[index]);
        }

        return results;
    }

    /// <summary>
    /// Reproduces the previous production streaming update conversion.
    /// </summary>
    /// <param name="update">The Azure streaming update.</param>
    /// <returns>The converted framework update.</returns>
    private static ChatResponseUpdate CreateLegacyStreamingResponseUpdate(StreamingChatCompletionUpdate update)
    {
        var result = new ChatResponseUpdate
        {
            ResponseId = update.CompletionId,
            CreatedAt = update.CreatedAt,
            ModelId = update.Model,
            Contents = update.ContentUpdate.Select(x => new TextContent(x.Text)).Cast<AIContent>().ToList(),
        };

        if (update.FinishReason is not null)
        {
            result.FinishReason = new Microsoft.Extensions.AI.ChatFinishReason(update.FinishReason?.ToString());
        }

        if (update.Role is not null)
        {
            result.Role = new Microsoft.Extensions.AI.ChatRole(update.Role.ToString().ToLowerInvariant());
        }

        return result;
    }

    /// <summary>
    /// Creates benchmark streaming updates.
    /// </summary>
    /// <param name="updateCount">The number of updates.</param>
    /// <param name="contentPartCount">The number of content parts in each update.</param>
    /// <returns>The benchmark updates.</returns>
    private static StreamingChatCompletionUpdate[] CreateUpdates(
        int updateCount,
        int contentPartCount)
    {
        var updates = new StreamingChatCompletionUpdate[updateCount];
        var createdAt = new DateTimeOffset(2026, 7, 16, 1, 2, 3, TimeSpan.Zero);

        for (var updateIndex = 0; updateIndex < updateCount; updateIndex++)
        {
            var parts = new ChatMessageContentPart[contentPartCount];

            for (var partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                parts[partIndex] = ChatMessageContentPart.CreateTextPart($"update-{updateIndex}-part-{partIndex}");
            }

            updates[updateIndex] = OpenAIChatModelFactory.StreamingChatCompletionUpdate(
                completionId: $"completion-{updateIndex}",
                contentUpdate: new ChatMessageContent(parts),
                functionCallUpdate: null,
                toolCallUpdates: [],
                role: updateIndex % 2 == 0 ? ChatMessageRole.Assistant : null,
                refusalUpdate: null,
                contentTokenLogProbabilities: [],
                refusalTokenLogProbabilities: [],
                finishReason: updateIndex % 10 == 0 ? OpenAI.Chat.ChatFinishReason.Stop : null,
                createdAt: createdAt.AddMilliseconds(updateIndex),
                model: "model-1",
                systemFingerprint: "fingerprint-1",
                usage: null);
        }

        return updates;
    }

    /// <summary>
    /// Verifies exact converted update equivalence.
    /// </summary>
    /// <param name="legacy">The legacy converted updates.</param>
    /// <param name="current">The production converted updates.</param>
    private static void EnsureEquivalent(
        ChatResponseUpdate[] legacy,
        ChatResponseUpdate[] current)
    {
        if (legacy.Length != current.Length)
        {
            throw new InvalidOperationException("The streaming conversion implementations returned different counts.");
        }

        for (var updateIndex = 0; updateIndex < legacy.Length; updateIndex++)
        {
            var legacyUpdate = legacy[updateIndex];
            var currentUpdate = current[updateIndex];

            if (legacyUpdate.ResponseId != currentUpdate.ResponseId ||
                legacyUpdate.CreatedAt != currentUpdate.CreatedAt ||
                legacyUpdate.ModelId != currentUpdate.ModelId ||
                legacyUpdate.FinishReason?.ToString() != currentUpdate.FinishReason?.ToString() ||
                legacyUpdate.Role?.ToString() != currentUpdate.Role?.ToString() ||
                legacyUpdate.Contents.Count != currentUpdate.Contents.Count)
            {
                throw new InvalidOperationException($"Streaming update metadata differed at index {updateIndex}.");
            }

            for (var contentIndex = 0; contentIndex < legacyUpdate.Contents.Count; contentIndex++)
            {
                var legacyText = ((TextContent)legacyUpdate.Contents[contentIndex]).Text;
                var currentText = ((TextContent)currentUpdate.Contents[contentIndex]).Text;

                if (legacyText != currentText)
                {
                    throw new InvalidOperationException($"Streaming update content differed at update {updateIndex}, content {contentIndex}.");
                }
            }
        }
    }
}
