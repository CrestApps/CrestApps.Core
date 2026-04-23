using CrestApps.Core.Templates.Models;

namespace CrestApps.Core.Templates.Services;

/// <summary>
/// Extension methods for querying templates through <see cref="ITemplateService"/>.
/// </summary>
public static class TemplateServiceExtensions
{
    /// <summary>
    /// Lists templates matching the specified semantic kind.
    /// </summary>
    /// <param name="templateService">The template service.</param>
    /// <param name="kind">The template kind to match.</param>
    public static async Task<IReadOnlyList<Template>> GetByKindAsync(this ITemplateService templateService, string kind)
    {
        ArgumentNullException.ThrowIfNull(templateService);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var templates = await templateService.ListAsync();

        return (templates ?? [])
            .Where(template => template.Kind is not null && string.Equals(template.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
