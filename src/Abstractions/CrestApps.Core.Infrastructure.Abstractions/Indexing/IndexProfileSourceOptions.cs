namespace CrestApps.Core.Infrastructure.Indexing;

/// <summary>
/// Options that hold the registered collection of <see cref="IndexProfileSourceDescriptor"/> entries.
/// Register descriptors via <see cref="AddOrUpdate"/> during application startup to declare which
/// providers support which profile types.
/// </summary>
public sealed class IndexProfileSourceOptions
{
    /// <summary>
    /// Gets the list of registered index profile source descriptors.
    /// </summary>
    public List<IndexProfileSourceDescriptor> Sources { get; } = [];

    /// <summary>
    /// Adds or update.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="providerDisplayName">The provider display name.</param>
    /// <param name="type">The type.</param>
    /// <param name="configure">The action used to configure.</param>
    public void AddOrUpdate(
        string providerName,
        string providerDisplayName,
        string type,
        Action<IndexProfileSourceDescriptor> configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var descriptor = Sources.FirstOrDefault(source =>
            string.Equals(source.ProviderName, providerName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(source.Type, type, StringComparison.OrdinalIgnoreCase));

        descriptor ??= new IndexProfileSourceDescriptor
        {
            ProviderName = providerName,
            ProviderDisplayName = providerDisplayName,
            Type = type,
            DisplayName = type,
            Description = type,
        };

        configure?.Invoke(descriptor);

        if (!Sources.Contains(descriptor))
        {
            Sources.Add(descriptor);
        }
    }
}
