namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

/// <summary>
/// Describes one request capability that feature enforcement removes from every request sent to a
/// deployment, so a listing can name the loss instead of leaving it in the log.
/// </summary>
public sealed class AIDeploymentEnforcedLimitViewModel
{
    /// <summary>
    /// Gets or sets the technical name of the feature the deployment does not declare.
    /// </summary>
    public string FeatureName { get; set; }

    /// <summary>
    /// Gets or sets the short badge text.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// Gets or sets the sentence describing what the runtime does to the request.
    /// </summary>
    public string Description { get; set; }
}
