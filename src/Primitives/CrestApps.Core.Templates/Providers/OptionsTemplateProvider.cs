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
    /// <param name="options">The options.</param>
    public OptionsTemplateProvider(IOptions<TemplateOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Gets templates.
    /// </summary>
    public Task<IReadOnlyList<Template>> GetTemplatesAsync()
    {
        return Task.FromResult<IReadOnlyList<Template>>(_options.Templates.ToList());
    }
}
