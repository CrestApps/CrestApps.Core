using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Represents a user-configured, model-invokable tool instance created from a registered
/// <see cref="IAIToolInstanceSource"/> blueprint. Unlike a plain <c>AITool</c> whose arguments are
/// always supplied by the model, a tool instance binds developer-defined behavior to user-provided
/// settings (endpoints, credentials, headers, etc.) captured up front. The AI model still decides when
/// to invoke the resulting function, but the user-provided settings are applied at invocation time.
/// </summary>
/// <remarks>
/// The <see cref="SourceCatalogEntry.Source"/> property holds the registered name of the owning tool
/// instance source, while <see cref="Name"/> is a unique technical name used to derive the function name
/// exposed to the AI model. Multiple instances may be created from the same source, each with different
/// settings and a distinct <see cref="Description"/> so the model can tell them apart.
/// </remarks>
public sealed class AIToolInstance : SourceCatalogEntry, INameAwareModel, IDisplayTextAwareModel, IModifiedUtcAwareModel, ICloneable<AIToolInstance>
{
    /// <summary>
    /// Gets or sets the unique technical name for this tool instance. This value is the basis for the
    /// function name exposed to the AI model, so it must be unique across all configured instances.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display text shown in management and selection surfaces.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the natural-language description presented to the AI model. This is the primary
    /// signal the model uses to distinguish between multiple instances built from the same source, so it
    /// should clearly explain what this specific instance does (for example, which API it calls).
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this instance was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this instance was last modified.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the display name of the user that authored this instance.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user that owns this instance.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Creates a shallow copy of this instance, sharing the same <see cref="ExtensibleEntity.Properties"/> reference.
    /// </summary>
    /// <returns>A new <see cref="AIToolInstance"/> with the same values.</returns>
    public AIToolInstance Clone()
    {
        return new AIToolInstance
        {
            ItemId = ItemId,
            Source = Source,
            Name = Name,
            DisplayText = DisplayText,
            Description = Description,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = Properties,
        };
    }
}
