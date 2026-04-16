using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Mvc.Web.Areas.AI.Controllers;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class AIProviderConnectionOptionsTests
{
    [Fact]
    public void AddCrestAppsAI_WhenTopLevelConnectionsConfigured_ShouldMergeThemIntoProviderOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "config-primary",
                ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
                ["CrestApps:AI:Connections:0:ApiKey"] = "secret",
                ["CrestApps:AI:Connections:0:EnableLogging"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AIProviderOptions>>().Value;

        Assert.True(options.Providers.ContainsKey("OpenAI"));
        var provider = options.Providers["OpenAI"];
        Assert.Contains("config-primary", provider.Connections.Keys);
        Assert.Equal("config-primary", provider.Connections["config-primary"].GetStringValue("DisplayText", false));
        Assert.True(provider.Connections["config-primary"].GetBooleanOrFalseValue("EnableLogging"));
    }

    [Fact]
    public void AddCrestAppsAI_WhenClientNameIsMissing_ShouldIgnoreConnectionEntry()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "config-primary",
                ["CrestApps:AI:Connections:0:ApiKey"] = "secret",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AIProviderOptions>>().Value;

        Assert.Empty(options.Providers);
    }

    [Fact]
    public void AddCrestAppsAI_WhenAzureOpenAIClientNameConfigured_ShouldNormalizeToAzureProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "config-primary",
                ["CrestApps:AI:Connections:0:ClientName"] = "AzureOpenAI",
                ["CrestApps:AI:Connections:0:Endpoint"] = "https://example.openai.azure.com/",
                ["CrestApps:AI:Connections:0:AuthenticationType"] = "ApiKey",
                ["CrestApps:AI:Connections:0:ApiKey"] = "secret",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AIProviderOptions>>().Value;

        Assert.True(options.Providers.ContainsKey(AzureOpenAIConstants.ClientName));
        Assert.Contains("config-primary", options.Providers[AzureOpenAIConstants.ClientName].Connections.Keys);
        Assert.False(options.Providers.ContainsKey("AzureOpenAI"));
    }

    [Fact]
    public async Task ConfigurationAIProviderConnectionStore_GetAllAsync_ShouldMergeStoredAndConfiguredConnections()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "config-primary",
                ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
                ["CrestApps:AI:Connections:0:DisplayText"] = "Config primary",
                ["CrestApps:AI:Connections:0:ApiKey"] = "config-secret",
            })
            .Build();

        var store = CreateConnectionStore(
            configuration,
            dbEntries:
            [
                new AIProviderConnection
                {
                    ItemId = "ui-connection",
                    ClientName = "OpenAI",
                    Name = "ui-secondary",
                    DisplayText = "UI secondary",
                    Properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ApiKey"] = "ui-secret",
                    },
                },
            ]);

        var connections = await store.GetAllAsync();

        Assert.Contains(connections, connection => connection.Name == "config-primary" && AIConfigurationRecordIds.IsConfigurationConnectionId(connection.ItemId));
        Assert.Contains(connections, connection => connection.Name == "ui-secondary" && connection.ItemId == "ui-connection");
    }

    [Fact]
    public async Task ConfigurationAIProviderConnectionStore_GetAllAsync_ShouldReadEveryConfiguredConnectionSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Primary:Connections:0:Name"] = "config-primary",
                ["Primary:Connections:0:ClientName"] = "OpenAI",
                ["Secondary:Connections:0:Name"] = "config-secondary",
                ["Secondary:Connections:0:ClientName"] = "OpenAI",
                ["Primary:Providers:OpenAI:Connections:provider-primary:ApiKey"] = "secret-1",
                ["Secondary:Providers:AzureOpenAI:Connections:provider-secondary:Endpoint"] = "https://example.openai.azure.com/",
                ["Secondary:Providers:AzureOpenAI:Connections:provider-secondary:AuthenticationType"] = "ApiKey",
                ["Secondary:Providers:AzureOpenAI:Connections:provider-secondary:ApiKey"] = "secret-2",
            })
            .Build();

        var catalogOptions = new AIProviderConnectionCatalogOptions();
        catalogOptions.ConnectionSections.Clear();
        catalogOptions.ConnectionSections.Add("Primary:Connections");
        catalogOptions.ConnectionSections.Add("Secondary:Connections");
        catalogOptions.ProviderSections.Clear();
        catalogOptions.ProviderSections.Add("Primary:Providers");
        catalogOptions.ProviderSections.Add("Secondary:Providers");

        var store = CreateConnectionStore(configuration, catalogOptions: catalogOptions);

        var connections = await store.GetAllAsync();

        Assert.Contains(connections, connection => connection.Name == "config-primary" && connection.ClientName == "OpenAI");
        Assert.Contains(connections, connection => connection.Name == "config-secondary" && connection.ClientName == "OpenAI");
        Assert.Contains(connections, connection => connection.Name == "provider-primary" && connection.ClientName == "OpenAI");
        Assert.Contains(connections, connection => connection.Name == "provider-secondary" && connection.ClientName == AzureOpenAIConstants.ClientName);
    }

    [Fact]
    public void AddCrestAppsAI_WhenDisplayTextConfigured_ShouldKeepDisplayText()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "config-primary",
                ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
                ["CrestApps:AI:Connections:0:DisplayText"] = "Config primary",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AIProviderOptions>>().Value;

        Assert.Equal("Config primary", options.Providers["OpenAI"].Connections["config-primary"].GetStringValue("DisplayText", false));
    }

    [Fact]
    public async Task AIDeploymentController_Create_ShouldPopulateConnectionsFromMergedCatalog()
    {
        var deploymentCatalog = new Mock<IAIDeploymentStore>();
        var connectionCatalog = new Mock<INamedSourceCatalog<AIProviderConnection>>();
        connectionCatalog.Setup(catalog => catalog.GetAllAsync()).ReturnsAsync(
        [
            new AIProviderConnection
            {
                ItemId = AIConfigurationRecordIds.CreateConnectionId("OpenAI", "config-primary"),
                Name = "config-primary",
                DisplayText = "Config primary",
                ClientName = "OpenAI",
            },
            new AIProviderConnection
            {
                ItemId = "ui-secondary-id",
                Name = "ui-secondary",
                DisplayText = "UI secondary",
                ClientName = "OpenAI",
            },
        ]);

        var controller = new AIDeploymentController(
            deploymentCatalog.Object,
            connectionCatalog.Object);

        var result = await controller.Create();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AIDeploymentViewModel>(viewResult.Model);

        Assert.Contains(model.Connections, connection => connection.Value == "config-primary" && connection.Text == "Config primary (OpenAI)");
        Assert.Contains(model.Connections, connection => connection.Value == "ui-secondary" && connection.Text == "UI secondary (OpenAI)");
    }

    [Fact]
    public async Task AIConnectionController_Index_ShouldIncludeMergedConnectionsAndMarkConfiguredOnesReadOnly()
    {
        var connectionCatalog = new Mock<INamedSourceCatalog<AIProviderConnection>>();
        connectionCatalog.Setup(catalog => catalog.GetAllAsync()).ReturnsAsync(
        [
            new AIProviderConnection
            {
                ItemId = AIConfigurationRecordIds.CreateConnectionId("OpenAI", "config-primary"),
                Name = "config-primary",
                DisplayText = "Config primary",
                Source = "OpenAI",
            },
            new AIProviderConnection
            {
                ItemId = "ui-connection",
                Name = "ui-secondary",
                DisplayText = "UI secondary",
                Source = "OpenAI",
            },
        ]);

        var controller = new AIConnectionController(connectionCatalog.Object);

        var result = await controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyCollection<AIConnectionViewModel>>(viewResult.Model);

        Assert.Contains(model, connection => connection.Name == "config-primary" && connection.IsReadOnly);
        Assert.Contains(model, connection => connection.Name == "ui-secondary" && !connection.IsReadOnly);
    }

    [Fact]
    public async Task AIDeploymentController_Index_ShouldMarkConfiguredDeploymentsAsReadOnly()
    {
        var deploymentCatalog = new Mock<IAIDeploymentStore>();
        deploymentCatalog.Setup(catalog => catalog.GetAllAsync()).ReturnsAsync(
        [
            new AIDeployment
            {
                ItemId = AIConfigurationRecordIds.CreateDeploymentId("AzureSpeech", null, "whisper"),
                Name = "whisper",
                ModelName = "whisper",
                ClientName = "AzureSpeech",
                Type = AIDeploymentType.SpeechToText,
            },
        ]);

        var connectionCatalog = new Mock<INamedSourceCatalog<AIProviderConnection>>();
        var controller = new AIDeploymentController(
            deploymentCatalog.Object,
            connectionCatalog.Object);

        var result = await controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyCollection<AIDeploymentViewModel>>(viewResult.Model);

        Assert.Contains(model, deployment => deployment.TechnicalName == "whisper" && deployment.IsReadOnly);
    }

    [Fact]
    public void AIConnectionViewModel_ApplyTo_ShouldNormalizeAzureOpenAIProviderName()
    {
        var model = new AIConnectionViewModel
        {
            Source = "AzureOpenAI",
            Name = "config-primary",
        };

        var connection = new AIProviderConnection();
        model.ApplyTo(connection);

        Assert.Equal(AzureOpenAIConstants.ClientName, connection.Source);
    }

    [Fact]
    public void AddAzureOpenAIProvider_ShouldRegisterAzureSpeechAsDeploymentProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIServices();
        services.AddCoreAIAzureOpenAI();
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AIOptions>>().Value;

        Assert.True(options.Deployments.ContainsKey(AzureOpenAIConstants.AzureSpeechProviderName));
        Assert.True(options.Deployments[AzureOpenAIConstants.AzureSpeechProviderName].SupportsContainedConnection);
    }

    [Fact]
    public void AddCrestAppsAI_WhenCustomConnectionSectionsConfigured_ShouldMergeEverySectionIntoProviderOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Primary:Connections:0:Name"] = "config-primary",
                ["Primary:Connections:0:ClientName"] = "OpenAI",
                ["Secondary:Providers:AzureOpenAI:Connections:azure-secondary:Endpoint"] = "https://example.openai.azure.com/",
                ["Secondary:Providers:AzureOpenAI:Connections:azure-secondary:AuthenticationType"] = "ApiKey",
                ["Secondary:Providers:AzureOpenAI:Connections:azure-secondary:ApiKey"] = "secret",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.Configure<AIProviderConnectionCatalogOptions>(options =>
        {
            options.ConnectionSections.Add("Primary:Connections");
            options.ProviderSections.Add("Secondary:Providers");
        });
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AIProviderOptions>>().Value;

        Assert.Contains("config-primary", options.Providers["OpenAI"].Connections.Keys);
        Assert.Contains("azure-secondary", options.Providers[AzureOpenAIConstants.ClientName].Connections.Keys);
    }

    [Fact]
    public async Task ConfigurationAIProviderConnectionStore_ShouldSkipConfiguredConflictsByName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "shared-name",
                ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            })
            .Build();

        var store = CreateConnectionStore(
            configuration,
            dbEntries:
            [
                new AIProviderConnection
                {
                    ItemId = "ui-connection",
                    ClientName = "OpenAI",
                    Name = "shared-name",
                },
            ]);

        var connections = await store.GetAllAsync();

        Assert.Single(connections);
        Assert.Equal("ui-connection", connections.Single().ItemId);
    }

    private static DefaultAIProviderConnectionStore CreateConnectionStore(
        IConfiguration configuration,
        AIProviderConnectionCatalogOptions catalogOptions = null,
        List<AIProviderConnection> dbEntries = null)
    {
        var sources = new List<INamedSourceCatalogSource<AIProviderConnection>>();

        if (dbEntries is { Count: > 0 })
        {
            sources.Add(new TestAIProviderConnectionSource(dbEntries));
        }

        sources.Add(new ConfigurationAIProviderConnectionSource(
            configuration,
            TimeProvider.System,
            Options.Create(catalogOptions ?? new AIProviderConnectionCatalogOptions()),
            NullLogger<ConfigurationAIProviderConnectionSource>.Instance));

        return new DefaultAIProviderConnectionStore(sources);
    }

    private sealed class TestAIProviderConnectionSource(List<AIProviderConnection> connections) : IWritableNamedSourceCatalogSource<AIProviderConnection>
    {
        public int Order => 0;

        public ValueTask<IReadOnlyCollection<AIProviderConnection>> GetEntriesAsync(IReadOnlyCollection<AIProviderConnection> knownEntries)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIProviderConnection>>(connections.ToArray());
        }

        public ValueTask CreateAsync(AIProviderConnection entry)
        {
            connections.Add(entry);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(AIProviderConnection entry)
        {
            connections.Remove(entry);
            return ValueTask.FromResult(true);
        }

        public ValueTask UpdateAsync(AIProviderConnection entry) => ValueTask.CompletedTask;
    }
}
