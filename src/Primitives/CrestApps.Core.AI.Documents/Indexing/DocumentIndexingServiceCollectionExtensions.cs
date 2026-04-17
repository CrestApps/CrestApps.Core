using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Documents.Indexing;

/// <summary>
/// Extension methods for registering document indexing profile handlers.
/// </summary>
public static class DocumentIndexingServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAIDocumentIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, AIDocumentSearchIndexProfileHandler>());

        return services;
    }
}
