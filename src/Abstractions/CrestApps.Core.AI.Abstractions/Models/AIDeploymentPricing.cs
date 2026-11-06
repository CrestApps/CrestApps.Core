namespace CrestApps.Core.AI.Models;

/// <summary>
/// What a deployment costs, stored on <see cref="AIDeployment"/>. Usage reports multiply each metered unit by these
/// prices to estimate the cost of every request sent to the deployment.
/// </summary>
/// <remarks>
/// A deployment can carry several prices, one per billed unit, which together describe how its provider bills it:
/// a chat model priced per million input, cached input, and output tokens; a transcription model priced per minute
/// of audio; or a model billed per request on top of its tokens.
/// </remarks>
public sealed class AIDeploymentPricing
{
    /// <summary>
    /// Gets or sets the currency the prices are in, such as <c>USD</c>.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Gets or sets the prices, one per billed unit.
    /// </summary>
    public List<AIDeploymentPrice> Prices { get; set; } = [];
}

/// <summary>
/// One price of a deployment: an amount for a quantity of one billed unit, such as 2.50 per million input tokens.
/// </summary>
public sealed class AIDeploymentPrice
{
    /// <summary>
    /// Gets or sets the billed unit, one of <see cref="Completions.AIUsageMeters"/>.
    /// </summary>
    public string Meter { get; set; }

    /// <summary>
    /// Gets or sets what <see cref="Per"/> units cost.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Gets or sets how many of the meter's base units <see cref="Amount"/> pays for, such as <c>1000000</c> for a
    /// price per million tokens or <c>60</c> for a price per minute of audio, which is metered in seconds.
    /// </summary>
    public long Per { get; set; } = 1;

    /// <summary>
    /// Gets or sets, for <see cref="Completions.AIUsageMeters.AdditionalCount"/>, the name of the count the provider
    /// reported, as it appears in <see cref="AICompletionUsageRecord.AdditionalCounts"/>.
    /// </summary>
    public string CountName { get; set; }

    /// <summary>
    /// Gets or sets an input size above which this price applies, for models with long-context pricing. A request
    /// whose input tokens exceed it pays this price instead of the meter's prices with a lower threshold, or none.
    /// </summary>
    public long? AboveInputTokens { get; set; }

    /// <summary>
    /// Gets or sets the increment, in the meter's base units, each request's quantity is rounded up to, such as
    /// <c>15</c> for audio billed in 15-second increments. Zero bills the exact quantity.
    /// </summary>
    public long Increment { get; set; }
}
