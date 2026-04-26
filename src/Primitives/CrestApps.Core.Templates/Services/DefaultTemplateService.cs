using System.Text;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Providers;
using CrestApps.Core.Templates.Rendering;

namespace CrestApps.Core.Templates.Services;

/// <summary>
/// Default implementation of <see cref="ITemplateService"/> that aggregates
/// templates from all registered <see cref="ITemplateProvider"/>s.
/// </summary>
public class DefaultTemplateService : ITemplateService
{
    private readonly IEnumerable<ITemplateProvider> _providers;
    private readonly ITemplateEngine _renderer;

    private IReadOnlyList<Template> _cachedTemplates;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultTemplateService"/> class.
    /// </summary>
    /// <param name="providers">The providers.</param>
    /// <param name="renderer">The renderer.</param>
    public DefaultTemplateService(
        IEnumerable<ITemplateProvider> providers,
        ITemplateEngine renderer)
    {
        _providers = providers;
        _renderer = renderer;
    }

    /// <summary>
    /// Lists the operation.
    /// </summary>
    public virtual async Task<IReadOnlyList<Template>> ListAsync()
    {
        if (_cachedTemplates is not null)
        {
            return _cachedTemplates;
        }

        var allTemplates = new List<Template>();
        var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            var templates = await provider.GetTemplatesAsync();

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

        _cachedTemplates = allTemplates;

        return allTemplates;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="id">The id.</param>
    public virtual async Task<Template> GetAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var allTemplates = await ListAsync();

        return allTemplates.FirstOrDefault(t =>
                    string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Renders the operation.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="arguments">The arguments.</param>
    public virtual async Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var template = await GetAsync(id)
            ?? throw new KeyNotFoundException($"template with ID '{id}' was not found.");

        return await _renderer.RenderAsync(template.Content, arguments);
    }

    /// <summary>
    /// Merges the operation.
    /// </summary>
    /// <param name="ids">The ids.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="separator">The separator.</param>
    public virtual async Task<string> MergeAsync(
        IEnumerable<string> ids,
        IDictionary<string, object> arguments = null,
        string separator = "\n\n")
    {
        ArgumentNullException.ThrowIfNull(ids);

        var builder = new StringBuilder();
        var isFirst = true;

        foreach (var id in ids)
        {
            var rendered = await RenderAsync(id, arguments);

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
}
