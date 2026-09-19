namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

/// <summary>
/// Pairs a registered model parameter with its position in the deployment's parameter list so a single
/// parameter card can be rendered in a partial while keeping its form-binding index.
/// </summary>
public sealed class AIDeploymentParameterCardViewModel
{
    /// <summary>
    /// Gets or sets the parameter to render.
    /// </summary>
    public AIDeploymentParameterViewModel Parameter { get; set; }

    /// <summary>
    /// Gets or sets the parameter's index within <see cref="AIDeploymentViewModel.ModelParameters"/>.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the card should be hidden on initial render because its
    /// required feature is not currently enabled.
    /// </summary>
    public bool Hidden { get; set; }
}
