using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Every count a provider reports is kept on the usage record: the framework's own keys, the names the OpenAI and
/// Anthropic adapters use, and any other count by its own name.
/// </summary>
public sealed class AIUsageCaptureTests
{
    [Fact]
    public void ApplyUsage_ReadsTheAnthropicAdapterCounts()
    {
        // The Anthropic adapter folds cache reads and writes into the input tokens and reports the writes and its
        // server-side tools as additional counts.
        var record = Apply(AIUsageOperationTypes.Chat, new UsageDetails
        {
            InputTokenCount = 1_500,
            OutputTokenCount = 200,
            CachedInputTokenCount = 1_000,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheCreationInputTokens"] = 400,
                ["WebSearchRequests"] = 3,
                ["WebFetchRequests"] = 2,
            },
        });

        Assert.Equal(1_000, record.CachedInputTokenCount);
        Assert.Equal(400, record.CacheWriteInputTokenCount);
        Assert.Equal(3, record.WebSearchCount);
        Assert.Equal(2, record.WebFetchCount);
        Assert.Null(record.AdditionalCounts);
    }

    [Fact]
    public void ApplyUsage_ReadsTheOpenAIAdapterCounts()
    {
        // The Microsoft.Extensions.AI OpenAI chat adapter reports audio and predicted-output tokens only as
        // additional counts.
        var record = Apply(AIUsageOperationTypes.Chat, new UsageDetails
        {
            InputTokenCount = 500,
            OutputTokenCount = 300,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["InputTokenDetails.AudioTokenCount"] = 120,
                ["OutputTokenDetails.AudioTokenCount"] = 90,
                ["OutputTokenDetails.AcceptedPredictionTokenCount"] = 40,
                ["OutputTokenDetails.RejectedPredictionTokenCount"] = 10,
                ["InputTokenDetails.ImageTokenCount"] = 70,
                ["InputTokenDetails.TextTokenCount"] = 310,
            },
        });

        Assert.Equal(120, record.InputAudioTokenCount);
        Assert.Equal(90, record.OutputAudioTokenCount);
        Assert.Equal(40, record.AcceptedPredictionTokenCount);
        Assert.Equal(10, record.RejectedPredictionTokenCount);
        Assert.Equal(70, record.InputImageTokenCount);
        Assert.Null(record.AdditionalCounts);
    }

    [Fact]
    public void ApplyUsage_KeepsAnUnknownCountByItsName()
    {
        var record = Apply(AIUsageOperationTypes.Chat, new UsageDetails
        {
            AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["CodeExecutionSeconds"] = 12, ["Ignored"] = 0 },
        });

        var count = Assert.Single(record.AdditionalCounts);
        Assert.Equal("CodeExecutionSeconds", count.Key);
        Assert.Equal(12, count.Value);
    }

    [Fact]
    public void ApplyUsage_TreatsTextToSpeechOutputTokensAsAudio_AndImageOutputTokensAsImage()
    {
        var speech = Apply(AIUsageOperationTypes.TextToSpeech, new UsageDetails { InputTokenCount = 50, OutputTokenCount = 800 });
        var image = Apply(AIUsageOperationTypes.Image, new UsageDetails { InputTokenCount = 30, OutputTokenCount = 4_160 });

        Assert.Equal(800, speech.OutputAudioTokenCount);
        Assert.Equal(4_160, image.OutputImageTokenCount);
    }

    [Fact]
    public void Build_SumsTheNewCountsAndOtherCountsPerRow()
    {
        var records = new[]
        {
            new AICompletionUsageRecord { ModelName = "claude", CacheWriteInputTokenCount = 100, WebSearchCount = 1, AdditionalCounts = new() { ["X"] = 2 } },
            new AICompletionUsageRecord { ModelName = "claude", CacheWriteInputTokenCount = 50, WebSearchCount = 2, AdditionalCounts = new() { ["X"] = 3 } },
        };

        var row = Assert.Single(AIUsageReport.Build(records, AIUsageReportGrouping.Model));

        Assert.Equal(150, row.CacheWriteInputTokenCount);
        Assert.Equal(3, row.WebSearchCount);
        Assert.Equal(5, row.AdditionalCounts["X"]);
    }

    private static AICompletionUsageRecord Apply(string operationType, UsageDetails usage)
        => AICompletionUsageRecordFactory.Create(null, operationType, "client", null, "deployment", "model", null, usage, 0, false);
}
