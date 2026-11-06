using CrestApps.Core.AI.Completions;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Reads and writes the <see cref="AIDeploymentPricing"/> stored on an <see cref="AIDeployment"/>.
/// </summary>
public static class AIDeploymentPricingExtensions
{
    /// <summary>
    /// Gets the deployment's pricing.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <returns>The pricing, or <see langword="null"/> when the deployment has none.</returns>
    public static AIDeploymentPricing GetPricing(this AIDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.TryGet<AIDeploymentPricing>(out var pricing) ? pricing : null;
    }

    /// <summary>
    /// Sets the deployment's pricing, keeping only the prices with a known meter, an amount, and a quantity, and
    /// removing the pricing when none is left.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="currency">The currency, such as <c>USD</c>; <c>USD</c> when empty.</param>
    /// <param name="prices">The prices.</param>
    public static void SetPricing(this AIDeployment deployment, string currency, IEnumerable<AIDeploymentPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        var kept = (prices ?? [])
            .Where(price => price is not null && price.Amount > 0 && price.Per > 0)
            .Select(price => (Price: price, Meter: AIUsageMeters.Find(price.Meter)))
            .Where(entry => entry.Meter is not null && (!entry.Meter.RequiresCountName || !string.IsNullOrWhiteSpace(entry.Price.CountName)))
            .Select(entry => new AIDeploymentPrice
            {
                Meter = entry.Meter.Name,
                Amount = entry.Price.Amount,
                Per = entry.Price.Per,
                CountName = entry.Meter.RequiresCountName ? entry.Price.CountName.Trim() : null,
                AboveInputTokens = entry.Price.AboveInputTokens is > 0 ? entry.Price.AboveInputTokens : null,
                Increment = Math.Max(0, entry.Price.Increment),
            })
            .ToList();

        if (kept.Count == 0)
        {
            deployment.Remove<AIDeploymentPricing>();

            return;
        }

        deployment.Put(new AIDeploymentPricing
        {
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant(),
            Prices = kept,
        });
    }
}
