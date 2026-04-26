using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

/// <summary>
/// Represents the AI Template Source Entry.
/// </summary>
public sealed class AITemplateSourceEntry
{
    /// <summary>
    /// Gets or sets the display Name.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public LocalizedString Description { get; set; }
}
