using System.Text;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Providers;
using CrestApps.Core.Templates.Rendering;
using CrestApps.Core.Templates.Tags;

namespace CrestApps.Core.Templates.Services;

/// <summary>
/// Default implementation of <see cref="ITemplateService"/> that aggregates
/// templates from all registered <see cref="ITemplateProvider"/>s.
/// </summary>
/// <remarks>
/// Templates are read once per service instance. Templates from providers that are not
/// <see cref="IDynamicTemplateProvider"/>s come first, so they take precedence over dynamic
/// templates with the same identifier.
/// </remarks>
public class DefaultTemplateService : ITemplateService
{
    private readonly ITemplateProvider[] _staticProviders;
    private readonly ITemplateProvider[] _dynamicProviders;
    private readonly ITemplateEngine _renderer;

    private IReadOnlyList<Template> _cachedTemplates;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultTemplateService"/> class.
    /// </summary>
    /// <param name="providers">The template providers.</param>
    /// <param name="renderer">The template engine.</param>
    public DefaultTemplateService(
        IEnumerable<ITemplateProvider> providers,
        ITemplateEngine renderer)
    {
        _staticProviders = providers.Where(provider => provider is not IDynamicTemplateProvider).ToArray();
        _dynamicProviders = providers.OfType<IDynamicTemplateProvider>().ToArray();
        _renderer = renderer;
    }

    /// <summary>
    /// Lists all available templates from the registered providers.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The discovered templates.</returns>
    public virtual async Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedTemplates is not null)
        {
            return _cachedTemplates;
        }

        var allTemplates = new List<Template>();
        var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddTemplates(allTemplates, templateIds, await GetStaticTemplatesAsync(cancellationToken));

        foreach (var provider in _dynamicProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddTemplates(allTemplates, templateIds, await provider.GetTemplatesAsync(cancellationToken));
        }

        _cachedTemplates = allTemplates;

        return allTemplates;
    }

    /// <summary>
    /// Reads the templates from the registered providers that are not <see cref="IDynamicTemplateProvider"/>s.
    /// Override this method to keep these templates for longer than the lifetime of the service instance.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The templates, without duplicate identifiers, in provider registration order.</returns>
    protected virtual async Task<IReadOnlyList<Template>> GetStaticTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var allTemplates = new List<Template>();
        var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _staticProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddTemplates(allTemplates, templateIds, await provider.GetTemplatesAsync(cancellationToken));
        }

        return allTemplates;
    }

    /// <summary>
    /// Gets a template by its identifier.
    /// </summary>
    /// <param name="id">The template identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The template whose identifier matches, otherwise the first template with a matching alias, or <see langword="null"/> if none was found.</returns>
    public virtual async Task<Template> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var allTemplates = await ListAsync(cancellationToken);

        return allTemplates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? allTemplates.FirstOrDefault(t => t.Aliases?.Contains(id, StringComparer.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Renders a template by identifier using the configured template engine.
    /// </summary>
    /// <param name="id">The template identifier.</param>
    /// <param name="arguments">The template arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered template output.</returns>
    public virtual async Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var template = await GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"template with ID '{id}' was not found.");

        if (!IncludeTemplateFilter.TryEnterTemplateFrame(id, out var pushed, out var ownsAsyncStack))
        {
            return null;
        }

        try
        {
            return await _renderer.RenderAsync(template.Content, arguments, cancellationToken);
        }
        finally
        {
            IncludeTemplateFilter.ExitTemplateFrame(pushed, ownsAsyncStack);
        }
    }

    /// <summary>
    /// Renders and merges multiple templates into a single output.
    /// </summary>
    /// <param name="ids">The template identifiers to render.</param>
    /// <param name="arguments">The template arguments.</param>
    /// <param name="separator">The separator inserted between rendered templates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The merged rendered output.</returns>
    public virtual async Task<string> MergeAsync(
        IEnumerable<string> ids,
        IDictionary<string, object> arguments = null,
        string separator = "\n\n",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var builder = new StringBuilder();
        var isFirst = true;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rendered = await RenderAsync(id, arguments, cancellationToken);

            if (rendered == null)
            {
                continue;
            }

            if (!isFirst)
            {
                builder.Append(separator);
            }

            builder.Append(rendered);
            isFirst = false;
        }

        return builder.ToString();
    }

    private static void AddTemplates(List<Template> allTemplates, HashSet<string> templateIds, IEnumerable<Template> templates)
    {
        foreach (var template in templates ?? [])
        {
            if (template == null ||
                string.IsNullOrWhiteSpace(template.Id) ||
                !templateIds.Add(template.Id))
            {
                continue;
            }

            allTemplates.Add(template);
        }
    }
}
