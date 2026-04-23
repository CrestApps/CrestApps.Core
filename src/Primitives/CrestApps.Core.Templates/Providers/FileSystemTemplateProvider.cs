using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Templates.Providers;

/// <summary>
/// Discovers templates from the file system.
/// Scans configured paths for templates stored directly under <c>Templates/</c>.
/// Subdirectories are ignored so other providers can own their own folder conventions.
/// </summary>
public sealed class FileSystemTemplateProvider : ITemplateProvider
{
    /// <summary>
    /// The directory name within a project where generic templates are stored.
    /// </summary>
    public const string TemplatesDirectoryPath = "Templates";

    private readonly TemplateOptions _options;
    private readonly IEnumerable<ITemplateParser> _parsers;
    private readonly ILogger<FileSystemTemplateProvider> _logger;

    public FileSystemTemplateProvider(
        IOptions<TemplateOptions> options,
        IEnumerable<ITemplateParser> parsers,
        ILogger<FileSystemTemplateProvider> logger)
    {
        _options = options.Value;
        _parsers = parsers;
        _logger = logger;
    }

    public Task<IReadOnlyList<Template>> GetTemplatesAsync()
    {
        var templates = new List<Template>();

        foreach (var basePath in _options.DiscoveryPaths)
        {
            var templatesDir = Path.Combine(basePath, TemplatesDirectoryPath.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(templatesDir))
            {
                continue;
            }

            DiscoverTemplates(templatesDir, basePath, templates);
        }

        return Task.FromResult<IReadOnlyList<Template>>(templates);
    }

    private void DiscoverTemplates(string templatesDirectory, string sourcePath, List<Template> templates)
    {
        foreach (var file in Directory.GetFiles(templatesDirectory))
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

                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var template = TemplateProviderConventions.CreateTemplate(
                    id,
                    parseResult,
                    sourcePath);

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
