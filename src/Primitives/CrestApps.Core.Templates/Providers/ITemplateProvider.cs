using CrestApps.Core.Templates.Models;

namespace CrestApps.Core.Templates.Providers;

/// <summary>
/// Provides prompt templates from a specific source.
/// Implement this interface to add custom prompt template discovery.
/// </summary>
public interface ITemplateProvider
{
    /// <summary>
    /// Gets all prompt templates from this provider.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The discovered prompt templates.</returns>
    Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default);
}
