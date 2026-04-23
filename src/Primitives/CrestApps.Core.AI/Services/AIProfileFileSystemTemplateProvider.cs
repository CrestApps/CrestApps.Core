using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Parsing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Discovers AI profile templates from the host application's file system.
/// Scans <c>Templates/Profiles</c> under the application's content root.
/// </summary>
public sealed class AIProfileFileSystemTemplateProvider : IAIProfileTemplateProvider
{
    public const string ProfilesDirectoryPath = "Templates/Profiles";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IEnumerable<ITemplateParser> _parsers;
    private readonly ILogger<AIProfileFileSystemTemplateProvider> _logger;

    public AIProfileFileSystemTemplateProvider(
        IHostEnvironment hostEnvironment,
        IEnumerable<ITemplateParser> parsers,
        ILogger<AIProfileFileSystemTemplateProvider> logger)
    {
        _hostEnvironment = hostEnvironment;
        _parsers = parsers;
        _logger = logger;
    }

    public Task<IReadOnlyList<AIProfileTemplate>> GetTemplatesAsync()
    {
        var templates = new List<AIProfileTemplate>();
        var profilesDirectory = Path.Combine(_hostEnvironment.ContentRootPath, ProfilesDirectoryPath.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(profilesDirectory))
        {
            return Task.FromResult<IReadOnlyList<AIProfileTemplate>>(templates);
        }

        foreach (var file in Directory.EnumerateFiles(profilesDirectory, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            var parser = AIProfileTemplateParser.GetParserForExtension(_parsers, extension);

            if (parser is null)
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(file);
                var parseResult = parser.Parse(content);
                var relativePath = Path.GetRelativePath(profilesDirectory, file);
                var id = Path.ChangeExtension(relativePath, null)?
                    .Replace(Path.DirectorySeparatorChar, '.')
                    .Replace(Path.AltDirectorySeparatorChar, '.');

                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                templates.Add(AIProfileTemplateParser.Parse(id, parseResult));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse AI profile template file: {FilePath}", file);
            }
        }

        return Task.FromResult<IReadOnlyList<AIProfileTemplate>>(templates);
    }
}
