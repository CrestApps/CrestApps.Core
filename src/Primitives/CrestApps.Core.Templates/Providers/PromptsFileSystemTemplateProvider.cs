using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Templates.Providers;

/// <summary>
/// Discovers system prompt templates from the file system.
/// Scans configured paths for files stored under <c>Templates/Prompts/</c>.
/// </summary>
public sealed class PromptsFileSystemTemplateProvider : ITemplateProvider
{
    /// <summary>
    /// The directory name within a project where prompt templates are stored.
    /// </summary>
    public const string PromptsDirectoryPath = "Templates/Prompts";

    private readonly TemplateOptions _options;
    private readonly IEnumerable<ITemplateParser> _parsers;
    private readonly ILogger<PromptsFileSystemTemplateProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptsFileSystemTemplateProvider"/> class.
    /// </summary>
    /// <param name="options">The template options.</param>
    /// <param name="parsers">The template parsers.</param>
    /// <param name="logger">The logger.</param>
    public PromptsFileSystemTemplateProvider(
        IOptions<TemplateOptions> options,
        IEnumerable<ITemplateParser> parsers,
        ILogger<PromptsFileSystemTemplateProvider> logger)
    {
        _options = options.Value;
        _parsers = parsers;
        _logger = logger;
    }

    /// <summary>
    /// Gets the prompt templates discovered from configured file system paths.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The discovered templates.</returns>
    public Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var templates = new List<Template>();

        foreach (var basePath in _options.DiscoveryPaths)
        {
            var promptsDir = Path.Combine(basePath, PromptsDirectoryPath.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(promptsDir))
            {
                continue;
            }

            DiscoverTemplates(promptsDir, featureId: null, basePath, templates);

            foreach (var subDir in Directory.GetDirectories(promptsDir))
            {
                var featureId = Path.GetFileName(subDir);
                DiscoverTemplates(subDir, featureId, basePath, templates);
            }
        }

        return Task.FromResult<IReadOnlyList<Template>>(templates);
    }

    private void DiscoverTemplates(string directory, string featureId, string sourcePath, List<Template> templates)
    {
        foreach (var file in Directory.GetFiles(directory))
        {
            var extension = Path.GetExtension(file);
            var parser = GetParserForExtension(extension);

            if (parser == null)
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(file);
                var parseResult = parser.Parse(content);
                var id = Path.GetFileNameWithoutExtension(file);
                var template = TemplateProviderConventions.CreateTemplate(
                    id,
                    parseResult,
                    sourcePath,
                    featureId,
                    TemplateProviderConventions.SystemPromptKind);

                templates.Add(template);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse template file: {FilePath}", file);
            }
        }
    }

    private ITemplateParser GetParserForExtension(string extension)
    {
        foreach (var parser in _parsers)
        {
            foreach (var supported in parser.SupportedExtensions)
            {
                if (string.Equals(supported, extension, StringComparison.OrdinalIgnoreCase))
                {
                    return parser;
                }
            }
        }

        return null;
    }
}
