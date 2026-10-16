using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Parsing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides the persona files in the content root's <c>Templates/Personas</c> folder. The file path below that
/// folder, without its extension and with folders joined by dots, becomes the persona's technical name.
/// </summary>
public sealed class AIPersonaFileSystemProvider : IAIPersonaProvider
{
    /// <summary>
    /// The folder, relative to the content root, that holds persona files.
    /// </summary>
    public const string PersonasDirectoryPath = "Templates/Personas";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IEnumerable<ITemplateParser> _parsers;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIPersonaFileSystemProvider"/> class.
    /// </summary>
    /// <param name="hostEnvironment">The host environment that gives the content root.</param>
    /// <param name="parsers">The parsers that read the files' front matter.</param>
    /// <param name="logger">The logger.</param>
    public AIPersonaFileSystemProvider(
        IHostEnvironment hostEnvironment,
        IEnumerable<ITemplateParser> parsers,
        ILogger<AIPersonaFileSystemProvider> logger)
    {
        _hostEnvironment = hostEnvironment;
        _parsers = parsers;
        _logger = logger;
    }

    /// <summary>
    /// Gets the personas in the persona folder. A file that cannot be read is logged and skipped.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<IReadOnlyList<AIPersona>> GetPersonasAsync(CancellationToken cancellationToken = default)
    {
        var personas = new List<AIPersona>();
        var personasDirectory = Path.Combine(_hostEnvironment.ContentRootPath, PersonasDirectoryPath.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(personasDirectory))
        {
            return Task.FromResult<IReadOnlyList<AIPersona>>(personas);
        }

        foreach (var file in Directory.EnumerateFiles(personasDirectory, "*", SearchOption.AllDirectories))
        {
            var parser = AIProfileTemplateParser.GetParserForExtension(_parsers, Path.GetExtension(file));

            if (parser is null)
            {
                continue;
            }

            try
            {
                var parseResult = parser.Parse(File.ReadAllText(file));
                var relativePath = Path.GetRelativePath(personasDirectory, file);
                var id = Path.ChangeExtension(relativePath, null)?
                    .Replace(Path.DirectorySeparatorChar, '.')
                    .Replace(Path.AltDirectorySeparatorChar, '.');

                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                personas.Add(AIPersonaFileParser.Parse(id, parseResult));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse AI persona file: {FilePath}", file);
            }
        }

        return Task.FromResult<IReadOnlyList<AIPersona>>(personas);
    }
}
