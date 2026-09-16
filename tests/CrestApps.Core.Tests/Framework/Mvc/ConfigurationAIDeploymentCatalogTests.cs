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
using BlazorDeploymentViewModel = CrestApps.Core.Blazor.Web.ViewModels.AIDeploymentViewModel;
using MvcDeploymentViewModel = CrestApps.Core.Mvc.Web.Areas.AI.ViewModels.AIDeploymentViewModel;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class ConfigurationAIDeploymentCatalogTests
{
    // What a connection's chat or utility deployment name is expected to stand for. Written out here rather
    // than read from the source so the test still fails if the source narrows the set again.
    private static readonly string[] _expectedChatModelFeatures =
    [
        AIDeploymentFeatureNames.TextGeneration,
        AIDeploymentFeatureNames.ToolCalling,
        AIDeploymentFeatureNames.Streaming,
    ];

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
                },
            ]);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(deployments, deployment => deployment.ItemId == "ui-deployment");
        var configuredDeployment = Assert.Single(deployments, deployment => deployment.Name == "whisper");
        Assert.Equal("AzureSpeech", configuredDeployment.ClientName);
        AssertDeclaresExactly(configuredDeployment, AIDeploymentFeatureNames.SpeechToText);
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
        AssertDeclaresExactly(deployment, AIDeploymentFeatureNames.TextToSpeech);
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
        AssertDeclaresExactly(deployment, AIDeploymentFeatureNames.SpeechToText);
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
        AssertDeclaresExactly(deployment, AIDeploymentFeatureNames.TextEmbedding);
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
        AssertDeclaresExactly(deployment, AIDeploymentFeatureNames.TextGeneration);
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
        AssertDeclaresExactly(containedDeployment, AIDeploymentFeatureNames.SpeechToText);
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
        AssertDeclaresExactly(chatDeployment, _expectedChatModelFeatures);
        Assert.True(chatDeployment.IsReadOnly);

        var embeddingDeployment = Assert.Single(deployments, d => d.Name == "text-embedding-3-small");
        Assert.Equal(AzureOpenAIConstants.ClientName, embeddingDeployment.ClientName);
        Assert.Equal("test1", embeddingDeployment.ConnectionName);
        AssertDeclaresExactly(embeddingDeployment, AIDeploymentFeatureNames.TextEmbedding);
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
        AssertDeclaresExactly(chatDeployment, _expectedChatModelFeatures);
        Assert.True(chatDeployment.IsReadOnly);

        var embeddingDeployment = Assert.Single(deployments, d => d.Name == "text-embedding-3-large");
        Assert.Equal("OpenAI", embeddingDeployment.ClientName);
        Assert.Equal("my-openai", embeddingDeployment.ConnectionName);
        AssertDeclaresExactly(embeddingDeployment, AIDeploymentFeatureNames.TextEmbedding);
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
        AssertDeclaresExactly(chatDeployment, _expectedChatModelFeatures);
        Assert.True(chatDeployment.IsReadOnly);

        var embeddingDeployment = Assert.Single(deployments, d => d.Name == "text-embedding-3-small");
        Assert.Equal(AzureOpenAIConstants.ClientName, embeddingDeployment.ClientName);
        Assert.Equal("test1", embeddingDeployment.ConnectionName);
        AssertDeclaresExactly(embeddingDeployment, AIDeploymentFeatureNames.TextEmbedding);
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
        // The explicit entry is read first and the connection's synonym is dropped as a duplicate, so the
        // declared feature set survives untouched rather than being widened to the chat model set.
        var chatDeployment = Assert.Single(deployments, d => d.Name == "gpt-4.1-mini");
        AssertDeclaresExactly(chatDeployment, AIDeploymentFeatureNames.TextGeneration);

        var embeddingDeployment = Assert.Single(deployments, d => d.Name == "text-embedding-3-small");
        AssertDeclaresExactly(embeddingDeployment, AIDeploymentFeatureNames.TextEmbedding);
    }


    [Fact]
    public async Task GetAllAsync_WhenConnectionDeclaresEveryDeploymentName_ShouldEmitTheMatchingCapabilities()
    {
        // Arrange. A connection-synthesized deployment is read-only in the UI, so an operator cannot declare
        // its capabilities by hand — the source has to emit them. Text to speech is included because the
        // source had no case for it at all.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Connections:0:Name"] = "my-openai",
            ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Connections:0:DefaultDeploymentName"] = "gpt-4.1",
            ["CrestApps:AI:Connections:0:DefaultEmbeddingDeploymentName"] = "text-embedding-3-large",
            ["CrestApps:AI:Connections:0:ImagesDeploymentName"] = "dall-e-3",
            ["CrestApps:AI:Connections:0:SpeechToTextDeploymentName"] = "whisper-1",
            ["CrestApps:AI:Connections:0:TextToSpeechDeploymentName"] = "tts-1",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        AssertDeclaresExactly(deployments, "gpt-4.1", _expectedChatModelFeatures);
        AssertDeclaresExactly(deployments, "text-embedding-3-large", AIDeploymentFeatureNames.TextEmbedding);
        AssertDeclaresExactly(deployments, "dall-e-3", AIDeploymentFeatureNames.ImageOutput);
        AssertDeclaresExactly(deployments, "whisper-1", AIDeploymentFeatureNames.SpeechToText);
        AssertDeclaresExactly(deployments, "tts-1", AIDeploymentFeatureNames.TextToSpeech);
    }

    [Fact]
    public async Task GetAllAsync_WhenConnectionNamesChatAndUtilityDeployments_ShouldDeclareToolCallingAndStreaming()
    {
        // Arrange. Capability enforcement only leaves a deployment alone when it declares no metadata, and a
        // connection-synthesized deployment always declares some, so enforcement is unavoidable for it. A set
        // narrowed to text generation therefore strips every tool from the request and completes a streaming
        // call in one piece — silently, and with no editable field the operator could use to correct it.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Connections:0:Name"] = "my-openai",
            ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Connections:0:ChatDeploymentName"] = "chat-model",
            ["CrestApps:AI:Connections:0:UtilityDeploymentName"] = "utility-model",
            ["CrestApps:AI:Connections:0:EmbeddingDeploymentName"] = "embedding-model",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        AssertDeclaresExactly(deployments, "chat-model", _expectedChatModelFeatures);
        AssertDeclaresExactly(deployments, "utility-model", _expectedChatModelFeatures);
        AssertDeclaresExactly(deployments, "embedding-model", AIDeploymentFeatureNames.TextEmbedding);
    }

    [Fact]
    public async Task GetAllAsync_WhenDeploymentIsDeclaredInTheDeploymentsSection_ShouldKeepOnlyTheDeclaredFeatures()
    {
        // Arrange. The widened set belongs to the synthesized path only. An entry in the Deployments section
        // says what it supports, so it keeps exactly that even when a connection in the same configuration
        // names a chat deployment of its own.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "my-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "declared-chat-model",
            ["CrestApps:AI:Deployments:0:Type"] = "Chat",
            ["CrestApps:AI:Connections:0:Name"] = "my-openai",
            ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Connections:0:ChatDeploymentName"] = "connection-chat-model",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        AssertDeclaresExactly(deployments, "declared-chat-model", AIDeploymentFeatureNames.TextGeneration);
        AssertDeclaresExactly(deployments, "connection-chat-model", _expectedChatModelFeatures);
    }

    [Fact]
    public async Task DeploymentListings_WhenAConfiguredChatDeploymentDeclaresOnlyTextGeneration_ShouldNameWhatEnforcementRemoves()
    {
        // Arrange. This is the shape the outage came from: a configuration-synthesized chat deployment whose
        // declaration stops at text generation. It is read-only in both hosts, so the operator can neither
        // widen it nor opt out of enforcement, and the only record of the loss used to be a log warning.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "my-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "narrow-chat-model",
            ["CrestApps:AI:Deployments:0:Type"] = "Chat",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        AssertDeclaresExactly(deployment, AIDeploymentFeatureNames.TextGeneration);
        Assert.True(deployment.IsReadOnly, "The operator can only be told about the loss if the record is the read-only kind.");
        AssertBothListingsReport(deployment, AIDeploymentFeatureNames.ToolCalling, AIDeploymentFeatureNames.Streaming);
    }

    [Fact]
    public async Task DeploymentListings_ShouldReportNothingWhenEnforcementTakesNothingAway()
    {
        // Arrange. A warning on every row would be worth as little as the log warning was. A connection-named
        // chat deployment declares the whole chat set, an embedding deployment never reaches the chat
        // enforcement path, and a stored deployment that declares no metadata at all stays unconstrained.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Connections:0:Name"] = "my-openai",
            ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Connections:0:ChatDeploymentName"] = "chat-model",
            ["CrestApps:AI:Connections:0:EmbeddingDeploymentName"] = "embedding-model",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(
            configuration,
            aiOptions,
            dbEntries:
            [
                new AIDeployment
                {
                    ItemId = "ui-deployment",
                    Name = "undeclared-model",
                    ClientName = "OpenAI",
                },
            ]);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        AssertBothListingsReport(Assert.Single(deployments, d => d.Name == "chat-model"));
        AssertBothListingsReport(Assert.Single(deployments, d => d.Name == "embedding-model"));

        var undeclared = Assert.Single(deployments, d => d.Name == "undeclared-model");
        Assert.False(undeclared.TryGet<AIDeploymentMetadata>(out _), "The unconstrained case only holds while the record declares nothing.");
        AssertBothListingsReport(undeclared);
    }

    /// <summary>
    /// Asserts that both hosts' deployment listings name exactly the given undeclared features, and that each
    /// one carries badge text and a description an operator can act on.
    /// </summary>
    private static void AssertBothListingsReport(AIDeployment deployment, params string[] expectedFeatures)
    {
        AssertListingReports(
            "MVC",
            [.. MvcDeploymentViewModel.FromDeployment(deployment).GetEnforcedLimits().Select(static limit => (limit.FeatureName, limit.Label, limit.Description))],
            expectedFeatures);

        AssertListingReports(
            "Blazor",
            [.. BlazorDeploymentViewModel.FromDeployment(deployment).GetEnforcedLimits().Select(static limit => (limit.FeatureName, limit.Label, limit.Description))],
            expectedFeatures);
    }

    private static void AssertListingReports(
        string host,
        List<(string FeatureName, string Label, string Description)> limits,
        string[] expectedFeatures)
    {
        Assert.Equal(expectedFeatures, limits.Select(static limit => limit.FeatureName));

        foreach (var limit in limits)
        {
            Assert.False(string.IsNullOrWhiteSpace(limit.Label), $"The {host} listing gave '{limit.FeatureName}' no badge text.");

            // The description has to name the capability the declaration is missing, because that name is the
            // only thing the operator can go and add to the configuration.
            Assert.Contains(limit.FeatureName, limit.Description, StringComparison.Ordinal);
        }
    }

    private static void AssertDeclaresExactly(IEnumerable<AIDeployment> deployments, string name, params string[] expectedFeatures)
    {
        AssertDeclaresExactly(Assert.Single(deployments, d => d.Name == name), expectedFeatures);
    }

    private static void AssertDeclaresExactly(AIDeployment deployment, params string[] expectedFeatures)
    {
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata), $"'{deployment.Name}' declares no capability metadata.");
        Assert.Equal(expectedFeatures, metadata.Features);
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
