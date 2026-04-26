namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the default AI Options.
/// </summary>
public sealed class DefaultAIOptions
{
    /// <summary>
    /// Gets or sets the temperature.
    /// </summary>
    public float? Temperature { get; set; } = 0;

    /// <summary>
    /// Gets or sets the max Output Tokens.
    /// </summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Gets or sets the top P.
    /// </summary>
    public float? TopP { get; set; } = 1;

    /// <summary>
    /// Gets or sets the frequency Penalty.
    /// </summary>
    public float? FrequencyPenalty { get; set; } = 0;

    /// <summary>
    /// Gets or sets the presence Penalty.
    /// </summary>
    public float? PresencePenalty { get; set; } = 0;

    /// <summary>
    /// Gets or sets the past Messages Count.
    /// </summary>
    public int PastMessagesCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets the maximum Iterations Per Request.
    /// </summary>
    public int MaximumIterationsPerRequest { get; set; } = 10;

    /// <summary>
    /// Gets or sets the absolute Maximum Iterations Per Request.
    /// </summary>
    public int AbsoluteMaximumIterationsPerRequest { get; set; } = 100;

    /// <summary>
    /// Gets or sets a value indicating whether enables Open Telemetry.
    /// </summary>
    public bool EnableOpenTelemetry { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enables Distributed Caching.
    /// </summary>
    public bool EnableDistributedCaching { get; set; } = true;

    public DefaultAIOptions Normalize()
    {
        if (AbsoluteMaximumIterationsPerRequest <= 0)
        {
            AbsoluteMaximumIterationsPerRequest = 100;
        }

        if (MaximumIterationsPerRequest <= 0)
        {
            MaximumIterationsPerRequest = 10;
        }

        MaximumIterationsPerRequest = Math.Min(MaximumIterationsPerRequest, AbsoluteMaximumIterationsPerRequest);

        return this;
    }

    public DefaultAIOptions ApplySiteOverrides(GeneralAIOptions settings)
    {
        var options = new DefaultAIOptions
        {
            Temperature = Temperature,
            MaxOutputTokens = MaxOutputTokens,
            TopP = TopP,
            FrequencyPenalty = FrequencyPenalty,
            PresencePenalty = PresencePenalty,
            PastMessagesCount = PastMessagesCount,
            MaximumIterationsPerRequest = MaximumIterationsPerRequest,
            AbsoluteMaximumIterationsPerRequest = AbsoluteMaximumIterationsPerRequest,
            EnableOpenTelemetry = EnableOpenTelemetry,
            EnableDistributedCaching = EnableDistributedCaching,
        }.Normalize();

        if (settings is null)
        {
            return options;
        }

        if (settings.OverrideMaximumIterationsPerRequest)
        {
            options.MaximumIterationsPerRequest = settings.MaximumIterationsPerRequest;
        }

        if (settings.OverrideEnableDistributedCaching)
        {
            options.EnableDistributedCaching = settings.EnableDistributedCaching;
        }

        if (settings.OverrideEnableOpenTelemetry)
        {
            options.EnableOpenTelemetry = settings.EnableOpenTelemetry;
        }

        return options.Normalize();
    }
}
