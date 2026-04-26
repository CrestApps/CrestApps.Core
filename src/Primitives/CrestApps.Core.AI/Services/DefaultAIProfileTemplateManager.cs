using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default AI Profile Template Manager.
/// </summary>
public sealed class DefaultAIProfileTemplateManager : NamedSourceCatalogManager<AIProfileTemplate>, IAIProfileTemplateManager
{
    private readonly IEnumerable<IAIProfileTemplateProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIProfileTemplateManager"/> class.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="providers">The providers.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIProfileTemplateManager(
        INamedSourceCatalog<AIProfileTemplate> catalog,
        IEnumerable<ICatalogEntryHandler<AIProfileTemplate>> handlers,
        IEnumerable<IAIProfileTemplateProvider> providers,
        ILogger<DefaultAIProfileTemplateManager> logger)
        : base(catalog, handlers, logger)
    {
        _providers = providers;
    }

    /// <summary>
    /// Gets all.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<IEnumerable<AIProfileTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var dbTemplates = await base.GetAllAsync(cancellationToken);

        return await MergeWithProvidersAsync(dbTemplates);
    }

    /// <summary>
    /// Finds by id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<AIProfileTemplate> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var template = await base.FindByIdAsync(id, cancellationToken);
        if (template is not null)
        {
            return template;
        }

        foreach (var provider in _providers)
        {
            var templates = await provider.GetTemplatesAsync(cancellationToken);
            template = templates.FirstOrDefault(t => string.Equals(t.ItemId, id, StringComparison.OrdinalIgnoreCase));

            if (template is not null)
            {
                return template;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<IEnumerable<AIProfileTemplate>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        var dbTemplates = await base.GetAsync(source, cancellationToken);

        return await MergeWithProvidersAsync(dbTemplates, source);
    }

    /// <summary>
    /// Finds by source.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public new async ValueTask<IEnumerable<AIProfileTemplate>> FindBySourceAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        var dbTemplates = await base.FindBySourceAsync(source, cancellationToken);

        return await MergeWithProvidersAsync(dbTemplates, source);
    }

    /// <summary>
    /// Gets listable.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIProfileTemplate>> GetListableAsync(CancellationToken cancellationToken = default)
    {
        var templates = await GetAllAsync(cancellationToken);

        return templates.Where(template => template.IsListable);
    }

    private async Task<IEnumerable<AIProfileTemplate>> MergeWithProvidersAsync(IEnumerable<AIProfileTemplate> dbTemplates, string source = null)
    {
        var templates = new List<AIProfileTemplate>(dbTemplates);
        var existingNames = new HashSet<string>(templates.Select(template => template.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            var providerTemplates = await provider.GetTemplatesAsync();

            foreach (var template in providerTemplates)
            {
                if (existingNames.Contains(template.Name))
                {
                    continue;
                }

                if (source is not null && !string.Equals(template.Source, source, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                templates.Add(template);
                existingNames.Add(template.Name);
            }
        }

        return templates;
    }
}
