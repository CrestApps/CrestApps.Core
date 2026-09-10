using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Describes a deployment slot: a named role this installation uses a deployment for, the capability a
/// deployment must declare to qualify for it, and where its site-wide default is configured.
/// </summary>
public sealed class AIDeploymentSlotDescriptor
{
    /// <summary>
    /// Gets or sets the technical name of the slot. See <see cref="AIDeploymentSlotNames"/>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the display text shown to operators.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the technical name of the model feature a deployment must declare to qualify for this
    /// slot. See <see cref="AIDeploymentFeatureNames"/>. When empty, every deployment qualifies.
    /// </summary>
    public string RequiredFeature { get; set; }

    /// <summary>
    /// Gets or sets the technical name of a model feature that disqualifies a deployment from this slot,
    /// even when the deployment also declares <see cref="RequiredFeature"/>.
    /// </summary>
    /// <remarks>
    /// The chat and utility slots exclude <see cref="AIDeploymentFeatureNames.Realtime"/>. A realtime
    /// deployment serves only the realtime WebSocket API and answers a text chat completion with an
    /// HTTP 400, so it cannot fill a text slot even when an operator has also ticked text generation for
    /// it. This is the rule that used to live in <c>AIDeployment.CanServeTextCompletion()</c> and be
    /// re-checked defensively at each call site.
    /// </remarks>
    public string ExcludedFeature { get; set; }

    /// <summary>
    /// Gets or sets the accessor that reads this slot's site-wide default deployment name from
    /// <see cref="DefaultAIDeploymentSettings"/>.
    /// </summary>
    public Func<DefaultAIDeploymentSettings, string> GetDefaultDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the technical name of the slot to continue with when this slot resolves to nothing.
    /// </summary>
    /// <remarks>
    /// Resolution is a single ordered chain, not two independent resolves. The utility slot falls back to
    /// the chat slot so that background work runs on the profile's configured chat deployment when no
    /// utility deployment is configured, rather than on an arbitrary text-capable model. "First capable" is
    /// evaluated exactly once, after the whole chain of explicit and site-default names is exhausted.
    /// </remarks>
    public string FallbackSlotName { get; set; }

    /// <summary>
    /// Gets or sets whether a deployment that declares no capability metadata at all qualifies for this
    /// slot.
    /// </summary>
    /// <remarks>
    /// This mirrors the asymmetry the capability system already has.
    /// <see cref="AIDeploymentFeatureNames.TextGeneration"/> is opt-out, so an unconstrained deployment is
    /// assumed to be text capable and this is <see langword="true"/> for the chat and utility slots. Every
    /// other feature is opt-in and must be declared.
    /// </remarks>
    public bool AllowUnconstrained { get; set; }

    /// <summary>
    /// Gets or sets the display order of the slot.
    /// </summary>
    public int Order { get; set; }
}
