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
    /// supplied file extension and records the extensions as supported output formats.
    /// </summary>
    /// <typeparam name="TWriter">The writer implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="extensions">The file extensions handled by the writer, with or without a leading dot.</param>
    public static IServiceCollection AddGeneratedFileWriter<TWriter>(this IServiceCollection services, params string[] extensions)
        where TWriter : class, IGeneratedFileWriter
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extensions);

        services.TryAddSingleton<TWriter>();
        services.Configure<GeneratedFileWriterOptions>(options =>
        {
            foreach (var extension in extensions)
            {
                options.Add(extension);
            }
        });

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
