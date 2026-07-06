using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Resolves keyed <see cref="IGeneratedFileWriter"/> registrations by file extension.
/// </summary>
internal sealed class GeneratedFileWriterResolver : IGeneratedFileWriterResolver
{
    private readonly IServiceProvider _services;
    private readonly GeneratedFileWriterOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedFileWriterResolver"/> class.
    /// </summary>
    /// <param name="services">The service provider used to resolve keyed writers.</param>
    /// <param name="options">The registered writer options.</param>
    public GeneratedFileWriterResolver(
        IServiceProvider services,
        IOptions<GeneratedFileWriterOptions> options)
    {
        _services = services;
        _options = options.Value;
    }

    /// <summary>
    /// Gets the supported output file extensions.
    /// </summary>
    public IReadOnlyCollection<string> SupportedExtensions => _options.Extensions;

    /// <summary>
    /// Determines whether a writer is registered for the supplied extension.
    /// </summary>
    /// <param name="extension">The file extension.</param>
    public bool IsSupported(string extension)
    {
        var normalized = GeneratedFileWriterOptions.Normalize(extension);

        return !string.IsNullOrEmpty(normalized) && _options.Extensions.Contains(normalized);
    }

    /// <summary>
    /// Resolves the writer registered for the supplied extension.
    /// </summary>
    /// <param name="extension">The file extension.</param>
    /// <param name="writer">The resolved writer when one exists.</param>
    public bool TryResolve(string extension, out IGeneratedFileWriter writer)
    {
        var normalized = GeneratedFileWriterOptions.Normalize(extension);

        if (string.IsNullOrEmpty(normalized))
        {
            writer = null;

            return false;
        }

        writer = _services.GetKeyedService<IGeneratedFileWriter>(normalized);

        return writer is not null;
    }
}
