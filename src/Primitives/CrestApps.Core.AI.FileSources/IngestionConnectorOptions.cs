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

/// <summary>
/// The connectors an administrator may choose between.
/// </summary>
/// <remarks>
/// Connectors are resolved as keyed services, which cannot be enumerated. This is what lets a screen offer
/// the ones a host actually registered rather than a hard-coded list.
/// </remarks>
public sealed class IngestionConnectorOptions
{
    /// <summary>
    /// Gets the registered connectors.
    /// </summary>
    public List<IngestionConnectorDescriptor> Connectors { get; } = [];

    /// <summary>
    /// Adds or replaces one connector's presentation.
    /// </summary>
    /// <param name="name">The connector name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="description">The description.</param>
    public void AddOrUpdate(string name, LocalizedString displayName, LocalizedString description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var descriptor = Connectors.Find(connector => string.Equals(connector.Name, name, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
        {
            descriptor = new IngestionConnectorDescriptor();
            Connectors.Add(descriptor);
        }

        descriptor.Name = name;
        descriptor.DisplayName = displayName;
        descriptor.Description = description;
    }
}
