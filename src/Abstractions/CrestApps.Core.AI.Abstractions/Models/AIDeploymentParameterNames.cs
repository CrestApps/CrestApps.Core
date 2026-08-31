namespace CrestApps.Core.AI.Models;

/// <summary>
/// Well-known technical names of the model parameters registered by the framework.
/// Modules can register additional parameters using <see cref="AIDeploymentCapabilityOptions.AddParameter"/>.
/// </summary>
public static class AIDeploymentParameterNames
{
    /// <summary>
    /// Controls how much internal reasoning the model applies before answering.
    /// </summary>
    public const string ReasoningEffort = "reasoningEffort";

    /// <summary>
    /// Controls how verbose the produced answer should be.
    /// </summary>
    public const string Verbosity = "verbosity";

    /// <summary>
    /// The deterministic sampling seed used to make responses reproducible.
    /// </summary>
    public const string Seed = "seed";
}
