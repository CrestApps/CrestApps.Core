using System.Globalization;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
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

    /// <summary>
    /// Configuration values are untyped text, but the reader turns any value that looks like a number into a
    /// number. A parameter's <c>DefaultValue</c> and <c>AllowedValues</c> are text, so a default such as
    /// <c>0.7</c> used to make the metadata unreadable and the deployment disappear.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_WhenAParameterValueLooksLikeANumber_ShouldKeepItAsText()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "tuned-chat",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:DefaultValue"] = "0.7",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:Minimum"] = "0",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:Maximum"] = "2",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:seed:AllowedValues:0"] = "1",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:seed:AllowedValues:1"] = "42",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:stream:DefaultValue"] = "true",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        var temperature = Assert.Contains("temperature", metadata.Parameters);
        Assert.Equal("0.7", temperature.DefaultValue);
        Assert.Equal(0, temperature.Minimum);
        Assert.Equal(2, temperature.Maximum);
        Assert.Equal(["1", "42"], Assert.Contains("seed", metadata.Parameters).AllowedValues);
        Assert.Equal("true", Assert.Contains("stream", metadata.Parameters).DefaultValue);
    }

    /// <summary>
    /// A number that is restored to text keeps the exact text it was written with, so an allowed value such as
    /// <c>1.0</c> still matches the value the operator selects. Text that is not a JSON number, such as
    /// <c>007</c>, is never turned into a number.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_WhenAParameterValueIsANumber_ShouldKeepItsExactText()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "tuned-chat",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:DefaultValue"] = "0.70",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:AllowedValues:0"] = "1.0",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:AllowedValues:1"] = "1e3",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:AllowedValues:2"] = "007",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:Step"] = "0.05",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        var temperature = Assert.Contains("temperature", metadata.Parameters);
        Assert.Equal("0.70", temperature.DefaultValue);
        Assert.Equal(["1.0", "1e3", "007"], temperature.AllowedValues);
        Assert.Equal(0.05, temperature.Step);
    }

    /// <summary>
    /// Configuration values are read the same way whatever the server's culture. Reading them with a culture that
    /// separates thousands with <c>.</c> used to turn <c>0.7</c> into <c>7</c> and a minimum of <c>0.25</c> into
    /// <c>25</c>, with no error.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_WhenTheCultureSeparatesThousandsWithADot_ShouldReadNumbersTheSameWay()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "tuned-chat",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:DefaultValue"] = "0.7",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:Minimum"] = "0.25",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:Maximum"] = "1.5",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);
        var originalCulture = CultureInfo.CurrentCulture;
        IEnumerable<AIDeployment> deployments;

        // Act
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        // Assert
        var deployment = Assert.Single(deployments);
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        var temperature = Assert.Contains("temperature", metadata.Parameters);
        Assert.Equal("0.7", temperature.DefaultValue);
        Assert.Equal(0.25, temperature.Minimum);
        Assert.Equal(1.5, temperature.Maximum);
    }

    /// <summary>
    /// One unreadable entry used to surface from the single catch around the whole read, so that entry, every
    /// entry after it, and every deployment declared on a connection were all lost. An unreadable entry is now
    /// skipped on its own.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_WhenOneDeploymentCannotBeRead_ShouldStillLoadTheOthers()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "unreadable-chat",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature"] = "high",
            ["CrestApps:AI:Deployments:1:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:1:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:1:Name"] = "readable-chat",
            ["CrestApps:AI:Deployments:1:Properties:AIDeploymentMetadata:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Connections:0:Name"] = "shared-openai",
            ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Connections:0:ChatDeploymentName"] = "connection-chat",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployments = await store.GetAllAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(deployments, deployment => deployment.Name == "unreadable-chat");
        Assert.Contains(deployments, deployment => deployment.Name == "readable-chat");
        Assert.Contains(deployments, deployment => deployment.Name == "connection-chat");
    }

    /// <summary>
    /// The numeric default must also resolve through the capability service, which is what the editors and
    /// the request pipeline consult, and be accepted as a valid value for a <c>Number</c> parameter.
    /// </summary>
    [Fact]
    public async Task AddCoreAIOpenAI_WhenANumberParameterDefaultIsConfigured_ShouldExposeItThroughTheCapabilityService()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "tuned-chat",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:temperature:DefaultValue"] = "0.7",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddCoreAIServices();
        services.AddCoreAIOpenAI();
        services.AddAIDeploymentParameter("temperature", new LocalizedString("temperature", "Temperature"), parameter =>
        {
            parameter.Kind = AIDeploymentParameterKind.Number;
            parameter.Minimum = 0;
            parameter.Maximum = 2;
        });
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var deploymentStore = scope.ServiceProvider.GetRequiredService<IAIDeploymentStore>();
        var capabilityService = scope.ServiceProvider.GetRequiredService<IAIDeploymentCapabilityService>();

        // Act
        var deployment = Assert.Single(await deploymentStore.GetAllAsync(TestContext.Current.CancellationToken));
        var temperature = capabilityService.GetCapabilities(deployment).GetParameter("temperature");

        // Assert
        Assert.NotNull(temperature);
        Assert.Equal("0.7", temperature.DefaultValue);
        Assert.True(temperature.IsValidValue("0.7"));
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
        AssertDeclaresExactly(chatDeployment, AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.ToolCalling, AIDeploymentFeatureNames.Streaming);
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
        AssertDeclaresExactly(chatDeployment, AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.ToolCalling, AIDeploymentFeatureNames.Streaming);
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
        AssertDeclaresExactly(chatDeployment, AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.ToolCalling, AIDeploymentFeatureNames.Streaming);
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

        // The explicit entry wins over the connection-synthesized one, so it declares only what its own
        // configuration says.
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
        AssertDeclaresExactly(deployments, "gpt-4.1", AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.ToolCalling, AIDeploymentFeatureNames.Streaming);
        AssertDeclaresExactly(deployments, "text-embedding-3-large", AIDeploymentFeatureNames.TextEmbedding);
        AssertDeclaresExactly(deployments, "dall-e-3", AIDeploymentFeatureNames.ImageOutput);
        AssertDeclaresExactly(deployments, "whisper-1", AIDeploymentFeatureNames.SpeechToText);
        AssertDeclaresExactly(deployments, "tts-1", AIDeploymentFeatureNames.TextToSpeech);
    }

    /// <summary>
    /// A chat deployment synthesized from a connection must declare tool calling. Without it every tool
    /// is stripped from every request before it is sent, so the model describes what it is about to do
    /// and then calls nothing — with the cause visible only as a warning in the log. A connection
    /// deployment is read-only in the UI, so an operator cannot declare the feature by hand.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_WhenTheChatDeploymentComesFromAConnection_ShouldDeclareToolCalling()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Connections:0:Name"] = "my-openai",
            ["CrestApps:AI:Connections:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Connections:0:ChatDeploymentName"] = "gpt-4.1-mini",
        }).Build();

        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.True(
            metadata.SupportsFeature(AIDeploymentFeatureNames.ToolCalling),
            "A connection-synthesized chat deployment must declare tool calling, or every tool is stripped from every request.");
    }

    /// <summary>
    /// Azure App Service rejects application setting names longer than 100 characters, and
    /// <c>OrchardCore__CrestApps__AI__Deployments__2__Properties__AIDeploymentMetadata__Parameters__reasoningEffort__DefaultValue</c>
    /// is 119. Declaring <c>Features</c> and <c>Parameters</c> directly on the entry keeps the same setting at 85
    /// characters, so a host configured only through Azure can still declare its model parameters.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_WhenFeaturesAndParametersAreDeclaredOnTheEntry_ShouldReadThemAsCapabilityMetadata()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "reasoning-chat",
            ["CrestApps:AI:Deployments:0:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:0:Features:1"] = AIDeploymentFeatureNames.ToolCalling,
            ["CrestApps:AI:Deployments:0:Features:2"] = AIDeploymentFeatureNames.Reasoning,
            ["CrestApps:AI:Deployments:0:Parameters:reasoningEffort:DefaultValue"] = "None",
            ["CrestApps:AI:Deployments:0:Parameters:reasoningEffort:AllowedValues:0"] = "None",
            ["CrestApps:AI:Deployments:0:Parameters:reasoningEffort:AllowedValues:1"] = "Low",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        AssertDeclaresExactly(
            deployment,
            AIDeploymentFeatureNames.TextGeneration,
            AIDeploymentFeatureNames.ToolCalling,
            AIDeploymentFeatureNames.Reasoning);
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        var reasoningEffort = Assert.Contains(AIDeploymentParameterNames.ReasoningEffort, metadata.Parameters);
        Assert.Equal("None", reasoningEffort.DefaultValue);
        Assert.Equal(["None", "Low"], reasoningEffort.AllowedValues);
        Assert.False(deployment.Properties.ContainsKey("Features"), "The shorthand features should be folded into the metadata, not left as a stray property.");
        Assert.False(deployment.Properties.ContainsKey("Parameters"), "The shorthand parameters should be folded into the metadata, not left as a stray property.");
    }

    /// <summary>
    /// A host can keep the full definition in <c>appsettings.json</c> and override a single value from Azure.
    /// The shorthand therefore adds features and merges parameter fields one key at a time; it never discards
    /// what the nested form declared.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_WhenBothFormsAreDeclared_ShouldCombineFeaturesAndMergeParameterFields()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "reasoning-chat",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Features:1"] = AIDeploymentFeatureNames.ToolCalling,
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:reasoningEffort:DefaultValue"] = "Medium",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:reasoningEffort:AllowedValues:0"] = "None",
            ["CrestApps:AI:Deployments:0:Properties:AIDeploymentMetadata:Parameters:reasoningEffort:AllowedValues:1"] = "Medium",
            ["CrestApps:AI:Deployments:0:Features:0"] = AIDeploymentFeatureNames.Reasoning,
            ["CrestApps:AI:Deployments:0:Features:1"] = "TOOLCALLING",
            ["CrestApps:AI:Deployments:0:Parameters:reasoningEffort:DefaultValue"] = "None",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        AssertDeclaresExactly(
            deployment,
            AIDeploymentFeatureNames.TextGeneration,
            AIDeploymentFeatureNames.ToolCalling,
            AIDeploymentFeatureNames.Reasoning);
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        var reasoningEffort = Assert.Contains(AIDeploymentParameterNames.ReasoningEffort, metadata.Parameters);
        Assert.Equal("None", reasoningEffort.DefaultValue);
        Assert.Equal(["None", "Medium"], reasoningEffort.AllowedValues);
    }

    [Fact]
    public async Task GetAllAsync_WhenAProviderGroupedEntryDeclaresShorthandMetadata_ShouldReadIt()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:OpenAI:0:Name"] = "grouped-reasoning-chat",
            ["CrestApps:AI:Deployments:OpenAI:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:OpenAI:0:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:OpenAI:0:Features:1"] = AIDeploymentFeatureNames.Reasoning,
            ["CrestApps:AI:Deployments:OpenAI:0:Parameters:reasoningEffort:DefaultValue"] = "None",
        }).Build();
        var aiOptions = new AIOptions();
        aiOptions.AddDeploymentProvider("OpenAI");
        var store = CreateStore(configuration, aiOptions);

        // Act
        var deployment = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));

        // Assert
        AssertDeclaresExactly(deployment, AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.Reasoning);
        Assert.True(deployment.TryGet<AIDeploymentMetadata>(out var metadata));
        Assert.Equal("None", Assert.Contains(AIDeploymentParameterNames.ReasoningEffort, metadata.Parameters).DefaultValue);
    }

    /// <summary>
    /// Reading the metadata is not enough on its own: the parameter must also resolve through the capability
    /// service, which is what the chat editors and the request pipeline consult. This proves an operator who
    /// configures the shorthand can actually run the deployment with a reasoning effort of <c>None</c>.
    /// </summary>
    [Fact]
    public async Task AddCoreAIOpenAI_WhenShorthandDeclaresReasoningEffort_ShouldExposeItThroughTheCapabilityService()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["CrestApps:AI:Deployments:0:ClientName"] = "OpenAI",
            ["CrestApps:AI:Deployments:0:ConnectionName"] = "shared-openai",
            ["CrestApps:AI:Deployments:0:Name"] = "reasoning-chat",
            ["CrestApps:AI:Deployments:0:Features:0"] = AIDeploymentFeatureNames.TextGeneration,
            ["CrestApps:AI:Deployments:0:Features:1"] = AIDeploymentFeatureNames.ToolCalling,
            ["CrestApps:AI:Deployments:0:Features:2"] = AIDeploymentFeatureNames.Reasoning,
            ["CrestApps:AI:Deployments:0:Parameters:reasoningEffort:DefaultValue"] = "None",
            ["CrestApps:AI:Deployments:0:Parameters:reasoningEffort:AllowedValues:0"] = "None",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddCoreAIServices();
        services.AddCoreAIOpenAI();
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var deploymentStore = scope.ServiceProvider.GetRequiredService<IAIDeploymentStore>();
        var capabilityService = scope.ServiceProvider.GetRequiredService<IAIDeploymentCapabilityService>();

        // Act
        var deployment = Assert.Single(await deploymentStore.GetAllAsync(TestContext.Current.CancellationToken));
        var capabilities = capabilityService.GetCapabilities(deployment);

        // Assert
        Assert.True(capabilities.SupportsFeature(AIDeploymentFeatureNames.Reasoning));
        var reasoningEffort = capabilities.GetParameter(AIDeploymentParameterNames.ReasoningEffort);
        Assert.NotNull(reasoningEffort);
        Assert.Equal("None", reasoningEffort.DefaultValue);
        Assert.True(reasoningEffort.IsValidValue("None"));
        Assert.False(reasoningEffort.IsValidValue("Medium"));
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
