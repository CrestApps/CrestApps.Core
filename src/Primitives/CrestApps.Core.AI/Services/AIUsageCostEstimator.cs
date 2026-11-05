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

        var cachedPrice = UnitPrice(pricing, AIUsageMeters.CachedInputTokens);
        var inputAudioPrice = UnitPrice(pricing, AIUsageMeters.InputAudioTokens);
        var outputAudioPrice = UnitPrice(pricing, AIUsageMeters.OutputAudioTokens);

        // Input and output tokens include the cached and audio tokens. Those are billed at their own price only when
        // the deployment has one; otherwise they stay in, at the token price.
        var inputTokens = (decimal)record.InputTokenCount;
        var outputTokens = (decimal)record.OutputTokenCount;
        var cost = 0m;

        if (cachedPrice is not null)
        {
            inputTokens -= record.CachedInputTokenCount;
            cost += record.CachedInputTokenCount * cachedPrice.Value;
        }

        if (inputAudioPrice is not null)
        {
            inputTokens -= record.InputAudioTokenCount;
            cost += record.InputAudioTokenCount * inputAudioPrice.Value;
        }

        if (outputAudioPrice is not null)
        {
            outputTokens -= record.OutputAudioTokenCount;
            cost += record.OutputAudioTokenCount * outputAudioPrice.Value;
        }

        cost += Math.Max(0, inputTokens) * (UnitPrice(pricing, AIUsageMeters.InputTokens) ?? 0);
        cost += Math.Max(0, outputTokens) * (UnitPrice(pricing, AIUsageMeters.OutputTokens) ?? 0);
        cost += UnitPrice(pricing, AIUsageMeters.Requests) ?? 0;
        cost += record.AudioDurationMs / 1_000m * (UnitPrice(pricing, AIUsageMeters.AudioSeconds) ?? 0);
        cost += record.CharacterCount * (UnitPrice(pricing, AIUsageMeters.Characters) ?? 0);
        cost += record.ImageCount * (UnitPrice(pricing, AIUsageMeters.Images) ?? 0);

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
        => pricing?.Prices is { Count: > 0 } prices && prices.Any(price => price.Amount > 0 && price.Per > 0 && AIUsageMeters.Find(price.Meter) is not null);

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
    /// Gets the price of one base unit of a meter, summing the deployment's prices for it, or <see langword="null"/>
    /// when the deployment has no price for it.
    /// </summary>
    private static decimal? UnitPrice(AIDeploymentPricing pricing, string meter)
    {
        decimal? total = null;

        foreach (var price in pricing.Prices)
        {
            if (price.Amount > 0 && price.Per > 0 && string.Equals(price.Meter, meter, StringComparison.OrdinalIgnoreCase))
            {
                total = (total ?? 0) + price.Amount / price.Per;
            }
        }

        return total;
    }
}
