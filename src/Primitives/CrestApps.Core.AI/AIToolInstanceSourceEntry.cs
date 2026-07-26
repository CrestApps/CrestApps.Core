using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

/// <summary>
/// Describes the display metadata for a registered AI tool instance source. Instances of this entry are
/// stored in <see cref="AIOptions.ToolInstanceSources"/> keyed by the source name and drive how the
/// source is presented when users create configured <c>AIToolInstance</c> entries.
/// </summary>
public sealed class AIToolInstanceSourceEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolInstanceSourceEntry"/> class.
    /// </summary>
    /// <param name="source">The unique registered name of the tool instance source.</param>
    public AIToolInstanceSourceEntry(string source)
    {
        Source = source;
    }

    /// <summary>
    /// Gets the unique registered name of the tool instance source. This value is stored as the
    /// <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/> of every instance created from it.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets or sets the friendly display name shown when choosing this source to configure a new instance.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description that explains what kinds of instances this source produces.
    /// </summary>
    public LocalizedString Description { get; set; }

    /// <summary>
    /// Gets or sets an optional category used to group sources in the management UI.
    /// </summary>
    public LocalizedString Category { get; set; }
}
