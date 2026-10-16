using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Manages AI personas. Reads merge the saved personas with those of every <see cref="IAIPersonaProvider"/>, and a
/// saved persona replaces a provided one with the same name.
/// </summary>
public sealed class DefaultAIPersonaManager : NamedCatalogManager<AIPersona>, IAIPersonaManager
{
    private readonly IEnumerable<IAIPersonaProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIPersonaManager"/> class.
    /// </summary>
    /// <param name="catalog">The catalog of saved personas.</param>
    /// <param name="handlers">The handlers notified as personas change.</param>
    /// <param name="providers">The providers of personas that are not saved, such as persona files.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIPersonaManager(
        INamedCatalog<AIPersona> catalog,
        IEnumerable<ICatalogEntryHandler<AIPersona>> handlers,
        IEnumerable<IAIPersonaProvider> providers,
        ILogger<DefaultAIPersonaManager> logger)
        : base(catalog, handlers, logger)
    {
        _providers = providers;
    }

    /// <summary>
    /// Gets every persona, saved and provided.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<IEnumerable<AIPersona>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var personas = new List<AIPersona>(await base.GetAllAsync(cancellationToken));
        var names = new HashSet<string>(personas.Select(persona => persona.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            foreach (var persona in await provider.GetPersonasAsync(cancellationToken))
            {
                if (names.Add(persona.Name))
                {
                    personas.Add(persona);
                }
            }
        }

        return personas;
    }

    /// <summary>
    /// Finds a persona by its identifier, saved first, then provided.
    /// </summary>
    /// <param name="id">The persona identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<AIPersona> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var persona = await base.FindByIdAsync(id, cancellationToken);

        return persona ?? await FindProvidedAsync(persona => string.Equals(persona.ItemId, id, StringComparison.OrdinalIgnoreCase), cancellationToken);
    }

    /// <summary>
    /// Finds a persona by its technical name, saved first, then provided.
    /// </summary>
    /// <param name="name">The persona's technical name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<AIPersona> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var persona = await base.FindByNameAsync(name, cancellationToken);

        return persona ?? await FindProvidedAsync(persona => string.Equals(persona.Name, name, StringComparison.OrdinalIgnoreCase), cancellationToken);
    }

    private async Task<AIPersona> FindProvidedAsync(Func<AIPersona, bool> predicate, CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            var persona = (await provider.GetPersonasAsync(cancellationToken)).FirstOrDefault(predicate);

            if (persona is not null)
            {
                return persona;
            }
        }

        return null;
    }
}
