using CrestApps.Core.Templates.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Templates.Providers;

/// <summary>
/// Provides prompt templates registered via code using <see cref="TemplateOptions"/>.
/// </summary>
public sealed class OptionsTemplateProvider : ITemplateProvider
{
    private readonly TemplateOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptionsTemplateProvider"/> class.
    /// </summary>
    /// <param name="options">The template options.</param>
    public OptionsTemplateProvider(IOptions<TemplateOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Gets the templates registered through <see cref="TemplateOptions"/>.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The registered templates.</returns>
    public Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Template>>(_options.Templates.ToList());
    }
}
