using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.OrchardCore.Tests.Framework.Mvc;

public sealed class ConfigurationAIDeploymentCatalogTests
{
    [Fact]
    public async Task GetAllAsync_ShouldMergeStoredAndConfiguredStandaloneDeployments()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["CrestApps:AI:Deployments:0:ClientName"] = "AzureSpeech", ["CrestApps:AI:Deployments:0:Name"] = "whisper", ["CrestApps:AI:Deployments:0:Type"] = "SpeechToText", ["CrestApps:AI:Deployments:0:IsDefault"] = "true", ["CrestApps:AI:Deployments:0:Endpoint"] = "https://eastus.stt.speech.microsoft.com", ["CrestApps:AI:Deployments:0:AuthenticationType"] = "ApiKey", ["CrestApps:AI:Deployments:0:ApiKey"] = "secret", }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.SupportsContainedConnection = true);
        var innerStore = new TestAIDeploymentStore([new AIDeployment { ItemId = "ui-deployment", Name = "ui-chat", ClientName = "OpenAI", Type = AIDeploymentType.Chat, },]);
        var catalog = new ConfigurationAIDeploymentStore(innerStore, configuration, Options.Create(aiOptions), Options.Create(new AIDeploymentCatalogOptions()), NullLogger<ConfigurationAIDeploymentStore>.Instance);
        var deployments = await catalog.GetAllAsync();
        Assert.Contains(deployments, deployment => deployment.ItemId == "ui-deployment");
        var configuredDeployment = Assert.Single(deployments, deployment => deployment.Name == "whisper");
        Assert.Equal("AzureSpeech", configuredDeployment.ClientName);
        Assert.Equal(AIDeploymentType.SpeechToText, configuredDeployment.Type);
        Assert.NotNull(configuredDeployment.Properties);
        Assert.Equal("AzureSpeech", configuredDeployment.Properties["ClientName"]?.ToString());
        Assert.Equal("SpeechToText", configuredDeployment.Properties["Type"]?.ToString());
        Assert.Equal("https://eastus.stt.speech.microsoft.com", configuredDeployment.Properties["Endpoint"]?.ToString());
    }

    [Fact]
    public async Task FindByNameAsync_ShouldReturnConfiguredDeploymentWhenNotInStore()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["CrestApps:AI:Deployments:0:ClientName"] = "AzureSpeech", ["CrestApps:AI:Deployments:0:Name"] = "AzureTextToSpeech", ["CrestApps:AI:Deployments:0:Type"] = "TextToSpeech", ["CrestApps:AI:Deployments:0:IsDefault"] = "true", }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.SupportsContainedConnection = true);
        var catalog = new ConfigurationAIDeploymentStore(new TestAIDeploymentStore([]), configuration, Options.Create(aiOptions), Options.Create(new AIDeploymentCatalogOptions()), NullLogger<ConfigurationAIDeploymentStore>.Instance);
        var deployment = await catalog.FindByNameAsync("AzureTextToSpeech");
        Assert.NotNull(deployment);
        Assert.Equal("AzureSpeech", deployment.ClientName);
        Assert.Equal(AIDeploymentType.TextToSpeech, deployment.Type);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReadProviderGroupedStandaloneDeployments()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["CrestApps:AI:Deployments:AzureSpeech:0:Name"] = "grouped-whisper", ["CrestApps:AI:Deployments:AzureSpeech:0:Type"] = "SpeechToText", ["CrestApps:AI:Deployments:AzureSpeech:0:IsDefault"] = "true", }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.SupportsContainedConnection = true);
        var catalog = new ConfigurationAIDeploymentStore(new TestAIDeploymentStore([]), configuration, Options.Create(aiOptions), Options.Create(new AIDeploymentCatalogOptions()), NullLogger<ConfigurationAIDeploymentStore>.Instance);
        var deployment = Assert.Single(await catalog.GetAllAsync());
        Assert.Equal("AzureSpeech", deployment.ClientName);
        Assert.Equal("grouped-whisper", deployment.Name);
        Assert.Equal(AIDeploymentType.SpeechToText, deployment.Type);
    }

    [Fact]
    public async Task GetAllAsync_ShouldPreserveConfiguredClientName()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["CrestApps:AI:Deployments:0:ClientName"] = "AzureOpenAI", ["CrestApps:AI:Deployments:0:Name"] = "text-embedding-3-small", ["CrestApps:AI:Deployments:0:ModelName"] = "text-embedding-3-small", ["CrestApps:AI:Deployments:0:Type"] = "Embedding", ["CrestApps:AI:Deployments:0:Endpoint"] = "https://example.openai.azure.com/", ["CrestApps:AI:Deployments:0:AuthenticationType"] = "ApiKey", ["CrestApps:AI:Deployments:0:ApiKey"] = "secret", }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureOpenAI");
        var catalog = new ConfigurationAIDeploymentStore(new TestAIDeploymentStore([]), configuration, Options.Create(aiOptions), Options.Create(new AIDeploymentCatalogOptions()), NullLogger<ConfigurationAIDeploymentStore>.Instance);
        var deployment = Assert.Single(await catalog.GetAllAsync());
        Assert.Equal("AzureOpenAI", deployment.ClientName);
        Assert.Equal(AIDeploymentType.Embedding, deployment.Type);
    }

    [Fact]
    public async Task GetAllAsync_ShouldLoadStandaloneDeploymentsForProvidersWithoutContainedConnections()
    {
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

        var catalog = new ConfigurationAIDeploymentStore(
            new TestAIDeploymentStore([]),
            configuration,
            Options.Create(aiOptions),
            Options.Create(new AIDeploymentCatalogOptions()),
            NullLogger<ConfigurationAIDeploymentStore>.Instance);

        var deployment = Assert.Single(await catalog.GetAllAsync());

        Assert.Equal("OpenAI", deployment.ClientName);
        Assert.Equal("gpt-4.1", deployment.Name);
        Assert.Equal("gpt-4.1", deployment.ModelName);
        Assert.Equal(AIDeploymentType.Chat, deployment.Type);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReadEveryConfiguredDeploymentSectionAndPreserveConnectionNames()
    {
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
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.SupportsContainedConnection = true);

        var catalogOptions = new AIDeploymentCatalogOptions();
        catalogOptions.DeploymentSections.Clear();
        catalogOptions.DeploymentSections.Add("Primary:Deployments");
        catalogOptions.DeploymentSections.Add("Secondary:Deployments");

        var catalog = new ConfigurationAIDeploymentStore(
            new TestAIDeploymentStore([]),
            configuration,
            Options.Create(aiOptions),
            Options.Create(catalogOptions),
            NullLogger<ConfigurationAIDeploymentStore>.Instance);

        var deployments = await catalog.GetAllAsync();
        var sharedDeployment = Assert.Single(deployments, x => x.Name == "gpt-4.1");
        var containedDeployment = Assert.Single(deployments, x => x.Name == "speech-primary");

        Assert.Equal("shared-primary", sharedDeployment.ConnectionName);
        Assert.Equal(AIConfigurationRecordIds.CreateDeploymentId("OpenAI", "shared-primary", "gpt-4.1"), sharedDeployment.ItemId);
        Assert.Equal("shared-primary", sharedDeployment.Properties["ConnectionName"]?.ToString());
        Assert.Null(containedDeployment.ConnectionName);
        Assert.Equal(AIDeploymentType.SpeechToText, containedDeployment.Type);
    }

    [Fact]
    public async Task GetAllAsync_ShouldPreferStoredDeploymentWhenConfiguredNameConflicts()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "AzureSpeech",
            ["CrestApps:AI:Deployments:0:Name"] = "shared-name",
            ["CrestApps:AI:Deployments:0:Type"] = "SpeechToText",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("AzureSpeech", entry => entry.SupportsContainedConnection = true);
        var catalog = new ConfigurationAIDeploymentStore(
            new TestAIDeploymentStore(
            [
                new AIDeployment
                {
                    ItemId = "ui-deployment",
                    Name = "shared-name",
                    ClientName = "OpenAI",
                    Type = AIDeploymentType.Chat,
                },
            ]),
            configuration,
            Options.Create(aiOptions),
            Options.Create(new AIDeploymentCatalogOptions()),
            NullLogger<ConfigurationAIDeploymentStore>.Instance);

        var deployments = await catalog.GetAllAsync();

        Assert.Single(deployments);
        Assert.Equal("ui-deployment", deployments.Single().ItemId);
    }

    private sealed class TestAIDeploymentStore(List<AIDeployment> deployments) : INamedSourceCatalog<AIDeployment>
    {
        public ValueTask CreateAsync(AIDeployment entry)
        {
            deployments.Add(entry);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(AIDeployment entry)
        {
            deployments.Remove(entry);
            return ValueTask.FromResult(true);
        }

        public ValueTask<AIDeployment> FindByIdAsync(string id)
        {
            return ValueTask.FromResult(deployments.FirstOrDefault(deployment => deployment.ItemId == id));
        }

        public ValueTask<AIDeployment> FindByNameAsync(string name)
        {
            return ValueTask.FromResult(deployments.FirstOrDefault(deployment => deployment.Name == name));
        }

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAllAsync()
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>(deployments.ToArray());
        }

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(IEnumerable<string> ids)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>(deployments.Where(deployment => ids.Contains(deployment.ItemId)).ToArray());
        }

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(string source)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>(deployments.Where(deployment => deployment.Source == source).ToArray());
        }

        public ValueTask<AIDeployment> GetAsync(string name, string source)
        {
            return ValueTask.FromResult(deployments.FirstOrDefault(deployment => deployment.Name == name && deployment.Source == source));
        }

        public ValueTask<PageResult<AIDeployment>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
            where TQuery : QueryContext
        {
            return ValueTask.FromResult(new PageResult<AIDeployment> { Count = deployments.Count, Entries = deployments.ToArray(), });
        }

        public ValueTask UpdateAsync(AIDeployment entry) => ValueTask.CompletedTask;
    }

}
