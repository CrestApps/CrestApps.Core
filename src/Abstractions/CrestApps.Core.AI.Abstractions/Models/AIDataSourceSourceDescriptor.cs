using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Describes a selectable AI data source source type.
/// </summary>
public sealed class AIDataSourceSourceDescriptor
{
    /// <summary>
    /// Gets or sets the technical source type identifier.
    /// </summary>
    public string SourceType { get; set; }

    /// <summary>
    /// Gets or sets the display name shown to operators.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the descriptive text shown to operators.
    /// </summary>
    public LocalizedString Description { get; set; }
}
