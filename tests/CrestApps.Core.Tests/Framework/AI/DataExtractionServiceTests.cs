using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Parsing;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
            settings.DataExtractionEntries =
            [
                new DataExtractionEntry
                {
                    Name = "email",
                    Description = "The user's email address.",
                    IsUpdatable = true,
                },
                new DataExtractionEntry
                {
                    Name = "phone",
                    Description = "The user's phone number.",
                    AllowMultipleValues = true,
                },
            ];
        });
        profile.UtilityDeploymentName = "utility";

        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentPurpose.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });
        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentPurpose.Chat, null, null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(
                It.Is<AIDeployment>(d => d.ClientName == "OpenAI" && d.ConnectionName == "Default" && d.ModelName == "gpt-4.1"),
                It.IsAny<Action<ChatClientBuilder>>()))
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
            new AIChatSession
            {
                ExtractedData =
                {
                    ["email"] = new ExtractedFieldState
                    {
                        Values = ["existing@example.com"],
                    },
                    ["phone"] = new ExtractedFieldState(),
                },
            },
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
        Assert.Equal(
            ["fields", "currentState", "lastUserMessage", "lastAssistantMessage"],
            promptArguments.Keys);
        Assert.Equal(
            """
            [{"Name":"email","Description":"The user\u0027s email address.","AllowMultipleValues":false,"IsUpdatable":true},{"Name":"phone","Description":"The user\u0027s phone number.","AllowMultipleValues":true,"IsUpdatable":false}]
            """,
            JsonSerializer.Serialize(promptArguments["fields"]));
        Assert.Equal(
            """[{"Name":"email","Values":["existing@example.com"]}]""",
            JsonSerializer.Serialize(promptArguments["currentState"]));
        Assert.Equal("My email is test@example.com", promptArguments["lastUserMessage"]);
        Assert.Equal("What is your email?", promptArguments["lastAssistantMessage"]);
    }

    [Fact]
    public async Task ProcessAsync_WhenNoAssistantPrecedesLastUserMessage_ShouldOmitAssistantPromptArgument()
    {
        // Arrange
        var templateService = new Mock<ITemplateService>();
        IDictionary<string, object> promptArguments = null;
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IDictionary<string, object>, CancellationToken>((_, arguments, _) => promptArguments = arguments)
            .ReturnsAsync("rendered prompt");

        var service = new DataExtractionService(
            Mock.Of<IAIClientFactory>(),
            templateService.Object,
            [new DefaultMarkdownTemplateParser()],
            TimeProvider.System,
            NullLogger<DataExtractionService>.Instance);
        var profile = CreateProfile(settings =>
        {
            settings.EnableDataExtraction = true;
            settings.DataExtractionEntries =
            [
               new DataExtractionEntry
               {
                   Name = "email",
                   Description = "The user's email address.",
               },
            ];
        });

        // Act
        await service.ProcessAsync(
            profile,
            new AIChatSession(),
            [new AIChatSessionPrompt { Role = ChatRole.User, Content = "test@example.com" }],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(promptArguments);
        Assert.Equal(["fields", "currentState", "lastUserMessage"], promptArguments.Keys);
        Assert.False(promptArguments.ContainsKey("lastAssistantMessage"));
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
            .ResolveOrDefaultAsync(AIDeploymentPurpose.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<ChatClientBuilder>>()))
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
            .ResolveOrDefaultAsync(AIDeploymentPurpose.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<ChatClientBuilder>>()))
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

    [Fact]
    public async Task ProcessAsync_WhenResponseFieldUsesCamelCaseAlias_ShouldMatchConfiguredSnakeCaseField()
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
            settings.DataExtractionEntries =
            [
                new DataExtractionEntry
                {
                    Name = "first_name",
                    Description = "The customer's first name.",
                },
                new DataExtractionEntry
                {
                    Name = "last_name",
                    Description = "The customer's last name.",
                },
            ];
        });
        profile.UtilityDeploymentName = "utility";

        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentPurpose.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<ChatClientBuilder>>()))
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
                {
                  "fields": [
                    {
                      "name": "firstName",
                      "values": ["Mike"],
                      "confidence": 0.99
                    },
                    {
                      "name": "lastName",
                      "values": ["Smith"],
                      "confidence": 0.99
                    }
                  ],
                  "sessionEnded": false
                }
                """)));

        var service = CreateService(clientFactory, templateService, deploymentManager);
        var session = new AIChatSession();

        // Act
        var result = await service.ProcessAsync(
            profile,
            session,
            [
                new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = "What is your full name?" },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "Mike Smith" },
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.NewFields.Count);
        Assert.True(session.ExtractedData.TryGetValue("first_name", out var firstNameState));
        Assert.Equal(["Mike"], firstNameState.Values);
        Assert.True(session.ExtractedData.TryGetValue("last_name", out var lastNameState));
        Assert.Equal(["Smith"], lastNameState.Values);
    }

    [Fact]
    public async Task ProcessAsync_WhenConfiguredFieldIsCustomerName_ShouldCombineFirstAndLastNameResponses()
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
            settings.DataExtractionEntries =
            [
                new DataExtractionEntry
                {
                    Name = "customer_name",
                    Description = "The customer first, last or full name.",
                    IsUpdatable = true,
                },
            ];
        });
        profile.UtilityDeploymentName = "utility";

        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentPurpose.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<ChatClientBuilder>>()))
            .ReturnsAsync(chatClient.Object);

        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtraction, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("system prompt");
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("rendered prompt");

        chatClient.SetupSequence(client => client
                .GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                {
                  "fields": [
                    {
                      "name": "firstName",
                      "values": ["Mike"],
                      "confidence": 0.99
                    }
                  ],
                  "sessionEnded": false
                }
                """)))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                {
                  "fields": [
                    {
                      "name": "last name",
                      "values": ["Smith"],
                      "confidence": 0.99
                    }
                  ],
                  "sessionEnded": false
                }
                """)));

        var service = CreateService(clientFactory, templateService, deploymentManager);
        var session = new AIChatSession();

        // Act
        await service.ProcessAsync(
            profile,
            session,
            [
                new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = "What is your first name?" },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "Mike" },
            ],
            TestContext.Current.CancellationToken);

        await service.ProcessAsync(
            profile,
            session,
            [
                new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = "What is your first name?" },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "Mike" },
                new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = "What is your last name?" },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "Smith" },
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.ExtractedData.TryGetValue("customer_name", out var state));
        Assert.Equal(["Mike Smith"], state.Values);
    }

    [Fact]
    public async Task ProcessAsync_WhenConfiguredFieldIsCustomerPhone_ShouldMatchPhoneNumberAlias()
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
            settings.DataExtractionEntries =
            [
                new DataExtractionEntry
                {
                    Name = "customer_phone",
                    Description = "Customer phone number.",
                    IsUpdatable = true,
                },
            ];
        });
        profile.UtilityDeploymentName = "utility";

        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentPurpose.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });

        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<ChatClientBuilder>>()))
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
                {
                  "fields": [
                    {
                      "name": "phone number",
                      "values": ["7024993350"],
                      "confidence": 0.99
                    }
                  ],
                  "sessionEnded": false
                }
                """)));

        var service = CreateService(clientFactory, templateService, deploymentManager);
        var session = new AIChatSession();

        // Act
        var result = await service.ProcessAsync(
            profile,
            session,
            [
                new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = "What is the best phone number for the team to reach you?" },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "7024993350" },
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(session.ExtractedData.TryGetValue("customer_phone", out var state));
        Assert.Equal(["7024993350"], state.Values);
    }

    [Fact]
    public async Task ProcessAsync_WhenDirectNormalizedAndSemanticMatchesExist_ShouldUseDirectMatch()
    {
        // Arrange
        DataExtractionEntry[] entries =
        [
            new()
            {
                Name = "Customer Name",
                Description = "The customer's full name.",
            },
            new()
            {
                Name = "customer_name",
                Description = "The exact customer name field.",
            },
            new()
            {
                Name = "display_name",
                Description = "The customer's full name.",
            },
        ];

        // Act
        var (result, session) = await ProcessExtractionAsync(
            entries,
            """{"fields":[{"name":"customer_name","values":["Direct"],"confidence":0.99}],"sessionEnded":false}""");

        // Assert
        Assert.Equal("customer_name", Assert.Single(result.NewFields).FieldName);
        Assert.Equal(["Direct"], session.ExtractedData["customer_name"].Values);
        Assert.False(session.ExtractedData.ContainsKey("Customer Name"));
    }

    [Fact]
    public async Task ProcessAsync_WhenNormalizedAndSemanticMatchesExist_ShouldUseNormalizedMatch()
    {
        // Arrange
        DataExtractionEntry[] entries =
        [
            new()
            {
                Name = "display_name",
                Description = "The customer's full name.",
            },
            new()
            {
                Name = "customer_name",
                Description = "The customer name.",
            },
        ];

        // Act
        var (result, session) = await ProcessExtractionAsync(
            entries,
            """{"fields":[{"name":"customer-name","values":["Normalized"],"confidence":0.99}],"sessionEnded":false}""");

        // Assert
        Assert.Equal("customer_name", Assert.Single(result.NewFields).FieldName);
        Assert.Equal(["Normalized"], session.ExtractedData["customer_name"].Values);
        Assert.False(session.ExtractedData.ContainsKey("display_name"));
    }

    [Theory]
    [InlineData("customer-name", "Mapped extracted field 'customer-name' to configured field 'customer_name' for session data extraction.")]
    [InlineData("givenName", "Mapped extracted field 'givenName' to configured field 'customer_name' for session data extraction using semantic aliasing.")]
    public async Task ProcessAsync_WhenAliasMatchOccurs_ShouldLogMapping(
        string resultName,
        string expectedMessage)
    {
        // Arrange
        var logger = new Mock<ILogger<DataExtractionService>>();
        logger.Setup(value => value.IsEnabled(LogLevel.Debug)).Returns(true);
        DataExtractionEntry[] entries =
        [
            new()
            {
                Name = "customer_name",
                Description = "The customer's full name.",
            },
        ];

        // Act
        await ProcessExtractionAsync(
            entries,
            logger,
            $$"""{"fields":[{"name":"{{resultName}}","values":["Mapped"],"confidence":0.99}],"sessionEnded":false}""");

        // Assert
#pragma warning disable CA1873
        logger.Verify(
            value => value.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString().Contains(expectedMessage, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    [Theory]
    [InlineData("email", "EMAIL", "email", "email")]
    [InlineData("first_name", "first-name", "firstName", "first_name")]
    [InlineData("primary_name", "secondary_name", "givenName", "primary_name")]
    public async Task ProcessAsync_WhenMultipleEntriesMatchSameTier_ShouldUseFirstConfiguredEntry(
        string firstName,
        string secondName,
        string resultName,
        string expectedName)
    {
        // Arrange
        DataExtractionEntry[] entries =
        [
            new()
            {
                Name = firstName,
                Description = "The customer's full name.",
            },
            new()
            {
                Name = secondName,
                Description = "The customer's full name.",
            },
        ];

        // Act
        var (result, session) = await ProcessExtractionAsync(
            entries,
            $$"""{"fields":[{"name":"{{resultName}}","values":["First"],"confidence":0.99}],"sessionEnded":false}""");

        // Assert
        Assert.Equal(expectedName, Assert.Single(result.NewFields).FieldName);
        Assert.Equal(["First"], session.ExtractedData[expectedName].Values);
    }

    [Theory]
    [InlineData("firstName", "Mike", "lastName", "Smith", "Mike Smith")]
    [InlineData("lastName", "Smith", "firstName", "Mike", "Mike")]
    [InlineData("fullName", "Mike Smith", "firstName", "Michael", "Michael Smith")]
    [InlineData("fullName", "Mike Smith", "lastName", "Jones", "Mike Jones")]
    public async Task ProcessAsync_WhenNameAliasesArriveAcrossExtractions_ShouldMergeNameParts(
        string firstResultName,
        string firstValue,
        string secondResultName,
        string secondValue,
        string expectedValue)
    {
        // Arrange
        DataExtractionEntry[] entries =
        [
            new()
            {
                Name = "customer_name",
                Description = "The customer's full name.",
                IsUpdatable = true,
            },
        ];
        var firstResponse = $$"""{"fields":[{"name":"{{firstResultName}}","values":["{{firstValue}}"],"confidence":0.99}],"sessionEnded":false}""";
        var secondResponse = $$"""{"fields":[{"name":"{{secondResultName}}","values":["{{secondValue}}"],"confidence":0.99}],"sessionEnded":false}""";

        // Act
        var (_, session) = await ProcessExtractionAsync(entries, firstResponse, secondResponse);

        // Assert
        Assert.Equal([expectedValue], session.ExtractedData["customer_name"].Values);
    }

    [Fact]
    public async Task ProcessAsync_WhenNoConfiguredFieldMatches_ShouldLogWarningAndIgnoreResult()
    {
        // Arrange
        var logger = new Mock<ILogger<DataExtractionService>>();
        DataExtractionEntry[] entries =
        [
            new()
            {
                Name = "email",
                Description = "The customer's email address.",
            },
        ];

        // Act
        var (result, session) = await ProcessExtractionAsync(
            entries,
            logger,
            """{"fields":[{"name":"unknown","values":["ignored"],"confidence":0.99}],"sessionEnded":false}""");

        // Assert
        Assert.Empty(result.NewFields);
        Assert.Empty(session.ExtractedData);
        logger.Verify(
            value => value.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString().Contains("Ignoring extracted field 'unknown'", StringComparison.Ordinal) &&
                    state.ToString().Contains("Configured fields: email", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
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

    private static Task<(ExtractionChangeSet Result, AIChatSession Session)> ProcessExtractionAsync(
        IReadOnlyList<DataExtractionEntry> entries,
        params string[] responses)
    {
        return ProcessExtractionAsync(entries, null, responses);
    }

    private static async Task<(ExtractionChangeSet Result, AIChatSession Session)> ProcessExtractionAsync(
        IReadOnlyList<DataExtractionEntry> entries,
        Mock<ILogger<DataExtractionService>> logger,
        params string[] responses)
    {
        var clientFactory = new Mock<IAIClientFactory>();
        var templateService = new Mock<ITemplateService>();
        var deploymentManager = new Mock<IAIDeploymentManager>();
        var chatClient = new Mock<IChatClient>();
        var responseQueue = new Queue<string>(responses);
        var profile = CreateProfile(settings =>
        {
            settings.EnableDataExtraction = true;
            settings.ExtractionCheckInterval = 1;
            settings.DataExtractionEntries = [.. entries];
        });
        profile.UtilityDeploymentName = "utility";

        deploymentManager.Setup(manager => manager
            .ResolveOrDefaultAsync(AIDeploymentPurpose.Utility, "utility", null))
            .ReturnsAsync(new AIDeployment
            {
                ClientName = "OpenAI",
                ConnectionName = "Default",
                ModelName = "gpt-4.1",
            });
        clientFactory.Setup(factory => factory
            .CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<ChatClientBuilder>>()))
            .ReturnsAsync(chatClient.Object);
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtraction, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("system prompt");
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("rendered prompt");
        chatClient.Setup(client => client
            .GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, responseQueue.Dequeue())));

        var service = new DataExtractionService(
            clientFactory.Object,
            templateService.Object,
            [new DefaultMarkdownTemplateParser()],
            TimeProvider.System,
            logger?.Object ?? NullLogger<DataExtractionService>.Instance,
            deploymentManager.Object);
        var session = new AIChatSession();
        ExtractionChangeSet result = null;

        for (var index = 0; index < responses.Length; index++)
        {
            result = await service.ProcessAsync(
                profile,
                session,
                [new AIChatSessionPrompt { Role = ChatRole.User, Content = $"message-{index}" }],
                TestContext.Current.CancellationToken);
        }

        return (result, session);
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
