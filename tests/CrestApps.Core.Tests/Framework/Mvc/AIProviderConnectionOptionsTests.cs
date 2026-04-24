using CrestApps.Core.AI;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.Services;
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

public sealed class AIProviderConnectionConfigurationTests
{
    [Fact]
    public async Task AddCrestAppsAI_WhenTopLevelConnectionsConfigured_ShouldExposeThemInConnectionStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "config-primary",
                ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
                ["CrestApps:AI:Connections:0:ApiKey"] = "secret",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var connections = await serviceProvider.GetRequiredService<IAIProviderConnectionStore>().GetAllAsync(TestContext.Current.CancellationToken);
        var connection = Assert.Single(connections);

        Assert.Equal("config-primary", connection.Name);
        Assert.Equal("OpenAI", connection.ClientName);
        Assert.Equal("config-primary", connection.DisplayText);
    }

    [Fact]
    public void AddCoreAIAzureOpenAI_WhenAzureClientSettingsConfigured_ShouldBindAzureClientSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:AzureClient:EnableLogging"] = "true",
                ["CrestApps:AI:AzureClient:EnableMessageLogging"] = "true",
                ["CrestApps:AI:AzureClient:EnableMessageContentLogging"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddCoreAIServices();
        services.AddCoreAIAzureOpenAI();
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AzureClientOptions>>().Value;

        Assert.True(options.EnableLogging);
        Assert.True(options.EnableMessageLogging);
        Assert.False(options.EnableMessageContentLogging);
    }

    [Fact]
    public void AddCoreAIAzureOpenAI_ShouldRegisterAzureConnectionHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddCoreAIServices();
        services.AddCoreAIAzureOpenAI();
        using var serviceProvider = services.BuildServiceProvider();

        var handlers = serviceProvider.GetServices<IAIProviderConnectionHandler>().ToArray();

        Assert.Contains(handlers, handler => handler.GetType().FullName == "CrestApps.Core.AI.OpenAI.Azure.Handlers.AzureOpenAIConnectionHandler");
    }

    [Fact]
    public void AddCoreAIOpenAI_ShouldRegisterOpenAIConnectionHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddCoreAIServices();
        services.AddCoreAIOpenAI();
        using var serviceProvider = services.BuildServiceProvider();

        var handlers = serviceProvider.GetServices<IAIProviderConnectionHandler>().ToArray();

        Assert.Contains(handlers, handler => handler.GetType().FullName == "CrestApps.Core.AI.OpenAI.Handlers.OpenAIConnectionHandler");
    }

    [Fact]
    public async Task AddCrestAppsAI_WhenClientNameIsMissing_ShouldIgnoreConnectionEntry()
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
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var connections = await serviceProvider.GetRequiredService<IAIProviderConnectionStore>().GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Empty(connections);
    }

    [Fact]
    public async Task AddCrestAppsAI_WhenAzureOpenAIClientNameConfigured_ShouldNormalizeToAzureProvider()
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
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var connections = await serviceProvider.GetRequiredService<IAIProviderConnectionStore>().GetAllAsync(TestContext.Current.CancellationToken);
        var connection = Assert.Single(connections);

        Assert.Equal("config-primary", connection.Name);
        Assert.Equal(AzureOpenAIConstants.ClientName, connection.ClientName);
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

        var connections = await store.GetAllAsync(TestContext.Current.CancellationToken);

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

        var connections = await store.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Contains(connections, connection => connection.Name == "config-primary" && connection.ClientName == "OpenAI");
        Assert.Contains(connections, connection => connection.Name == "config-secondary" && connection.ClientName == "OpenAI");
        Assert.Contains(connections, connection => connection.Name == "provider-primary" && connection.ClientName == "OpenAI");
        Assert.Contains(connections, connection => connection.Name == "provider-secondary" && connection.ClientName == AzureOpenAIConstants.ClientName);
    }

    [Fact]
    public async Task AddCrestAppsAI_WhenDisplayTextConfigured_ShouldKeepDisplayText()
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
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var connections = await serviceProvider.GetRequiredService<IAIProviderConnectionStore>().GetAllAsync(TestContext.Current.CancellationToken);
        var connection = Assert.Single(connections);

        Assert.Equal("Config primary", connection.DisplayText);
    }

    [Fact]
    public async Task AddCrestAppsAI_WhenLegacyDeploymentSettingsConfigured_ShouldNormalizeThemIntoConnectionProperties()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "config-primary",
                ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
                ["CrestApps:AI:Connections:0:DefaultDeploymentName"] = "legacy-chat",
                ["CrestApps:AI:Connections:0:DefaultEmbeddingDeploymentName"] = "legacy-embedding",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var connections = await serviceProvider.GetRequiredService<IAIProviderConnectionStore>().GetAllAsync(TestContext.Current.CancellationToken);
        var connection = Assert.Single(connections);

        Assert.Equal("legacy-chat", connection.Properties["ChatDeploymentName"]);
        Assert.Equal("legacy-embedding", connection.Properties["EmbeddingDeploymentName"]);
        Assert.False(connection.Properties.ContainsKey("DefaultDeploymentName"));
        Assert.False(connection.Properties.ContainsKey("DefaultEmbeddingDeploymentName"));
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

        Assert.True(options.Deployments.ContainsKey(AzureOpenAIConstants.AzureSpeechClientName));
        Assert.True(options.Deployments[AzureOpenAIConstants.AzureSpeechClientName].UseContainedConnection);
    }

    [Fact]
    public async Task AddCrestAppsAI_WhenCustomConnectionSectionsConfigured_ShouldExposeEverySectionInConnectionStore()
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
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.Configure<AIProviderConnectionCatalogOptions>(options =>
        {
            options.ConnectionSections.Add("Primary:Connections");
            options.ProviderSections.Add("Secondary:Providers");
        });
        services.AddCoreAIServices();
        using var serviceProvider = services.BuildServiceProvider();

        var connections = await serviceProvider.GetRequiredService<IAIProviderConnectionStore>().GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Contains(connections, connection => connection.Name == "config-primary" && connection.ClientName == "OpenAI");
        Assert.Contains(connections, connection => connection.Name == "azure-secondary" && connection.ClientName == AzureOpenAIConstants.ClientName);
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

        var connections = await store.GetAllAsync(TestContext.Current.CancellationToken);

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

        public ValueTask<IReadOnlyCollection<AIProviderConnection>> GetEntriesAsync(IReadOnlyCollection<AIProviderConnection> knownEntries, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIProviderConnection>>(connections.ToArray());
        }

        public ValueTask CreateAsync(AIProviderConnection entry, CancellationToken cancellationToken = default)
        {
            connections.Add(entry);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(AIProviderConnection entry, CancellationToken cancellationToken = default)
        {
            connections.Remove(entry);
            return ValueTask.FromResult(true);
        }

        public ValueTask UpdateAsync(AIProviderConnection entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
