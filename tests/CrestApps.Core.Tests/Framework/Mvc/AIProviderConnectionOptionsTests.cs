using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Models;
using CrestApps.Core.Mvc.Web.Areas.AI.Controllers;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.OrchardCore.Tests.Framework.Mvc;

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
    public async Task ConfigurationAIProviderConnectionCatalog_GetAllAsync_ShouldMergeStoredAndConfiguredConnections()
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

        var catalog = new ConfigurationAIProviderConnectionCatalog(
            new TestAIProviderConnectionStore(
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
            ]),
            configuration,
            Options.Create(new AIProviderConnectionCatalogOptions()),
            NullLogger<ConfigurationAIProviderConnectionCatalog>.Instance);

        var connections = await catalog.GetAllAsync();

        Assert.Contains(connections, connection => connection.Name == "config-primary" && AIConfigurationRecordIds.IsConfigurationConnectionId(connection.ItemId));
        Assert.Contains(connections, connection => connection.Name == "ui-secondary" && connection.ItemId == "ui-connection");
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
        var deploymentCatalog = new Mock<INamedSourceCatalog<AIDeployment>>();
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
        var deploymentCatalog = new Mock<INamedSourceCatalog<AIDeployment>>();
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
    public async Task ConfigurationAIProviderConnectionCatalog_ShouldSkipConfiguredConflictsByName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:Connections:0:Name"] = "shared-name",
                ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            })
            .Build();

        var catalog = new ConfigurationAIProviderConnectionCatalog(
            new TestAIProviderConnectionStore(
            [
                new AIProviderConnection
                {
                    ItemId = "ui-connection",
                    ClientName = "OpenAI",
                    Name = "shared-name",
                },
            ]),
            configuration,
            Options.Create(new AIProviderConnectionCatalogOptions()),
            NullLogger<ConfigurationAIProviderConnectionCatalog>.Instance);

        var connections = await catalog.GetAllAsync();

        Assert.Single(connections);
        Assert.Equal("ui-connection", connections.Single().ItemId);
    }

    private sealed class TestAIProviderConnectionStore(List<AIProviderConnection> connections) : INamedSourceCatalog<AIProviderConnection>
    {
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

        public ValueTask<AIProviderConnection> FindByIdAsync(string id)
        {
            return ValueTask.FromResult(connections.FirstOrDefault(connection => connection.ItemId == id));
        }

        public ValueTask<AIProviderConnection> FindByNameAsync(string name)
        {
            return ValueTask.FromResult(connections.FirstOrDefault(connection => connection.Name == name));
        }

        public ValueTask<IReadOnlyCollection<AIProviderConnection>> GetAllAsync()
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIProviderConnection>>(connections.ToArray());
        }

        public ValueTask<IReadOnlyCollection<AIProviderConnection>> GetAsync(IEnumerable<string> ids)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIProviderConnection>>(connections.Where(connection => ids.Contains(connection.ItemId)).ToArray());
        }

        public ValueTask<IReadOnlyCollection<AIProviderConnection>> GetAsync(string source)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIProviderConnection>>(connections.Where(connection => connection.Source == source).ToArray());
        }

        public ValueTask<AIProviderConnection> GetAsync(string name, string source)
        {
            return ValueTask.FromResult(connections.FirstOrDefault(connection => connection.Name == name && connection.Source == source));
        }

        public ValueTask<PageResult<AIProviderConnection>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
            where TQuery : QueryContext
        {
            return ValueTask.FromResult(new PageResult<AIProviderConnection> { Count = connections.Count, Entries = connections.ToArray(), });
        }

        public ValueTask UpdateAsync(AIProviderConnection entry) => ValueTask.CompletedTask;
    }
}
