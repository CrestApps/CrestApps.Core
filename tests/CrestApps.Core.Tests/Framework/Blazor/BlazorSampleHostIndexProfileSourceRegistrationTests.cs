using System.Reflection;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Blazor.Web.ViewModels;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.PostgreSQL;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.Blazor;

public sealed class BlazorSampleHostIndexProfileSourceRegistrationTests
{
    [Fact]
    public void AddBlazorSampleHostServices_ShouldRegisterDistinctArticleProviders()
    {
        var services = new ServiceCollection();
        var method = typeof(AIDataSourceViewModel).Assembly
            .GetType("CrestApps.Core.Blazor.Web.Services.EntityCoreSampleServiceCollectionExtensions", throwOnError: true)!
            .GetMethod(
                "AddBlazorSampleHostServices",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        _ = method.Invoke(null, [services, Path.GetTempPath()]);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<IndexProfileSourceOptions>>().Value;

        Assert.Contains(options.Sources, source =>
            source.ProviderName == ElasticsearchConstants.ProviderName &&
            source.Type == IndexProfileTypes.Articles);
        Assert.Contains(options.Sources, source =>
            source.ProviderName == AISearchConstants.ProviderName &&
            source.Type == IndexProfileTypes.Articles);
        Assert.Contains(options.Sources, source =>
            source.ProviderName == PostgreSQLConstants.ProviderName &&
            source.Type == IndexProfileTypes.Articles);
    }
}
