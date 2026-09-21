using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Extension methods for registering <see cref="IGeneratedFileWriter"/> implementations.
/// </summary>
public static class GeneratedFileWriterServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IGeneratedFileWriter"/> implementation as a keyed singleton for each
    /// supplied file extension and records the extensions as output formats a caller may request.
    /// </summary>
    /// <typeparam name="TWriter">The writer implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="extensions">The file extensions handled by the writer, with or without a leading dot.</param>
    public static IServiceCollection AddGeneratedFileWriter<TWriter>(this IServiceCollection services, params string[] extensions)
        where TWriter : class, IGeneratedFileWriter
    {
        return services.AddGeneratedFileWriter<TWriter>(requestable: true, extensions);
    }

    /// <summary>
    /// Registers an <see cref="IGeneratedFileWriter"/> implementation as a keyed singleton for each
    /// supplied file extension, optionally without offering those extensions as formats a caller may ask
    /// for.
    /// </summary>
    /// <remarks>
    /// An unrequestable format still resolves, so the host can write it, but it is absent from
    /// <see cref="IGeneratedFileWriterResolver.SupportedExtensions"/> and
    /// <see cref="IGeneratedFileWriterResolver.IsSupported"/>, which is what the file-creation tools gate
    /// on. That distinction matters for a format whose safety depends on who wrote it: a preview image is
    /// markup this host generated and escaped, and the same extension reached through a tool whose content
    /// argument is written verbatim would be markup a model supplied, served from this origin.
    /// </remarks>
    /// <typeparam name="TWriter">The writer implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="requestable">Whether callers may ask for these formats by name.</param>
    /// <param name="extensions">The file extensions handled by the writer, with or without a leading dot.</param>
    public static IServiceCollection AddGeneratedFileWriter<TWriter>(this IServiceCollection services, bool requestable, params string[] extensions)
        where TWriter : class, IGeneratedFileWriter
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extensions);

        services.TryAddSingleton<TWriter>();

        if (requestable)
        {
            services.Configure<GeneratedFileWriterOptions>(options =>
            {
                foreach (var extension in extensions)
                {
                    options.Add(extension);
                }
            });
        }

        foreach (var extension in extensions)
        {
            var normalized = GeneratedFileWriterOptions.Normalize(extension);

            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            services.AddKeyedSingleton<IGeneratedFileWriter>(
                normalized,
                (sp, _) => sp.GetRequiredService<TWriter>());
        }

        return services;
    }
}
