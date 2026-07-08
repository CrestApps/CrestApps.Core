using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Holds the registered set of AI data source source descriptors.
/// </summary>
public sealed class AIDataSourceSourceOptions
{
    /// <summary>
    /// Gets the configured source descriptors.
    /// </summary>
    public List<AIDataSourceSourceDescriptor> Sources { get; } = [];

    /// <summary>
    /// Adds or updates a source descriptor.
    /// </summary>
    /// <param name="sourceType">The source type identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="description">The description.</param>
    public void AddOrUpdate(string sourceType, LocalizedString displayName, LocalizedString description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(description.Value);

        var descriptor = Sources.FirstOrDefault(source =>
            string.Equals(source.SourceType, sourceType, StringComparison.OrdinalIgnoreCase));

        descriptor ??= new AIDataSourceSourceDescriptor();
        descriptor.SourceType = sourceType;
        descriptor.DisplayName = displayName;
        descriptor.Description = description;

        if (!Sources.Contains(descriptor))
        {
            Sources.Add(descriptor);
        }
    }
}
