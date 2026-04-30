using System.Reflection;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Parsing;

namespace CrestApps.Core.Templates.Providers;

/// <summary>
/// Discovers templates from embedded resources in a specified assembly.
/// Looks for resources matching the pattern <c>*.Templates.*</c>
/// with extensions supported by registered parsers.
/// </summary>
public sealed class EmbeddedResourceTemplateProvider : ITemplateProvider
{
    private const string TemplatesResourceSegment = ".Templates.";

    private readonly Assembly _assembly;
    private readonly IEnumerable<ITemplateParser> _parsers;
    private readonly string _source;
    private readonly string _featureId;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddedResourceTemplateProvider"/> class.
    /// </summary>
    /// <param name="assembly">The assembly to scan for embedded templates.</param>
    /// <param name="parsers">The template parsers.</param>
    /// <param name="source">The logical source name to assign to discovered templates.</param>
    /// <param name="featureId">The feature identifier to assign to discovered templates.</param>
    public EmbeddedResourceTemplateProvider(
        Assembly assembly,
        IEnumerable<ITemplateParser> parsers,
        string source = null,
        string featureId = null)
    {
        _assembly = assembly;
        _parsers = parsers;
        _source = source ?? assembly.GetName().Name;
        _featureId = featureId;
    }

    /// <summary>
    /// Gets the templates discovered from embedded resources.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The discovered templates.</returns>
    public Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var templates = new List<Template>();
        var resourceNames = _assembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            var templatesIndex = resourceName.IndexOf(TemplatesResourceSegment, StringComparison.OrdinalIgnoreCase);

            if (templatesIndex < 0)
            {
                continue;
            }

            // Find a parser that supports this resource's extension.
            var extension = GetExtension(resourceName);
            var parser = GetParserForExtension(extension);

            if (parser == null)
            {
                continue;
            }

            using var stream = _assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            var parseResult = parser.Parse(content);

            var afterTemplates = resourceName[(templatesIndex + TemplatesResourceSegment.Length)..];
            var relativePath = TemplateProviderConventions.ResolveEmbeddedTemplateId(afterTemplates, out var defaultKind);

            var id = extension != null && relativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? relativePath[..^extension.Length]
                : relativePath;

            var template = TemplateProviderConventions.CreateTemplate(
                id,
                parseResult,
                _source,
                _featureId,
                defaultKind);

            templates.Add(template);
        }

        return Task.FromResult<IReadOnlyList<Template>>(templates);
    }

    private ITemplateParser GetParserForExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

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

    private static string GetExtension(string resourceName)
    {
        var lastDot = resourceName.LastIndexOf('.');

        if (lastDot <= 0)
        {
            return null;
        }

        return resourceName[lastDot..];
    }
}
