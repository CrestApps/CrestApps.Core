using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Copilot;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Data.EntityCore.Services;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using CrestApps.Core.Startup.Shared.Handlers;
using CrestApps.Core.Startup.Shared.Models;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.Blazor.Web.Services;

internal static class EntityCoreSampleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Blazor sample host services that surround the framework:
    /// Entity Core storage, sample-only managers, article demo services, and
    /// provider-specific option bridges used by the admin UI.
    /// </summary>
    public static IServiceCollection AddBlazorSampleHostServices(this IServiceCollection services, string appDataPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(appDataPath);

        var dbPath = Path.Combine(appDataPath, "crestapps-blazor.db");

        services.AddCoreEntityCoreSqliteDataStore($"Data Source={dbPath}");
        services.AddDocumentCatalog<Article, DocumentCatalog<Article>>();

        services
            .AddSharedArticleServices()
            .AddSharedTemplateProviders()
            .AddKeyedScoped<IAIReferenceLinkResolver, ArticleAIReferenceLinkResolver>(IndexProfileTypes.Articles)
            .AddScoped<SampleCitationReferenceCollector>()
            .AddScoped<CompositeAIReferenceLinkResolver>()
            .AddScoped<IAIDataSourceIndexingService, DefaultAIDataSourceIndexingService>()
            .AddScoped<Areas.AI.Services.AIProfileDocumentService>()
            .AddScoped<Areas.AI.Services.AIProfileTemplateDocumentService>()
            .AddScoped<Areas.AIChat.Services.SampleAIChatSessionEventService>()
            .AddScoped<Areas.AIChat.Services.SampleAICompletionUsageService>()
            .AddScoped<Areas.AIChat.Services.SampleAIChatSessionEventPostCloseObserver>()
            .AddScoped<IAICompletionUsageObserver>(sp => sp.GetRequiredService<Areas.AIChat.Services.SampleAICompletionUsageService>())
            .AddScoped<IAIChatSessionAnalyticsRecorder>(sp => sp.GetRequiredService<Areas.AIChat.Services.SampleAIChatSessionEventPostCloseObserver>())
            .AddScoped<IAIChatSessionConversionGoalRecorder>(sp => sp.GetRequiredService<Areas.AIChat.Services.SampleAIChatSessionEventPostCloseObserver>())
            .AddScoped<IAIChatSessionHandler, Areas.AIChat.Handlers.AnalyticsChatSessionHandler>()
            .AddScoped<ICatalogEntryHandler<AIMemoryEntry>, Areas.AI.Handlers.AIMemoryEntryHandler>()
            .AddScoped<Areas.Indexing.Services.SampleAIDocumentIndexingService>()
            .AddScoped<IAuthorizationHandler, Areas.AIChat.Services.SampleChatInteractionDocumentAuthorizationHandler>()
            .AddScoped<IAuthorizationHandler, Areas.AIChat.Services.SampleAIChatSessionDocumentAuthorizationHandler>()
            .AddScoped<IAIChatDocumentEventHandler, Areas.AIChat.Services.SampleAIChatDocumentEventHandler>()
            .AddScoped<ICatalogEntryHandler<Article>, ArticleHandler>()
            .AddScoped<ICopilotCredentialStore, JsonFileCopilotCredentialStore>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DocumentChatInteractionSettingsHandler>());
        services.ConfigureOptions<Areas.AIChat.Services.SampleCopilotOptionsConfiguration>();
        services.ConfigureOptions<Areas.AIChat.Services.SampleClaudeOptionsConfiguration>();

        services.Configure<IndexProfileSourceOptions>(options => options
            .AddOrUpdate(ElasticsearchConstants.ProviderName, "Elasticsearch", IndexProfileTypes.Articles, descriptor =>
            {
                descriptor.DisplayName = "Articles";
                descriptor.Description = "Create an Elasticsearch index for sample article records managed in the Blazor app.";
            })
        );
        services.Configure<IndexProfileSourceOptions>(options => options
            .AddOrUpdate(ElasticsearchConstants.ProviderName, "Azure AI Search", IndexProfileTypes.Articles, descriptor =>
            {
                descriptor.DisplayName = "Articles";
                descriptor.Description = "Create an Azure AI Search index for sample article records managed in the Blazor app.";
            })
        );

        return services;
    }
}
