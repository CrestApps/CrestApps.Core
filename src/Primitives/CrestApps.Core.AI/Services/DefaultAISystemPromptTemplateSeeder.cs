using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Reads the default System Prompt templates embedded under <c>SystemPromptDefaults</c> in the registered assemblies,
/// and saves the missing ones through <see cref="IAIProfileTemplateManager"/>.
/// </summary>
public sealed class DefaultAISystemPromptTemplateSeeder : IAISystemPromptTemplateSeeder
{
    private const string ResourceSegment = ".SystemPromptDefaults.";

    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<ITemplateParser> _parsers;
    private readonly AISystemPromptTemplateDefaultsOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAISystemPromptTemplateSeeder"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider the template manager is resolved from, when templates are
    /// saved.</param>
    /// <param name="parsers">The template parsers, which read each file's front matter and body.</param>
    /// <param name="options">The sources of default templates.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAISystemPromptTemplateSeeder(
        IServiceProvider serviceProvider,
        IEnumerable<ITemplateParser> parsers,
        IOptions<AISystemPromptTemplateDefaultsOptions> options,
        ILogger<DefaultAISystemPromptTemplateSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _parsers = parsers;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Reads the default templates of every registered source.
    /// </summary>
    /// <param name="category">When set, only the templates of this category are returned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The default templates, once each by identifier.</returns>
    public Task<IReadOnlyList<Template>> GetDefaultsAsync(string category = null, CancellationToken cancellationToken = default)
    {
        var templates = new List<Template>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in _options.Sources)
        {
            foreach (var resourceName in source.Assembly.GetManifestResourceNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var index = resourceName.IndexOf(ResourceSegment, StringComparison.OrdinalIgnoreCase);
                var extension = Path.GetExtension(resourceName);
                var parser = GetParser(extension);

                if (index < 0 || parser is null)
                {
                    continue;
                }

                var id = resourceName[(index + ResourceSegment.Length)..^extension.Length];

                using var stream = source.Assembly.GetManifestResourceStream(resourceName);

                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                var parsed = parser.Parse(reader.ReadToEnd());
                var metadata = parsed.Metadata ?? new TemplateMetadata();

                if (!IsInCategory(metadata, source.Category) || !IsInCategory(metadata, category) || !ids.Add(id))
                {
                    continue;
                }

                templates.Add(new Template
                {
                    Id = id,
                    Kind = AITemplateSources.SystemPrompt,
                    Source = source.Assembly.GetName().Name,
                    Metadata = metadata,
                    Content = parsed.Body,
                });
            }
        }

        return Task.FromResult<IReadOnlyList<Template>>(templates);
    }

    /// <summary>
    /// Saves each default template that has no saved AI profile template of the same name yet.
    /// </summary>
    /// <param name="category">When set, only the templates of this category are seeded.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The names of the templates created.</returns>
    public async Task<IReadOnlyList<string>> SeedAsync(string category = null, CancellationToken cancellationToken = default)
    {
        if (_serviceProvider.GetService<INamedSourceCatalog<AIProfileTemplate>>() is null)
        {
            _logger.LogWarning("No AI profile template store is registered, so the default System Prompt templates were not seeded.");

            return [];
        }

        var manager = _serviceProvider.GetRequiredService<IAIProfileTemplateManager>();
        var created = new List<string>();

        foreach (var template in await GetDefaultsAsync(category, cancellationToken))
        {
            if (await manager.FindByNameAsync(template.Id, cancellationToken) is not null)
            {
                continue;
            }

            var record = await manager.NewAsync(template.Id, AITemplateSources.SystemPrompt, cancellationToken: cancellationToken);
            record.DisplayText = string.IsNullOrWhiteSpace(template.Metadata.Title)
                ? template.Id
                : template.Metadata.Title.Trim();
            record.Description = template.Metadata.Description;
            record.Category = template.Metadata.Category;
            record.IsListable = template.Metadata.IsListable;
            record.Put(new SystemPromptTemplateMetadata
            {
                SystemMessage = template.Content?.Trim(),
            });

            await manager.CreateAsync(record, cancellationToken);
            created.Add(template.Id);
        }

        if (created.Count > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Seeded {Count} default System Prompt templates: {Names}.", created.Count, string.Join(", ", created));
        }

        return created;
    }

    private ITemplateParser GetParser(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        return _parsers.FirstOrDefault(parser => parser.SupportedExtensions.Any(supported => string.Equals(supported, extension, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsInCategory(TemplateMetadata metadata, string category)
    {
        return string.IsNullOrWhiteSpace(category) ||
            string.Equals(metadata.Category?.Trim(), category.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
