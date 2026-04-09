namespace CrestApps.Core.AI.Models;

public sealed class AIProviderConnectionCatalogOptions
{
    public IList<string> ConnectionSections { get; } =
    [
        "CrestApps:AI:Connections",
    ];

    public IList<string> ProviderSections { get; } =
    [
        "CrestApps:Providers",
    ];
}
