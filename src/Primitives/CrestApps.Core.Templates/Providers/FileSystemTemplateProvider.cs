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
    private readonly Dictionary<string, ITemplateParser> _parsersByExtension;
    private readonly ILogger<FileSystemTemplateProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTemplateProvider"/> class.
    /// </summary>
    /// <param name="options">The template options.</param>
    /// <param name="parsers">The template parsers.</param>
    /// <param name="logger">The logger.</param>
    public FileSystemTemplateProvider(
        IOptions<TemplateOptions> options,
        IEnumerable<ITemplateParser> parsers,
        ILogger<FileSystemTemplateProvider> logger)
    {
        _options = options.Value;
        _parsersByExtension = CreateParserLookup(parsers);
        _logger = logger;
    }

    /// <summary>
    /// Gets the templates discovered from configured file system paths.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The discovered templates.</returns>
    public Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default)
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
        foreach (var file in Directory.EnumerateFiles(templatesDirectory))
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
        return _parsersByExtension.TryGetValue(extension, out var parser)
            ? parser
            : null;
    }

    /// <summary>
    /// Creates an immutable parser lookup while preserving first-registration precedence.
    /// </summary>
    /// <param name="parsers">The registered template parsers.</param>
    /// <returns>The parser lookup keyed by file extension.</returns>
    private static Dictionary<string, ITemplateParser> CreateParserLookup(
        IEnumerable<ITemplateParser> parsers)
    {
        var parsersByExtension = new Dictionary<string, ITemplateParser>(StringComparer.OrdinalIgnoreCase);

        foreach (var parser in parsers)
        {
            foreach (var supported in parser.SupportedExtensions)
            {
                if (supported is null)
                {
                    continue;
                }

                parsersByExtension.TryAdd(supported, parser);
            }
        }

        return parsersByExtension;
    }
}
