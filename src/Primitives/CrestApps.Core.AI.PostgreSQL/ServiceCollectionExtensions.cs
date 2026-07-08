using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.PostgreSQL.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.PostgreSQL;
using CrestApps.Core.PostgreSQL.Builders;
using CrestApps.Core.PostgreSQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.PostgreSQL;

/// <summary>
/// Provides extension methods for registering AI-specific PostgreSQL services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL AI document source services including vector search.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCorePostgreSQLAIDocumentSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedScoped<IVectorSearchService>(PostgreSQLConstants.ProviderName, (sp, _)
            => new PostgreSQLVectorSearchService(sp.GetRequiredService<IPostgreSQLClientFactory>(), sp.GetRequiredService<ILogger<PostgreSQLVectorSearchService>>()));

        return services.AddCorePostgreSQLSource(IndexProfileTypes.AIDocuments, descriptor =>
                {
                    descriptor.DisplayName = new LocalizedString("AI Documents", "AI Documents");
                    descriptor.Description = new LocalizedString("PostgreSQL AI Documents Description", "Create a PostgreSQL index for uploaded and embedded AI document chunks.");
                }).AddCoreAIDocumentIndexProfileHandler();
    }

    /// <summary>
    /// Registers the PostgreSQL AI data source services for RAG.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCorePostgreSQLAIDataSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddCorePostgreSQLSource(IndexProfileTypes.DataSource, descriptor =>
                {
                    descriptor.DisplayName = new LocalizedString("Data Source", "Data Source");
                    descriptor.Description = new LocalizedString("PostgreSQL Data Source Description", "Create a PostgreSQL index for AI knowledge base data source documents.");
                }).AddCoreAIDataSourceRag()
                .Configure<AIDataSourceSourceOptions>(options => options.AddOrUpdate(
                    AIDataSourceSourceTypes.PostgreSQL,
                    new LocalizedString("PostgreSQL", "PostgreSQL"),
                    new LocalizedString("PostgreSQL Source Description", "Read source documents from an external PostgreSQL table using explicit connection settings.")))
                .AddKeyedScoped<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.PostgreSQL, (sp, _)
                    => new PostgreSQLAIDataSourceSourceHandler(
                        sp.GetRequiredService<IPostgreSQLClientFactory>(),
                        sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
                        sp.GetRequiredService<ILogger<PostgreSQLAIDataSourceSourceHandler>>()))
                .AddCoreAIDataSourceIndexProfileHandler();
    }

    /// <summary>
    /// Registers the PostgreSQL AI memory source services including memory vector search.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCorePostgreSQLAIMemorySource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddKeyedScoped<IMemoryVectorSearchService>(PostgreSQLConstants.ProviderName, (sp, _)
            => new PostgreSQLMemoryVectorSearchService(sp.GetRequiredService<IPostgreSQLClientFactory>(), sp.GetRequiredService<ILogger<PostgreSQLMemoryVectorSearchService>>()));

        return services.AddCorePostgreSQLSource(IndexProfileTypes.AIMemory, descriptor =>
                {
                    descriptor.DisplayName = new LocalizedString("AI Memory", "AI Memory");
                    descriptor.Description = new LocalizedString("PostgreSQL AI Memory Description", "Create a PostgreSQL index for user and system memory records.");
                }).AddCoreAIMemoryIndexProfileHandler();
    }

    /// <summary>
    /// Registers a PostgreSQL index profile source with the specified type and optional configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="type">The index profile type identifier.</param>
    /// <param name="configure">An optional action to configure the index profile source descriptor.</param>
    public static IServiceCollection AddCorePostgreSQLSource(this IServiceCollection services, string type, Action<IndexProfileSourceDescriptor> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(type);

        services.AddCoreAIDefaultIndexProfileHandler();
        services.Configure<IndexProfileSourceOptions>(options => options
            .AddOrUpdate(PostgreSQLConstants.ProviderName, new LocalizedString("PostgreSQL", "PostgreSQL"), type, configure)
        );

        return services;
    }

    /// <summary>
    /// Adds AI document indexing support to the PostgreSQL builder.
    /// </summary>
    /// <param name="builder">The PostgreSQL builder.</param>
    public static CrestAppsPostgreSQLBuilder AddAIDocuments(this CrestAppsPostgreSQLBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCorePostgreSQLAIDocumentSource();

        return builder;
    }

    /// <summary>
    /// Adds AI data source indexing support to the PostgreSQL builder.
    /// </summary>
    /// <param name="builder">The PostgreSQL builder.</param>
    public static CrestAppsPostgreSQLBuilder AddAIDataSources(this CrestAppsPostgreSQLBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCorePostgreSQLAIDataSource();

        return builder;
    }

    /// <summary>
    /// Adds AI memory indexing support to the PostgreSQL builder.
    /// </summary>
    /// <param name="builder">The PostgreSQL builder.</param>
    public static CrestAppsPostgreSQLBuilder AddAIMemory(this CrestAppsPostgreSQLBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCorePostgreSQLAIMemorySource();

        return builder;
    }
}
