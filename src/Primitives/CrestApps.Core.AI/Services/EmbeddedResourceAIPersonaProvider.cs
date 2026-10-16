using System.Reflection;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Parsing;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides the persona files an assembly embeds under <c>Templates/Personas</c>. The file path below that folder,
/// without its extension, becomes the persona's technical name.
/// </summary>
public sealed class EmbeddedResourceAIPersonaProvider : IAIPersonaProvider
{
    private const string PersonasResourceSegment = ".Templates.Personas.";
    private const string OrchardCorePersonasResourceSegment = ".Templates>Personas>";

    private readonly Assembly _assembly;
    private readonly IEnumerable<ITemplateParser> _parsers;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddedResourceAIPersonaProvider"/> class.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the persona files.</param>
    /// <param name="parsers">The parsers that read the files' front matter.</param>
    public EmbeddedResourceAIPersonaProvider(
        Assembly assembly,
        IEnumerable<ITemplateParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _assembly = assembly;
        _parsers = parsers ?? [];
    }

    /// <summary>
    /// Gets the personas embedded in the assembly.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<IReadOnlyList<AIPersona>> GetPersonasAsync(CancellationToken cancellationToken = default)
    {
        var personas = new List<AIPersona>();

        foreach (var resourceName in _assembly.GetManifestResourceNames())
        {
            var personasIndex = resourceName.IndexOf(PersonasResourceSegment, StringComparison.OrdinalIgnoreCase);
            var segmentLength = PersonasResourceSegment.Length;

            if (personasIndex < 0)
            {
                personasIndex = resourceName.IndexOf(OrchardCorePersonasResourceSegment, StringComparison.OrdinalIgnoreCase);
                segmentLength = OrchardCorePersonasResourceSegment.Length;
            }

            if (personasIndex < 0)
            {
                continue;
            }

            var extension = GetExtension(resourceName);
            var parser = AIProfileTemplateParser.GetParserForExtension(_parsers, extension);

            if (parser is null)
            {
                continue;
            }

            using var stream = _assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            var parseResult = parser.Parse(reader.ReadToEnd());
            var relativePath = resourceName[(personasIndex + segmentLength)..];
            var id = extension is not null && relativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? relativePath[..^extension.Length]
                : relativePath;

            personas.Add(AIPersonaFileParser.Parse(id, parseResult));
        }

        return Task.FromResult<IReadOnlyList<AIPersona>>(personas);
    }

    private static string GetExtension(string resourceName)
    {
        var lastDot = resourceName.LastIndexOf('.');

        return lastDot <= 0
            ? null
            : resourceName[lastDot..];
    }
}
