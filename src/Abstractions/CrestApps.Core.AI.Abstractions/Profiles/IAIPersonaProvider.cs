using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Provides personas that are not stored in the persona catalog, such as persona files shipped with an application.
/// A saved persona with the same name replaces a provided one.
/// </summary>
public interface IAIPersonaProvider
{
    /// <summary>
    /// Gets all personas from this provider.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<AIPersona>> GetPersonasAsync(CancellationToken cancellationToken = default);
}
