using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// How one registered connector is presented to whoever configures an indexer.
/// </summary>
public sealed class IngestionConnectorDescriptor
{
    /// <summary>
    /// Gets or sets the connector name, which is stored as the indexer's source.
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

    /// <summary>
    /// Gets the names this connector was registered under before it was renamed.
    /// </summary>
    /// <remarks>
    /// A record stores its connector's name, so renaming a connector leaves records naming the old one.
    /// Listing the old name here keeps those records resolvable and keeps them on the screen that manages
    /// them, rather than stranding them as belonging to no connector at all.
    /// </remarks>
    public IList<string> Aliases { get; } = [];

    /// <summary>
    /// Determines whether a stored source names this connector, under either its current name or one it
    /// was registered under before.
    /// </summary>
    /// <param name="source">The stored source.</param>
    /// <returns><c>true</c> when the source names this connector; otherwise, <c>false</c>.</returns>
    public bool Matches(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (string.Equals(Name, source, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var alias in Aliases)
        {
            if (string.Equals(alias, source, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
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
        AddOrUpdate(name, displayName, description, aliases: null);
    }

    /// <summary>
    /// Adds or replaces one connector's presentation, along with the names it was registered under before.
    /// </summary>
    /// <param name="name">The connector name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="description">The description.</param>
    /// <param name="aliases">The names this connector was registered under before it was renamed.</param>
    public void AddOrUpdate(string name, LocalizedString displayName, LocalizedString description, IEnumerable<string> aliases)
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
        descriptor.Aliases.Clear();

        if (aliases is not null)
        {
            foreach (var alias in aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    descriptor.Aliases.Add(alias);
                }
            }
        }
    }

    /// <summary>
    /// Finds the connector a stored source names, under either its current name or one it was registered
    /// under before.
    /// </summary>
    /// <param name="source">The stored source.</param>
    /// <returns>The descriptor, or <see langword="null"/> when no registered connector claims the source.</returns>
    public IngestionConnectorDescriptor Find(string source)
    {
        return string.IsNullOrWhiteSpace(source)
            ? null
            : Connectors.Find(connector => connector.Matches(source));
    }
}
