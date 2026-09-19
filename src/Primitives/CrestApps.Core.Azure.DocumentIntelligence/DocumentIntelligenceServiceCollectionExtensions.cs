using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.Azure.DocumentIntelligence.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Azure.DocumentIntelligence;

/// <summary>
/// Registers the Azure AI Document Intelligence reader.
/// </summary>
public static class DocumentIntelligenceServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Document Intelligence reader for the configured extensions.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the reader.</param>
    /// <remarks>
    /// Registration is explicit rather than automatic because a keyed reader resolves to the last
    /// registration, so adding this package would otherwise silently change which reader serves every PDF in
    /// the application. The reader is wrapped so that an unconfigured, throttled or failing service falls
    /// through to whichever reader was already registered.
    /// </remarks>
    public static IServiceCollection AddCoreAIDocumentIntelligence(
        this IServiceCollection services,
        Action<DocumentIntelligenceOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DocumentIntelligenceOptions();
        configure?.Invoke(options);

        services.AddOptions<DocumentIntelligenceOptions>();

        if (configure != null)
        {
            services.Configure(configure);
        }

        if (!options.IsConfigured())
        {
            // Nothing to reach. The local readers keep serving every document, which is the same behaviour as
            // not referencing this package at all.
            return services;
        }

        services.TryAddSingleton(sp => CreateClient(sp.GetRequiredService<IOptions<DocumentIntelligenceOptions>>().Value));
        services.TryAddSingleton<DocumentIntelligenceIngestionDocumentReader>();

        foreach (var extension in options.Extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                continue;
            }

            var key = extension.StartsWith('.') ? extension : '.' + extension;

            services.AddKeyedSingleton<IngestionDocumentReader>(key, (sp, serviceKey) =>
            {
                var preferred = sp.GetRequiredService<DocumentIntelligenceIngestionDocumentReader>();
                var fallback = ResolveFallback(sp, (string)serviceKey);

                if (fallback == null)
                {
                    return preferred;
                }

                return new FallbackIngestionDocumentReader(
                    preferred,
                    fallback,
                    sp.GetRequiredService<ILogger<FallbackIngestionDocumentReader>>());
            });
        }

        return services;
    }

    /// <summary>
    /// Adds the Document Intelligence reader for the configured extensions.
    /// </summary>
    /// <param name="builder">The ingestion builder.</param>
    /// <param name="configure">Configures the reader.</param>
    /// <remarks>
    /// This reader is a reader like any other, so it belongs to the ingestion builder rather than to document
    /// processing: a host that only reads files into a knowledge base can reach it without registering chat
    /// uploads, tabular workspaces and the tool surface to get at it.
    /// </remarks>
    public static CrestAppsDocumentIngestionBuilder AddDocumentIntelligence(
        this CrestAppsDocumentIngestionBuilder builder,
        Action<DocumentIntelligenceOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIDocumentIntelligence(configure);

        return builder;
    }

    /// <summary>
    /// Finds the reader that was already serving an extension, so it can act as the fallback.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="key">The extension.</param>
    /// <returns>The previously registered reader, or <see langword="null"/> when there was none.</returns>
    private static IngestionDocumentReader ResolveFallback(IServiceProvider serviceProvider, string key)
    {
        // Every reader keyed to this extension is resolved and the last non-wrapping one wins, which is the
        // registration that was in place before this package was added.
        foreach (var reader in serviceProvider.GetKeyedServices<IngestionDocumentReader>(key).Reverse())
        {
            if (reader is not DocumentIntelligenceIngestionDocumentReader and not FallbackIngestionDocumentReader)
            {
                return reader;
            }
        }

        return null;
    }

    private static DocumentIntelligenceClient CreateClient(DocumentIntelligenceOptions options)
    {
        var endpoint = new Uri(options.Endpoint);

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(options.ApiKey));
        }

        return new DocumentIntelligenceClient(endpoint, new DefaultAzureCredential());
    }
}
