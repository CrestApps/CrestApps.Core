using CrestApps.Core.AI.Models;
using CrestApps.Core.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Services;


/// <summary>
/// Projects MVC-managed AI provider connections into <see cref="AIProviderOptions"/>
/// so framework AI clients can resolve connection settings from the sample app's
/// YesSql-backed admin UI.
/// </summary>
public sealed class MvcAIProviderOptionsConfiguration : IConfigureOptions<AIProviderOptions>
{
    private readonly MvcAIProviderOptionsStore _providerOptionsStore;

    public MvcAIProviderOptionsConfiguration(
        MvcAIProviderOptionsStore providerOptionsStore)
    {
        _providerOptionsStore = providerOptionsStore;
    }

    public void Configure(AIProviderOptions options)
    {
        _providerOptionsStore.ApplyTo(options);
    }
}

public static class MvcAIProviderOptionsBuilderExtensions
{
    public static CrestAppsAISuiteBuilder ConfigureProviderOptions(this CrestAppsAISuiteBuilder builder, IConfigurationSection configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.Configure<AIProviderOptions>(configuration);

        return builder;
    }

    public static CrestAppsAISuiteBuilder AddMvcProviderOptions(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<MvcAIProviderOptionsStore>();
        builder.Services.AddTransient<IConfigureOptions<AIProviderOptions>, MvcAIProviderOptionsConfiguration>();

        return builder;
    }
}
