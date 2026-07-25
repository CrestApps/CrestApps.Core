using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Represents a user-configured catalog entry created from an <see cref="AIToolSource"/>.
/// Unlike a plain <c>AITool</c> whose arguments are always supplied by the model, a tool definition
/// binds developer-defined behavior to user-provided settings (endpoints, credentials, headers, etc.)
/// captured up front. The AI model still decides when to invoke the resulting function, but the
/// user-provided settings are applied at invocation time.
/// </summary>
/// <remarks>
/// The <see cref="SourceCatalogEntry.Source"/> property holds the registered name of the owning
/// <see cref="AIToolSource"/>. Multiple definitions may be created from the same source,
/// each with different settings and a distinct <see cref="Description"/> so the model can tell the
/// definitions apart.
/// </remarks>
public sealed class AIToolDefinition : SourceCatalogEntry, IDisplayTextAwareModel, IModifiedUtcAwareModel, ICloneable<AIToolDefinition>
{
    /// <summary>
    /// Gets or sets the human-readable display text shown in management and selection surfaces.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the natural-language description presented to the AI model. This is the primary
    /// signal the model uses to distinguish between multiple definitions built from the same source, so
    /// it should clearly explain what this specific definition does (for example, which API it calls).
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this definition was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this definition was last modified.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the display name of the user that authored this definition.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user that owns this definition.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Creates a shallow copy of this instance, sharing the same <see cref="ExtensibleEntity.Properties"/> reference.
    /// </summary>
    /// <returns>A new <see cref="AIToolDefinition"/> with the same values.</returns>
    public AIToolDefinition Clone()
    {
        return new AIToolDefinition
        {
            ItemId = ItemId,
            Source = Source,
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
