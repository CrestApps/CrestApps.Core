using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class ConfigurationAIDeploymentCatalogTests
{
    [Fact]
    public async Task GetAllAsync_ShouldMergeStoredAndConfiguredStandaloneDeployments()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "AzureSpeech",
            ["CrestApps:AI:Deployments:0:Name"] = "whisper",
            ["CrestApps:AI:Deployments:0:Type"] = "SpeechToText",
            ["CrestApps:AI:Deployments:0:IsDefault"] = "true",
            ["CrestApps:AI:Deployments:0:Endpoint"] = "https://eastus.stt.speech.microsoft.com",
            ["CrestApps:AI:Deployments:0:AuthenticationType"] = "ApiKey",
            ["CrestApps:AI:Deployments:0:ApiKey"] = "secret",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.UseContainedConnection = true);
        var store = CreateStore(
            configuration,
            aiOptions,
            dbEntries:
            [
                new AIDeployment
                {
                    ItemId = "ui-deployment",
                    Name = "ui-chat",
                    ClientName = "OpenAI",
                    Capability = AIDeploymentCapability.Chat,
                },
            ]);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(deployments, deployment => deployment.ItemId == "ui-deployment");
        var configuredDeployment = Assert.Single(deployments, deployment => deployment.Name == "whisper");
        Assert.Equal("AzureSpeech", configuredDeployment.ClientName);
        Assert.Equal(AIDeploymentCapability.SpeechToText, configuredDeployment.Capability);
        Assert.NotNull(configuredDeployment.Properties);
        Assert.Equal("AzureSpeech", configuredDeployment.Properties["ClientName"]?.ToString());
        Assert.Equal("SpeechToText", configuredDeployment.Properties["Type"]?.ToString());
        Assert.Equal("https://eastus.stt.speech.microsoft.com", configuredDeployment.Properties["Endpoint"]?.ToString());
    }

    [Fact]
    public async Task FindByNameAsync_ShouldReturnConfiguredDeploymentWhenNotInStore()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "AzureSpeech",
            ["CrestApps:AI:Deployments:0:Name"] = "AzureTextToSpeech",
            ["CrestApps:AI:Deployments:0:Type"] = "TextToSpeech",
            ["CrestApps:AI:Deployments:0:IsDefault"] = "true",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.UseContainedConnection = true);
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = await store.FindByNameAsync("AzureTextToSpeech", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(deployment);
        Assert.Equal("AzureSpeech", deployment.ClientName);
        Assert.Equal(AIDeploymentCapability.TextToSpeech, deployment.Capability);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReadProviderGroupedStandaloneDeployments()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:AzureSpeech:0:Name"] = "grouped-whisper",
            ["CrestApps:AI:Deployments:AzureSpeech:0:Type"] = "SpeechToText",
            ["CrestApps:AI:Deployments:AzureSpeech:0:IsDefault"] = "true",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.UseContainedConnection = true);
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("AzureSpeech", deployment.ClientName);
        Assert.Equal("grouped-whisper", deployment.Name);
        Assert.Equal(AIDeploymentCapability.SpeechToText, deployment.Capability);
    }

    [Fact]
    public async Task GetAllAsync_ShouldNormalizeAzureOpenAIAliasToAzure()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "AzureOpenAI",
            ["CrestApps:AI:Deployments:0:Name"] = "text-embedding-3-small",
            ["CrestApps:AI:Deployments:0:ModelName"] = "text-embedding-3-small",
            ["CrestApps:AI:Deployments:0:Type"] = "Embedding",
            ["CrestApps:AI:Deployments:0:Endpoint"] = "https://example.openai.azure.com/",
            ["CrestApps:AI:Deployments:0:AuthenticationType"] = "ApiKey",
            ["CrestApps:AI:Deployments:0:ApiKey"] = "secret",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider(AzureOpenAIConstants.ClientName);
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(AzureOpenAIConstants.ClientName, deployment.ClientName);
        Assert.Equal(AIDeploymentCapability.Embedding, deployment.Capability);
    }

    [Fact]
    public async Task GetAllAsync_ShouldLoadStandaloneDeploymentsForProvidersWithoutContainedConnections()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:Name"] = "gpt-4.1",
            ["CrestApps:AI:Deployments:0:ModelName"] = "gpt-4.1",
            ["CrestApps:AI:Deployments:0:Type"] = "Chat",
            ["CrestApps:AI:Deployments:0:IsDefault"] = "true",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");

        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("OpenAI", deployment.ClientName);
        Assert.Equal("gpt-4.1", deployment.Name);
        Assert.Equal("gpt-4.1", deployment.ModelName);
        Assert.Equal(AIDeploymentCapability.Chat, deployment.Capability);
    }

    [Fact]
    public async Task AddCoreAIOpenAI_WhenDeploymentConfigured_ShouldExposeItInDeploymentStore()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "gpt-4.1",
            ["CrestApps:AI:Deployments:0:ModelName"] = "gpt-4.1",
            ["CrestApps:AI:Deployments:0:Type"] = "Chat",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddCoreAIServices();
        services.AddCoreAIOpenAI();
        using var serviceProvider = services.BuildServiceProvider();

        var deploymentStore = serviceProvider.GetRequiredService<IAIDeploymentStore>();
        var deployment = Assert.Single(await deploymentStore.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal("gpt-4.1", deployment.Name);
        Assert.Equal("OpenAI", deployment.ClientName);
        Assert.Equal("shared-openai", deployment.ConnectionName);
        Assert.True(deployment.IsReadOnly);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReadEveryConfiguredDeploymentSectionAndPreserveConnectionNames()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Primary:Deployments:0:ClientName"] = "OpenAI",
            ["Primary:Deployments:0:ConnectionName"] = "shared-primary",
            ["Primary:Deployments:0:Name"] = "gpt-4.1",
            ["Primary:Deployments:0:ModelName"] = "gpt-4.1",
            ["Primary:Deployments:0:Type"] = "Chat",
            ["Secondary:Deployments:AzureSpeech:0:Name"] = "speech-primary",
            ["Secondary:Deployments:AzureSpeech:0:Type"] = "SpeechToText",
            ["Secondary:Deployments:AzureSpeech:0:Endpoint"] = "https://example.cognitiveservices.azure.com/",
            ["Secondary:Deployments:AzureSpeech:0:AuthenticationType"] = "ApiKey",
            ["Secondary:Deployments:AzureSpeech:0:ApiKey"] = "secret",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.UseContainedConnection = true);

        var catalogOptions = new AIDeploymentCatalogOptions();
        catalogOptions.DeploymentSections.Clear();
        catalogOptions.DeploymentSections.Add("Primary:Deployments");
        catalogOptions.DeploymentSections.Add("Secondary:Deployments");

        var store = CreateStore(configuration, aiOptions, catalogOptions: catalogOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        var sharedDeployment = Assert.Single(deployments, x => x.Name == "gpt-4.1");
        var containedDeployment = Assert.Single(deployments, x => x.Name == "speech-primary");
        Assert.Equal("shared-primary", sharedDeployment.ConnectionName);
        Assert.Equal(AIConfigurationRecordIds.CreateDeploymentId("OpenAI", "shared-primary", "gpt-4.1"), sharedDeployment.ItemId);
        Assert.Equal("shared-primary", sharedDeployment.Properties["ConnectionName"]?.ToString());
        Assert.Null(containedDeployment.ConnectionName);
        Assert.Equal(AIDeploymentCapability.SpeechToText, containedDeployment.Capability);
    }

    [Fact]
    public async Task GetAllAsync_ShouldPreferStoredDeploymentWhenConfiguredNameConflicts()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "AzureSpeech",
            ["CrestApps:AI:Deployments:0:Name"] = "shared-name",
            ["CrestApps:AI:Deployments:0:Type"] = "SpeechToText",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.UseContainedConnection = true);
        var store = CreateStore(
            configuration,
            aiOptions,
            dbEntries:
            [
                new AIDeployment
                {
                    ItemId = "ui-deployment",
                    Name = "shared-name",
                    ClientName = "OpenAI",
                    Capability = AIDeploymentCapability.Chat,
                },
            ]);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(deployments);
        Assert.Equal("ui-deployment", deployments.Single().ItemId);
    }

    [Fact]
    public async Task GetAllAsync_ShouldCreateDeploymentsFromProviderSectionConnectionDeploymentNames()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Providers:Azure:Connections:test1:Endpoint"] = "https://test1.openai.azure.com/",
            ["CrestApps:AI:Providers:Azure:Connections:test1:AuthenticationType"] = "ApiKey",
            ["CrestApps:AI:Providers:Azure:Connections:test1:ApiKey"] = "secret",
            ["CrestApps:AI:Providers:Azure:Connections:test1:DefaultDeploymentName"] = "gpt-4.1-mini",
            ["CrestApps:AI:Providers:Azure:Connections:test1:DefaultEmbeddingDeploymentName"] = "text-embedding-3-small",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider(AzureOpenAIConstants.ClientName);
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        var chatDeployment = Assert.Single(deployments, d => d.Name == "gpt-4.1-mini");
        Assert.Equal(AzureOpenAIConstants.ClientName, chatDeployment.ClientName);
        Assert.Equal("test1", chatDeployment.ConnectionName);
        Assert.Equal(AIDeploymentCapability.Chat | AIDeploymentCapability.Utility, chatDeployment.Capability);
        Assert.True(chatDeployment.IsReadOnly);

        var embeddingDeployment = Assert.Single(deployments, d => d.Name == "text-embedding-3-small");
        Assert.Equal(AzureOpenAIConstants.ClientName, embeddingDeployment.ClientName);
        Assert.Equal("test1", embeddingDeployment.ConnectionName);
        Assert.Equal(AIDeploymentCapability.Embedding, embeddingDeployment.Capability);
        Assert.True(embeddingDeployment.IsReadOnly);
    }

    [Fact]
    public async Task GetAllAsync_ShouldCreateDeploymentsFromTopLevelConnectionSectionDeploymentNames()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Connections:0:Name"] = "my-openai",
            ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Connections:0:DefaultDeploymentName"] = "gpt-4.1",
            ["CrestApps:AI:Connections:0:DefaultEmbeddingDeploymentName"] = "text-embedding-3-large",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        var chatDeployment = Assert.Single(deployments, d => d.Name == "gpt-4.1");
        Assert.Equal("OpenAI", chatDeployment.ClientName);
        Assert.Equal("my-openai", chatDeployment.ConnectionName);
        Assert.Equal(AIDeploymentCapability.Chat | AIDeploymentCapability.Utility, chatDeployment.Capability);
        Assert.True(chatDeployment.IsReadOnly);

        var embeddingDeployment = Assert.Single(deployments, d => d.Name == "text-embedding-3-large");
        Assert.Equal("OpenAI", embeddingDeployment.ClientName);
        Assert.Equal("my-openai", embeddingDeployment.ConnectionName);
        Assert.Equal(AIDeploymentCapability.Embedding, embeddingDeployment.Capability);
        Assert.True(embeddingDeployment.IsReadOnly);
    }

    [Fact]
    public async Task GetAllAsync_ShouldCreateDeploymentsFromCustomProviderSections()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:CrestApps_AI:Providers:Azure:Connections:test1:Endpoint"] = "https://test1.openai.azure.com/",
            ["CrestApps:CrestApps_AI:Providers:Azure:Connections:test1:DefaultDeploymentName"] = "gpt-4.1-mini",
            ["CrestApps:CrestApps_AI:Providers:Azure:Connections:test1:DefaultEmbeddingDeploymentName"] = "text-embedding-3-small",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider(AzureOpenAIConstants.ClientName);

        var connectionCatalogOptions = new AIProviderConnectionCatalogOptions();
        connectionCatalogOptions.ProviderSections.Add("CrestApps:CrestApps_AI:Providers");

        var store = CreateStore(configuration, aiOptions, connectionCatalogOptions: connectionCatalogOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        var chatDeployment = Assert.Single(deployments, d => d.Name == "gpt-4.1-mini");
        Assert.Equal(AzureOpenAIConstants.ClientName, chatDeployment.ClientName);
        Assert.Equal("test1", chatDeployment.ConnectionName);
        Assert.Equal(AIDeploymentCapability.Chat | AIDeploymentCapability.Utility, chatDeployment.Capability);
        Assert.True(chatDeployment.IsReadOnly);

        var embeddingDeployment = Assert.Single(deployments, d => d.Name == "text-embedding-3-small");
        Assert.Equal(AzureOpenAIConstants.ClientName, embeddingDeployment.ClientName);
        Assert.Equal("test1", embeddingDeployment.ConnectionName);
        Assert.Equal(AIDeploymentCapability.Embedding, embeddingDeployment.Capability);
        Assert.True(embeddingDeployment.IsReadOnly);
    }

    [Fact]
    public async Task GetAllAsync_ShouldNotDuplicateConnectionDeploymentsAlreadyInExplicitSection()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "Azure",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "test1",
            ["CrestApps:AI:Deployments:0:Name"] = "gpt-4.1-mini",
            ["CrestApps:AI:Deployments:0:Type"] = "Chat",
            ["CrestApps:AI:Providers:Azure:Connections:test1:DefaultDeploymentName"] = "gpt-4.1-mini",
            ["CrestApps:AI:Providers:Azure:Connections:test1:DefaultEmbeddingDeploymentName"] = "text-embedding-3-small",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider(AzureOpenAIConstants.ClientName);
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        var chatDeployment = Assert.Single(deployments, d => d.Name == "gpt-4.1-mini");
        Assert.Equal(AIDeploymentCapability.Chat, chatDeployment.Capability);

        var embeddingDeployment = Assert.Single(deployments, d => d.Name == "text-embedding-3-small");
        Assert.Equal(AIDeploymentCapability.Embedding, embeddingDeployment.Capability);
    }

    private static DefaultAIDeploymentStore CreateStore(
        IConfiguration configuration,
        AIOptions aiOptions,
        AIDeploymentCatalogOptions catalogOptions = null,
        AIProviderConnectionCatalogOptions connectionCatalogOptions = null,
        List<AIDeployment> dbEntries = null)
    {
        var sources = new List<INamedSourceCatalogSource<AIDeployment>>();

        if (dbEntries is { Count: > 0 })
        {
            sources.Add(new TestAIDeploymentSource(dbEntries));
        }

        sources.Add(new ConfigurationAIDeploymentSource(
            configuration,
            TimeProvider.System,
            Options.Create(aiOptions),
            Options.Create(catalogOptions ?? new AIDeploymentCatalogOptions()),
            Options.Create(connectionCatalogOptions ?? new AIProviderConnectionCatalogOptions()),
            NullLogger<ConfigurationAIDeploymentSource>.Instance));

        return new DefaultAIDeploymentStore(sources);
    }

    private sealed class TestAIDeploymentSource(List<AIDeployment> deployments) : IWritableNamedSourceCatalogSource<AIDeployment>
    {
        public int Order => 0;

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetEntriesAsync(IReadOnlyCollection<AIDeployment> knownEntries, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>(deployments.ToArray());
        }

        public ValueTask CreateAsync(AIDeployment entry, CancellationToken cancellationToken = default)
        {
            deployments.Add(entry);

            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(AIDeployment entry, CancellationToken cancellationToken = default)
        {
            deployments.Remove(entry);

            return ValueTask.FromResult(true);
        }

        public ValueTask UpdateAsync(AIDeployment entry, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
