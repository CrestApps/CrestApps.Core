using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

/// <summary>
/// Represents the AI Provider Connection Options Entry.
/// </summary>
public sealed class AIProviderConnectionOptionsEntry
{
    public AIProviderConnectionOptionsEntry(string providerName)
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
