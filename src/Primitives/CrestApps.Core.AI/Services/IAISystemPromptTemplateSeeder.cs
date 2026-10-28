using CrestApps.Core.Templates.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Copies the default System Prompt templates, such as the speaking styles and guardrail topics the framework ships,
/// into the host's saved AI profile templates, where they can be edited or deleted like any other. Call
/// <see cref="SeedAsync"/> once, from a migration or a setup step: a template that already exists by name is never
/// overwritten, but one deleted after seeding comes back if it runs again.
/// </summary>
public interface IAISystemPromptTemplateSeeder
{
    /// <summary>
    /// Reads the default templates of every registered <see cref="AISystemPromptTemplateDefaultsSource"/>.
    /// </summary>
    /// <param name="category">When set, only the templates of this category are returned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The default templates, once each by identifier.</returns>
    Task<IReadOnlyList<Template>> GetDefaultsAsync(string category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves each default template that has no saved AI profile template of the same name yet, as a System Prompt
    /// template with the default's title, description, category and body.
    /// </summary>
    /// <param name="category">When set, only the templates of this category are seeded.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The names of the templates created.</returns>
    Task<IReadOnlyList<string>> SeedAsync(string category = null, CancellationToken cancellationToken = default);
}
