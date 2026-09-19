using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// How one registered connector is presented to whoever configures a file source.
/// </summary>
public sealed class IngestionConnectorDescriptor
{
    /// <summary>
    /// Gets or sets the connector name, which is stored as the file source's source.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public LocalizedString Description { get; set; }
}
