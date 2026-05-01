using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Parsing;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class DataExtractionServiceTests
{
    [Fact]
    public async Task ProcessAsync_WhenExtractionIsDisabled_ShouldReturnNull()
    {
        // Arrange
        var service = CreateService();
        var profile = CreateProfile(settings =>
        {
            settings.EnableDataExtraction = false;
            settings.DataExtractionEntries = [new DataExtractionEntry
            {
                Name = "email"
            }, ];
        });

        // Act
        var result = await service.ProcessAsync(profile, new AIChatSession(), CreatePrompts("hello"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessAsync_WhenPromptCountDoesNotMatchInterval_ShouldReturnNull()
    {
        // Arrange
        var service = CreateService();
        var profile = CreateProfile(settings =>
        {
            settings.EnableDataExtraction = true;
            settings.ExtractionCheckInterval = 2;
            settings.DataExtractionEntries = [new DataExtractionEntry
            {
                Name = "email"
            }, ];
        });

        // Act
        var result = await service.ProcessAsync(profile, new AIChatSession(), CreatePrompts("first"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessAsync_WhenOnlyNonUpdatableFieldsAlreadyExist_ShouldReturnNull()
    {
        // Arrange
        var service = CreateService();
        var profile = CreateProfile(settings =>
        {
            settings.EnableDataExtraction = true;
            settings.ExtractionCheckInterval = 1;
            settings.DataExtractionEntries = [new DataExtractionEntry
            {
                Name = "email",
                AllowMultipleValues = false,
                IsUpdatable = false,
            }, ];
        });

        var session = new AIChatSession
        {
            ExtractedData =
            {
                ["email"] = new ExtractedFieldState
                {
                    Values = ["user@example.com"],
                },
            },
        };

        // Act
        var result = await service.ProcessAsync(profile, session, CreatePrompts("hello"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessAsync_UsesTemplateForExtractionPrompt()
    {
        // Arrange
        var clientFactory = new Mock<IAIClientFactory>();
        var templateService = new Mock<ITemplateService>();
        var deploymentManager = new Mock<IAIDeploymentManager>();
        var chatClient = new Mock<IChatClient>();

        var profile = CreateProfile(settings =>
        {
            settings.EnableDataExtraction = true;
            settings.ExtractionCheckInterval = 1;
            settings.DataExtractionEntries = [new DataExtractionEntry
            {
                Name = "email",
                Description = "The user's email address.",
                IsUpdatable = true,
            }, ];
        });
        profile.UtilityDeploymentName = "utility";

        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentType.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });
        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentType.Chat, null, null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.Is<AIDeployment>(d => d.ClientName == "OpenAI" && d.ConnectionName == "Default" && d.ModelName == "gpt-4.1")))
            .ReturnsAsync(chatClient.Object);

        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtraction, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("system prompt");

        IDictionary<string, object> promptArguments = null;
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IDictionary<string, object>, CancellationToken>((_, arguments, _) => promptArguments = arguments)
            .ReturnsAsync("rendered prompt");

        chatClient.Setup(client => client
            .GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"fields\":[],\"sessionEnded\":false}")));

        var service = new DataExtractionService(
            clientFactory.Object,
            templateService.Object,
            [new DefaultMarkdownTemplateParser()],
            TimeProvider.System,
            NullLogger<DataExtractionService>.Instance,
            deploymentManager.Object);

        // Act
        await service.ProcessAsync(
            profile,
            new AIChatSession(),
            [
                new AIChatSessionPrompt
                {
                    Role = ChatRole.Assistant,
                    Content = "What is your email?",
                },
                new AIChatSessionPrompt
                {
                    Role = ChatRole.User,
                    Content = "My email is test@example.com",
                },
            ],
            TestContext.Current.CancellationToken);

        // Assert
        templateService.Verify(
            service => service.RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotNull(promptArguments);
        Assert.Equal("My email is test@example.com", promptArguments["lastUserMessage"]);
        Assert.Equal("What is your email?", promptArguments["lastAssistantMessage"]);
    }

    [Fact]
    public async Task ProcessAsync_WhenResponseIsMarkdownWrappedJson_ShouldExtractValues()
    {
        // Arrange
        var clientFactory = new Mock<IAIClientFactory>();
        var templateService = new Mock<ITemplateService>();
        var deploymentManager = new Mock<IAIDeploymentManager>();
        var chatClient = new Mock<IChatClient>();
        var profile = CreateProfile(settings =>
        {
            settings.EnableDataExtraction = true;
            settings.ExtractionCheckInterval = 1;
            settings.DataExtractionEntries = [new DataExtractionEntry
            {
                Name = "zipCode",
                Description = "The user's zip code.",
            }, ];
        });
        profile.UtilityDeploymentName = "utility";

        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentType.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.IsAny<AIDeployment>()))
            .ReturnsAsync(chatClient.Object);

        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtraction, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("system prompt");
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("rendered prompt");

        chatClient.Setup(client => client
            .GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                ```json
                {
                  "fields": [
                    {
                      "name": "zipCode",
                      "values": ["89118"],
                      "confidence": 0.99
                    }
                  ],
                  "sessionEnded": false
                }
                ```
                """)));

        var service = CreateService(clientFactory, templateService, deploymentManager);
        var session = new AIChatSession();

        // Act
        var result = await service.ProcessAsync(
            profile,
            session,
            [
                new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = "What is your zip code?" },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "89118" },
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.NewFields);
        Assert.Equal("zipCode", result.NewFields[0].FieldName);
        Assert.Equal("89118", result.NewFields[0].Value);
        Assert.True(session.ExtractedData.TryGetValue("zipCode", out var state));
        Assert.Equal(["89118"], state.Values);
    }

    [Fact]
    public async Task ProcessAsync_WhenAssistantResponseUsesContentsText_ShouldExtractValues()
    {
        // Arrange
        var clientFactory = new Mock<IAIClientFactory>();
        var templateService = new Mock<ITemplateService>();
        var deploymentManager = new Mock<IAIDeploymentManager>();
        var chatClient = new Mock<IChatClient>();
        var profile = CreateProfile(settings =>
        {
            settings.EnableDataExtraction = true;
            settings.ExtractionCheckInterval = 1;
            settings.DataExtractionEntries = [new DataExtractionEntry
            {
                Name = "zipCode",
                Description = "The user's zip code.",
            }, ];
        });
        profile.UtilityDeploymentName = "utility";

        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentType.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.IsAny<AIDeployment>()))
            .ReturnsAsync(chatClient.Object);

        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtraction, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("system prompt");
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("rendered prompt");

        var responseMessage = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("""{"fields":[{"name":"zipCode","values":["89118"],"confidence":0.99}],"sessionEnded":false}""")],
        };

        chatClient.Setup(client => client
            .GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(responseMessage));

        var service = CreateService(clientFactory, templateService, deploymentManager);
        var session = new AIChatSession();

        // Act
        var result = await service.ProcessAsync(
            profile,
            session,
            [
                new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = "What is your zip code?" },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "89118" },
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.NewFields);
        Assert.Equal("89118", result.NewFields[0].Value);
    }

    private static DataExtractionService CreateService()
    {
        var clientFactory = new Mock<IAIClientFactory>();
        var templateService = new Mock<ITemplateService>();
        var deploymentManager = new Mock<IAIDeploymentManager>();

        return CreateService(clientFactory, templateService, deploymentManager);
    }

    private static DataExtractionService CreateService(
        Mock<IAIClientFactory> clientFactory,
        Mock<ITemplateService> templateService,
        Mock<IAIDeploymentManager> deploymentManager)
    {
        return new DataExtractionService(
            clientFactory.Object,
            templateService.Object,
            [new DefaultMarkdownTemplateParser()],
            TimeProvider.System,
            NullLogger<DataExtractionService>.Instance,
            deploymentManager.Object);
    }

    private static AIProfile CreateProfile(Action<AIProfileDataExtractionSettings> configure)
    {
        var profile = new AIProfile();
        profile.AlterSettings(configure);

        return profile;
    }

    private static AIChatSessionPrompt[] CreatePrompts(params string[] userMessages)
    {
        return userMessages.Select(message => new AIChatSessionPrompt
        {
            Role = ChatRole.User,
            Content = message,
        }).ToArray();
    }
}
