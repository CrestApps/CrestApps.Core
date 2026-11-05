using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// The cost of metered requests is estimated from the prices set on their deployments.
/// </summary>
public sealed class AIUsageCostEstimatorTests
{
    [Fact]
    public void Estimate_WithoutPrices_ReturnsNull()
    {
        var record = new AICompletionUsageRecord { InputTokenCount = 1_000 };

        Assert.Null(AIUsageCostEstimator.Estimate(record, null));
        Assert.Null(AIUsageCostEstimator.Estimate(record, new AIDeploymentPricing()));
    }

    [Fact]
    public void Estimate_PricesInputAndOutputTokensPerMillion()
    {
        var pricing = Pricing(
            Price(AIUsageMeters.InputTokens, 2.50m, 1_000_000),
            Price(AIUsageMeters.OutputTokens, 10m, 1_000_000));
        var record = new AICompletionUsageRecord { InputTokenCount = 200_000, OutputTokenCount = 50_000 };

        Assert.Equal(1.00m, AIUsageCostEstimator.Estimate(record, pricing));
    }

    [Fact]
    public void Estimate_TakesCachedTokensOutOfInput_OnlyWhenTheyHaveAPrice()
    {
        var record = new AICompletionUsageRecord { InputTokenCount = 1_000_000, CachedInputTokenCount = 400_000 };

        var withoutCachedPrice = Pricing(Price(AIUsageMeters.InputTokens, 2m, 1_000_000));
        var withCachedPrice = Pricing(
            Price(AIUsageMeters.InputTokens, 2m, 1_000_000),
            Price(AIUsageMeters.CachedInputTokens, 0.5m, 1_000_000));

        Assert.Equal(2m, AIUsageCostEstimator.Estimate(record, withoutCachedPrice));
        Assert.Equal(1.2m + 0.2m, AIUsageCostEstimator.Estimate(record, withCachedPrice));
    }

    [Fact]
    public void Estimate_PricesAudioTokensApartFromTextTokens()
    {
        var pricing = Pricing(
            Price(AIUsageMeters.InputTokens, 4m, 1_000_000),
            Price(AIUsageMeters.OutputTokens, 16m, 1_000_000),
            Price(AIUsageMeters.InputAudioTokens, 32m, 1_000_000),
            Price(AIUsageMeters.OutputAudioTokens, 64m, 1_000_000));
        var record = new AICompletionUsageRecord
        {
            InputTokenCount = 1_000,
            InputAudioTokenCount = 800,
            OutputTokenCount = 2_000,
            OutputAudioTokenCount = 1_500,
        };

        // 200 text in at 4, 800 audio in at 32, 500 text out at 16, 1,500 audio out at 64, per million.
        var expected = (200 * 4m + 800 * 32m + 500 * 16m + 1_500 * 64m) / 1_000_000m;

        Assert.Equal(expected, AIUsageCostEstimator.Estimate(record, pricing));
    }

    [Fact]
    public void Estimate_PricesAudioDurationCharactersImagesAndRequests()
    {
        var pricing = Pricing(
            Price(AIUsageMeters.AudioSeconds, 0.36m, 3_600),
            Price(AIUsageMeters.Characters, 15m, 1_000_000),
            Price(AIUsageMeters.Images, 0.04m, 1),
            Price(AIUsageMeters.Requests, 0.01m, 1));
        var record = new AICompletionUsageRecord
        {
            AudioDurationMs = 600_000,
            CharacterCount = 100_000,
            ImageCount = 2,
        };

        Assert.Equal(0.06m + 1.5m + 0.08m + 0.01m, AIUsageCostEstimator.Estimate(record, pricing));
    }

    [Fact]
    public void Estimate_AddsUpSeveralPricesForTheSameMeter()
    {
        var pricing = Pricing(
            Price(AIUsageMeters.Requests, 0.01m, 1),
            Price(AIUsageMeters.Requests, 5m, 1_000));

        Assert.Equal(0.015m, AIUsageCostEstimator.Estimate(new AICompletionUsageRecord(), pricing));
    }

    [Fact]
    public void Build_AddsTheEstimatedCostAndCurrencyToEachRow()
    {
        var pricing = new Dictionary<string, AIDeploymentPricing>(StringComparer.OrdinalIgnoreCase)
        {
            ["chat"] = Pricing(Price(AIUsageMeters.InputTokens, 1m, 1_000)),
        };
        var records = new[]
        {
            new AICompletionUsageRecord { ClientName = "OpenAI", DeploymentName = "chat", ModelName = "gpt", InputTokenCount = 1_000, TotalTokenCount = 1_000 },
            new AICompletionUsageRecord { ClientName = "OpenAI", DeploymentName = "chat", ModelName = "gpt", InputTokenCount = 2_000, TotalTokenCount = 2_000 },
            new AICompletionUsageRecord { ClientName = "OpenAI", DeploymentName = "other", ModelName = "gpt", InputTokenCount = 5_000, TotalTokenCount = 5_000 },
        };

        var rows = AIUsageReport.Build(records, AIUsageReportGrouping.Model, pricing);

        Assert.Equal(2, rows.Count);

        var priced = Assert.Single(rows, row => row.Currency == "USD");
        Assert.Equal(2, priced.RequestCount);
        Assert.Equal(3m, priced.EstimatedCost);

        var unpriced = Assert.Single(rows, row => row.Currency is null);
        Assert.Null(unpriced.EstimatedCost);
    }

    [Fact]
    public void SetPricing_KeepsOnlyCompletePrices_AndRemovesEmptyPricing()
    {
        var deployment = new AIDeployment();

        deployment.SetPricing("eur", [Price("inputtokens", 1m, 1_000_000), Price("Unknown", 1m, 1), Price(AIUsageMeters.OutputTokens, 0m, 1_000_000)]);

        var pricing = deployment.GetPricing();
        Assert.NotNull(pricing);
        Assert.Equal("EUR", pricing.Currency);
        var price = Assert.Single(pricing.Prices);
        Assert.Equal(AIUsageMeters.InputTokens, price.Meter);

        deployment.SetPricing("USD", []);

        Assert.Null(deployment.GetPricing());
    }

    private static AIDeploymentPricing Pricing(params AIDeploymentPrice[] prices)
        => new() { Currency = "USD", Prices = [.. prices] };

    private static AIDeploymentPrice Price(string meter, decimal amount, long per)
        => new() { Meter = meter, Amount = amount, Per = per };
}
