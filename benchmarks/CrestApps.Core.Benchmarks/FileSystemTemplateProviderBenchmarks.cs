using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Templates;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Parsing;
using CrestApps.Core.Templates.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures template discovery across many files and registered parsers.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class FileSystemTemplateProviderBenchmarks
{
    private string _tempDirectory;
    private LegacyFileSystemTemplateProvider _legacyProvider;
    private FileSystemTemplateProvider _provider;

    /// <summary>
    /// Gets or sets the number of template files.
    /// </summary>
    [Params(100, 1000)]
    public int FileCount { get; set; }

    /// <summary>
    /// Gets or sets the number of registered parsers.
    /// </summary>
    [Params(1, 20)]
    public int ParserCount { get; set; }

    /// <summary>
    /// Creates template files and parser registrations.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"CrestAppsTemplateBenchmarks_{Guid.NewGuid():N}");
        var templatesDirectory = Path.Combine(_tempDirectory, FileSystemTemplateProvider.TemplatesDirectoryPath);
        Directory.CreateDirectory(templatesDirectory);

        for (var i = 0; i < FileCount; i++)
        {
            File.WriteAllText(Path.Combine(templatesDirectory, $"template-{i}.md"), "Template content.");
        }

        var parsers = new List<ITemplateParser>(ParserCount);

        for (var i = 1; i < ParserCount; i++)
        {
            parsers.Add(new BenchmarkTemplateParser($".unsupported-{i}"));
        }

        parsers.Add(new BenchmarkTemplateParser(".md"));

        var options = new TemplateOptions();
        options.DiscoveryPaths.Add(_tempDirectory);
        _legacyProvider = new LegacyFileSystemTemplateProvider(options, parsers);
        _provider = new FileSystemTemplateProvider(
            Options.Create(options),
            parsers,
            NullLogger<FileSystemTemplateProvider>.Instance);
    }

    /// <summary>
    /// Removes the temporary benchmark files.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    /// <summary>
    /// Discovers and parses the configured templates.
    /// </summary>
    /// <returns>The discovered templates.</returns>
    [Benchmark(Baseline = true)]
    public Task<IReadOnlyList<Template>> GetTemplatesBufferedAsync()
    {
        return _legacyProvider.GetTemplatesAsync();
    }

    /// <summary>
    /// Discovers templates using streaming file enumeration and the extension lookup.
    /// </summary>
    /// <returns>The discovered templates.</returns>
    [Benchmark]
    public Task<IReadOnlyList<Template>> GetTemplatesOptimizedAsync()
    {
        return _provider.GetTemplatesAsync();
    }

    private sealed class BenchmarkTemplateParser : ITemplateParser
    {
        public BenchmarkTemplateParser(string extension)
        {
            SupportedExtensions = [extension];
        }

        public IReadOnlyList<string> SupportedExtensions { get; }

        public TemplateParseResult Parse(string rawContent)
        {
            return new TemplateParseResult
            {
                Body = rawContent,
            };
        }
    }

    private sealed class LegacyFileSystemTemplateProvider
    {
        private readonly TemplateOptions _options;
        private readonly IEnumerable<ITemplateParser> _parsers;

        public LegacyFileSystemTemplateProvider(
            TemplateOptions options,
            IEnumerable<ITemplateParser> parsers)
        {
            _options = options;
            _parsers = parsers;
        }

        public Task<IReadOnlyList<Template>> GetTemplatesAsync()
        {
            var templates = new List<Template>();

            foreach (var basePath in _options.DiscoveryPaths)
            {
                var templatesDirectory = Path.Combine(
                    basePath,
                    FileSystemTemplateProvider.TemplatesDirectoryPath);

                if (!Directory.Exists(templatesDirectory))
                {
                    continue;
                }

                DiscoverTemplates(templatesDirectory, basePath, templates);
            }

            return Task.FromResult<IReadOnlyList<Template>>(templates);
        }

        private void DiscoverTemplates(
            string templatesDirectory,
            string sourcePath,
            List<Template> templates)
        {
            foreach (var file in Directory.GetFiles(templatesDirectory))
            {
                var parser = GetParserForExtension(Path.GetExtension(file));

                if (parser is null)
                {
                    continue;
                }

                var content = File.ReadAllText(file);
                var parseResult = parser.Parse(content);
                var id = Path.GetFileNameWithoutExtension(file);

                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                templates.Add(TemplateProviderConventions.CreateTemplate(id, parseResult, sourcePath));
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
}
