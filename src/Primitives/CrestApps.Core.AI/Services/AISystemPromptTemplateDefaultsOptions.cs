using System.Reflection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Where the default System Prompt templates that <see cref="IAISystemPromptTemplateSeeder"/> seeds are read from.
/// Each source is an assembly that embeds Markdown templates under a <c>SystemPromptDefaults</c> folder. Unlike
/// templates under <c>Templates/Prompts</c>, these are not served as read-only templates: they are copied once into
/// the saved System Prompt templates, where they can be edited or deleted.
/// </summary>
public sealed class AISystemPromptTemplateDefaultsOptions
{
    /// <summary>
    /// Gets the sources of default templates.
    /// </summary>
    public IList<AISystemPromptTemplateDefaultsSource> Sources { get; } = [];
}

/// <summary>
/// An assembly that embeds default System Prompt templates under a <c>SystemPromptDefaults</c> folder.
/// </summary>
/// <param name="Assembly">The assembly.</param>
/// <param name="Category">When set, only the templates of this category are read from the assembly.</param>
public sealed record AISystemPromptTemplateDefaultsSource(Assembly Assembly, string Category = null);
