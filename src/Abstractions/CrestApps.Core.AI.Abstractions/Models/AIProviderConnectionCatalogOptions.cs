namespace CrestApps.Core.AI.Models;

/// <summary>
/// Options that specify the configuration section names from which AI provider connections
/// and provider definitions are loaded.
/// </summary>
public sealed class AIProviderConnectionCatalogOptions
{
    /// <summary>
    /// Gets the ordered list of configuration section names that are scanned for AI provider connection definitions.
    /// </summary>
    public IList<string> ConnectionSections { get; } =
    [
        "CrestApps:AI:Connections",
    ];

    /// <summary>
    /// Gets the ordered list of configuration section names that are scanned for AI provider definitions.
    /// </summary>
    public IList<string> ProviderSections { get; } =
    [
        "CrestApps:AI:Providers",
    ];
}
