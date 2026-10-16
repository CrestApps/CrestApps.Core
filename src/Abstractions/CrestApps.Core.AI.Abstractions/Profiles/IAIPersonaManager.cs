using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Manages AI personas. Reads merge the saved personas with those of every <see cref="IAIPersonaProvider"/>, and a
/// saved persona replaces a provided one with the same name. Only saved personas can be updated or deleted.
/// </summary>
public interface IAIPersonaManager : INamedCatalogManager<AIPersona>
{
}
