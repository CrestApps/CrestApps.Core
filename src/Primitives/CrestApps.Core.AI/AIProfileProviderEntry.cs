using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

/// <summary>
/// Represents the AI Profile Provider Entry.
/// </summary>
public sealed class AIProfileProviderEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileProviderEntry"/> class.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    public AIProfileProviderEntry(string providerName)
    {
        ProviderName = providerName;
    }

    /// <summary>
    /// Gets the provider Name.
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets or sets the display Name.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public LocalizedString Description { get; set; }
}
