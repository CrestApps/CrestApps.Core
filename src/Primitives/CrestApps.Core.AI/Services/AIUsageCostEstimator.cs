using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Estimates what metered AI requests cost from the prices set on their deployments
/// (<see cref="AIDeploymentPricing"/>).
/// </summary>
public static class AIUsageCostEstimator
{
    /// <summary>
    /// Estimates what one metered request cost.
    /// </summary>
    /// <param name="record">The metered request.</param>
    /// <param name="pricing">The prices of the deployment the request was sent to.</param>
    /// <returns>The cost in <see cref="AIDeploymentPricing.Currency"/>, or <see langword="null"/> when the deployment
    /// has no price.</returns>
    public static decimal? Estimate(AICompletionUsageRecord record, AIDeploymentPricing pricing)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!HasPrices(pricing))
        {
            return null;
        }

        var prices = GetApplicablePrices(pricing, record.InputTokenCount);
        var cost = 0m;

        // Input and output tokens include the cached, cache-write, audio, and image tokens. Each of those is taken out
        // and billed at its own price only when the deployment has one; otherwise it stays in at the token price.
        var inputTokens = (long)record.InputTokenCount;
        var cachedAudioTokens = HasPrice(prices, AIUsageMeters.CachedInputAudioTokens) ? record.CachedInputAudioTokenCount : 0;

        cost += Charge(prices, AIUsageMeters.CachedInputAudioTokens, cachedAudioTokens);
        inputTokens -= cachedAudioTokens;

        if (HasPrice(prices, AIUsageMeters.InputAudioTokens))
        {
            var audioTokens = Math.Max(0, record.InputAudioTokenCount - cachedAudioTokens);
            cost += Charge(prices, AIUsageMeters.InputAudioTokens, audioTokens);
            inputTokens -= audioTokens;
        }

        if (HasPrice(prices, AIUsageMeters.InputImageTokens))
        {
            cost += Charge(prices, AIUsageMeters.InputImageTokens, record.InputImageTokenCount);
            inputTokens -= record.InputImageTokenCount;
        }

        if (HasPrice(prices, AIUsageMeters.CacheWriteInputTokens))
        {
            cost += Charge(prices, AIUsageMeters.CacheWriteInputTokens, record.CacheWriteInputTokenCount);
            inputTokens -= record.CacheWriteInputTokenCount;
        }

        if (HasPrice(prices, AIUsageMeters.CachedInputTokens))
        {
            var cachedTokens = Math.Max(0, record.CachedInputTokenCount - cachedAudioTokens);
            cost += Charge(prices, AIUsageMeters.CachedInputTokens, cachedTokens);
            inputTokens -= cachedTokens;
        }

        var outputTokens = (long)record.OutputTokenCount;

        if (HasPrice(prices, AIUsageMeters.OutputAudioTokens))
        {
            cost += Charge(prices, AIUsageMeters.OutputAudioTokens, record.OutputAudioTokenCount);
            outputTokens -= record.OutputAudioTokenCount;
        }

        if (HasPrice(prices, AIUsageMeters.OutputImageTokens))
        {
            cost += Charge(prices, AIUsageMeters.OutputImageTokens, record.OutputImageTokenCount);
            outputTokens -= record.OutputImageTokenCount;
        }

        cost += Charge(prices, AIUsageMeters.InputTokens, Math.Max(0, inputTokens));
        cost += Charge(prices, AIUsageMeters.OutputTokens, Math.Max(0, outputTokens));
        cost += Charge(prices, AIUsageMeters.Requests, 1);
        cost += Charge(prices, AIUsageMeters.AudioSeconds, record.AudioDurationMs / 1_000m);
        cost += Charge(prices, AIUsageMeters.Characters, record.CharacterCount);
        cost += Charge(prices, AIUsageMeters.Images, record.ImageCount);
        cost += Charge(prices, AIUsageMeters.WebSearches, record.WebSearchCount);
        cost += Charge(prices, AIUsageMeters.WebFetches, record.WebFetchCount);

        foreach (var price in prices)
        {
            if (price.Meter == AIUsageMeters.AdditionalCount &&
                record.AdditionalCounts?.TryGetValue(price.CountName, out var count) == true)
            {
                cost += Charge(price, count);
            }
        }

        return cost;
    }

    /// <summary>
    /// Estimates what metered requests cost together.
    /// </summary>
    /// <param name="records">The metered requests, all to deployments priced in the same currency.</param>
    /// <param name="pricingByDeployment">The pricing of each deployment by name.</param>
    /// <returns>The cost, or <see langword="null"/> when none of the requests' deployments has a price.</returns>
    public static decimal? Estimate(IEnumerable<AICompletionUsageRecord> records, IReadOnlyDictionary<string, AIDeploymentPricing> pricingByDeployment)
    {
        ArgumentNullException.ThrowIfNull(records);

        decimal? total = null;

        foreach (var record in records)
        {
            if (Estimate(record, GetPricing(pricingByDeployment, record)) is { } cost)
            {
                total = (total ?? 0) + cost;
            }
        }

        return total;
    }

    /// <summary>
    /// Gets the pricing of the deployment a metered request was sent to.
    /// </summary>
    /// <param name="pricingByDeployment">The pricing of each deployment by name, or <see langword="null"/>.</param>
    /// <param name="record">The metered request.</param>
    /// <returns>The pricing, or <see langword="null"/> when the deployment has none.</returns>
    public static AIDeploymentPricing GetPricing(IReadOnlyDictionary<string, AIDeploymentPricing> pricingByDeployment, AICompletionUsageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return pricingByDeployment is not null &&
            !string.IsNullOrEmpty(record.DeploymentName) &&
            pricingByDeployment.TryGetValue(record.DeploymentName, out var pricing)
                ? pricing
                : null;
    }

    /// <summary>
    /// Gets a value indicating whether a deployment's pricing has any price.
    /// </summary>
    /// <param name="pricing">The pricing.</param>
    public static bool HasPrices(AIDeploymentPricing pricing)
        => pricing?.Prices is { Count: > 0 } prices && prices.Any(IsValid);

    /// <summary>
    /// Loads the pricing of every deployment that has a price, keyed by deployment name, to estimate the cost of
    /// metered requests by their <see cref="AICompletionUsageRecord.DeploymentName"/>.
    /// </summary>
    /// <param name="deploymentStore">The deployment store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<IReadOnlyDictionary<string, AIDeploymentPricing>> GetPricingByDeploymentAsync(this IAIDeploymentStore deploymentStore, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deploymentStore);

        var pricing = new Dictionary<string, AIDeploymentPricing>(StringComparer.OrdinalIgnoreCase);

        foreach (var deployment in await deploymentStore.GetAllAsync(cancellationToken))
        {
            if (!string.IsNullOrEmpty(deployment.Name) &&
                deployment.TryGet<AIDeploymentPricing>(out var deploymentPricing) &&
                HasPrices(deploymentPricing))
            {
                pricing.TryAdd(deployment.Name, deploymentPricing);
            }
        }

        return pricing;
    }

    /// <summary>
    /// Gets the prices that apply to a request of the given input size. For each meter (and count name), only the prices
    /// with the highest long-context threshold the request exceeds apply, so a long-context price replaces the regular
    /// one; prices at the same threshold add up.
    /// </summary>
    private static List<AIDeploymentPrice> GetApplicablePrices(AIDeploymentPricing pricing, long inputTokens)
    {
        var applicable = new List<AIDeploymentPrice>();

        foreach (var group in pricing.Prices
            .Where(IsValid)
            .Where(price => price.AboveInputTokens is not > 0 || inputTokens > price.AboveInputTokens)
            .GroupBy(price => (Meter: AIUsageMeters.Find(price.Meter).Name, CountName: price.CountName ?? string.Empty)))
        {
            var threshold = group.Max(price => price.AboveInputTokens ?? 0);

            applicable.AddRange(group
                .Where(price => (price.AboveInputTokens ?? 0) == threshold)
                .Select(price => price.Meter == group.Key.Meter ? price : new AIDeploymentPrice
                {
                    Meter = group.Key.Meter,
                    Amount = price.Amount,
                    Per = price.Per,
                    CountName = price.CountName,
                    AboveInputTokens = price.AboveInputTokens,
                    Increment = price.Increment,
                }));
        }

        return applicable;
    }

    private static bool IsValid(AIDeploymentPrice price)
        => price is not null &&
            price.Amount > 0 &&
            price.Per > 0 &&
            AIUsageMeters.Find(price.Meter) is { } meter &&
            (!meter.RequiresCountName || !string.IsNullOrEmpty(price.CountName));

    private static bool HasPrice(List<AIDeploymentPrice> prices, string meter)
        => prices.Any(price => price.Meter == meter);

    private static decimal Charge(List<AIDeploymentPrice> prices, string meter, decimal quantity)
    {
        var cost = 0m;

        foreach (var price in prices)
        {
            if (price.Meter == meter)
            {
                cost += Charge(price, quantity);
            }
        }

        return cost;
    }

    /// <summary>
    /// Charges one price for a quantity, rounded up to the price's billing increment.
    /// </summary>
    private static decimal Charge(AIDeploymentPrice price, decimal quantity)
    {
        if (quantity <= 0)
        {
            return 0;
        }

        if (price.Increment > 0)
        {
            quantity = Math.Ceiling(quantity / price.Increment) * price.Increment;
        }

        return quantity * price.Amount / price.Per;
    }
}
