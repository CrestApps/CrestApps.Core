using System.Reflection;
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
    private static readonly MethodInfo MergeNamePartsMethod = typeof(DataExtractionService)
        .GetMethod("MergeNameParts", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo BuildExtractionPromptAsyncMethod = typeof(DataExtractionService)
        .GetMethod("BuildExtractionPromptAsync", BindingFlags.NonPublic | BindingFlags.Instance);

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

    [Theory]
    [InlineData(null, null, "FirstName", null)]
    [InlineData("Existing", "", "FirstName", "Existing")]
    [InlineData("  Existing  ", " \t\r\n ", "LastName", "  Existing  ")]
    [InlineData(null, "  Ada  ", "FirstName", "Ada")]
    [InlineData("", "  Ada Lovelace  ", "LastName", "Ada Lovelace")]
    [InlineData("\t\r\n", "  李 小龍  ", "FirstName", "李 小龍")]
    [InlineData("Smith", "  ADA  ", "FirstName", "ADA")]
    [InlineData("Ada", "  Lovelace  ", "LastName", "Ada Lovelace")]
    [InlineData("  Ada   Byron  Lovelace  ", "  Augusta  ", "FirstName", "Augusta Byron Lovelace")]
    [InlineData("  Ada   Byron  Lovelace  ", "  King  ", "LastName", "Ada Byron King")]
    [InlineData("Ada\tByron  Lovelace\n", "King", "LastName", "Ada\tByron King")]
    [InlineData("\u00A0Ada\u00A0  Lovelace\u00A0", "Grace", "FirstName", "Grace Lovelace")]
    [InlineData("Jean-Luc O'Neill, Jr.", "Smith-Jones", "LastName", "Jean-Luc O'Neill, Smith-Jones")]
    [InlineData("|Ada|  |Lovelace|", "--Grace--", "FirstName", "--Grace-- |Lovelace|")]
    [InlineData("mCdonald SMITH", "ADA", "FirstName", "ADA SMITH")]
    [InlineData("Ada Smith", "  Mary   Jane  ", "FirstName", "Mary   Jane Smith")]
    [InlineData("---", "Ada", "LastName", "--- Ada")]
    [InlineData("Ada Lovelace", "Byron", "FullName", "Ada Byron")]
    [InlineData("Ada Lovelace", "Byron", "Unknown", "Ada Byron")]
    [InlineData("Ada Lovelace", "Byron", "PhoneNumber", "Ada Byron")]
    public void MergeNameParts_ShouldPreserveExactSemantics(
        string existingValue,
        string newValue,
        string resultFieldKind,
        string expected)
    {
        // Act
        var result = InvokeMergeNameParts(
            existingValue,
            newValue,
            Enum.Parse<DataExtractionService.ExtractionFieldKind>(resultFieldKind));

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MergeNameParts_ShouldMatchLegacyImplementationAcrossInputMatrix()
    {
        // Arrange
        string[] existingValues =
        [
            null,
            "",
            " ",
            "\t\r\n",
            "\u00A0",
            "Ada",
            "  Ada  ",
            "Ada Lovelace",
            "  Ada   Byron  Lovelace  ",
            "Ada\tByron  Lovelace\n",
            "\u00A0Ada\u00A0  Lovelace\u00A0",
            "Jean-Luc O'Neill, Jr.",
            "|Ada|  |Lovelace|",
            "mCdonald SMITH",
            "李 小龍",
            "---",
        ];
        string[] newValues =
        [
            null,
            "",
            " ",
            "\t\r\n",
            "Grace",
            "  Grace  ",
            "Mary   Jane",
            "Smith-Jones (III)",
            "O'Connor, Jr.",
            "김민수",
            "\u00A0李 小龍\u00A0",
        ];

        // Act and assert
        foreach (var existingValue in existingValues)
        {
            foreach (var newValue in newValues)
            {
                foreach (var resultFieldKind in Enum.GetValues<DataExtractionService.ExtractionFieldKind>())
                {
                    var expected = MergeNamePartsLegacy(existingValue, newValue, resultFieldKind);
                    var actual = InvokeMergeNameParts(existingValue, newValue, resultFieldKind);

                    Assert.True(
                        string.Equals(expected, actual, StringComparison.Ordinal),
                        $"Mismatch for existing '{existingValue}', incoming '{newValue}', and kind '{resultFieldKind}'.");
                }
            }
        }
    }

    [Fact]
    public async Task BuildExtractionPromptAsync_WithNoEntries_ShouldPreserveEmptyProjectionAndArgumentOrder()
    {
        // Arrange
        var templateService = new Mock<ITemplateService>();
        IDictionary<string, object> promptArguments = null;
        using var cancellation = new CancellationTokenSource();
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), cancellation.Token))
            .Callback<string, IDictionary<string, object>, CancellationToken>((_, arguments, _) => promptArguments = arguments)
            .ReturnsAsync("empty projection");
        var service = CreateService(
            new Mock<IAIClientFactory>(),
            templateService,
            new Mock<IAIDeploymentManager>());

        // Act
        var result = await InvokeBuildExtractionPromptAsync(
            service,
            [],
            new AIChatSession
            {
                ExtractedData = null,
            },
            [new AIChatSessionPrompt { Role = ChatRole.User, Content = "  latest message  " }],
            cancellation.Token);

        // Assert
        Assert.Equal("empty projection", result);
        Assert.NotNull(promptArguments);
        Assert.Equal(["fields", "currentState", "lastUserMessage"], promptArguments.Keys);
        Assert.Equal("[]", JsonSerializer.Serialize(promptArguments["fields"]));
        Assert.Equal("[]", JsonSerializer.Serialize(promptArguments["currentState"]));
        Assert.Equal("latest message", promptArguments["lastUserMessage"]);
        Assert.True(promptArguments.ContainsKey("FIELDS"));
        templateService.Verify(service => service.RenderAsync(
            AITemplateIds.DataExtractionPrompt,
            It.Is<IDictionary<string, object>>(arguments => ReferenceEquals(arguments, promptArguments)),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task BuildExtractionPromptAsync_WithOneNullValuedEntry_ShouldPreserveAnonymousShape()
    {
        // Arrange
        var templateService = new Mock<ITemplateService>();
        IDictionary<string, object> promptArguments = null;
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IDictionary<string, object>, CancellationToken>((_, arguments, _) => promptArguments = arguments)
            .ReturnsAsync("single projection");
        var service = CreateService(
            new Mock<IAIClientFactory>(),
            templateService,
            new Mock<IAIDeploymentManager>());

        // Act
        var result = await InvokeBuildExtractionPromptAsync(
            service,
            [
                new DataExtractionEntry
                {
                    Name = null,
                    Description = null,
                    AllowMultipleValues = true,
                    IsUpdatable = true,
                },
            ],
            new AIChatSession(),
            [
                new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = null },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "value" },
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("single projection", result);
        Assert.NotNull(promptArguments);
        Assert.Equal(["fields", "currentState", "lastUserMessage"], promptArguments.Keys);
        Assert.Equal(
            """[{"Name":null,"Description":null,"AllowMultipleValues":true,"IsUpdatable":true}]""",
            JsonSerializer.Serialize(promptArguments["fields"]));
        Assert.Equal("[]", JsonSerializer.Serialize(promptArguments["currentState"]));
    }

    [Fact]
    public async Task BuildExtractionPromptAsync_WithManyEntries_ShouldPreserveOrderDuplicatesAliasesExamplesAndCurrentState()
    {
        // Arrange
        var templateService = new Mock<ITemplateService>();
        IDictionary<string, object> promptArguments = null;
        using var cancellation = new CancellationTokenSource();
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), cancellation.Token))
            .Callback<string, IDictionary<string, object>, CancellationToken>((_, arguments, _) => promptArguments = arguments)
            .ReturnsAsync("many projection");
        var service = CreateService(
            new Mock<IAIClientFactory>(),
            templateService,
            new Mock<IAIDeploymentManager>());
        List<DataExtractionEntry> fields =
        [
            new()
            {
                Name = "customer_name",
                Description = "Aliases: fullName, display name. Examples: Ada Lovelace; 李 小龍.",
                IsUpdatable = true,
            },
            new()
            {
                Name = "customer_name",
                Description = null,
                AllowMultipleValues = true,
            },
            new()
            {
                Name = "email",
                Description = "Aliases: e-mail. Examples: ada@example.com.",
                AllowMultipleValues = true,
                IsUpdatable = true,
            },
        ];
        var session = new AIChatSession
        {
            ExtractedData =
            {
                ["empty"] = new ExtractedFieldState(),
                ["null-state"] = null,
                ["customer_name"] = new ExtractedFieldState
                {
                    Values = ["Ada Lovelace", null, "", "  李 小龍  "],
                },
                ["email"] = new ExtractedFieldState
                {
                    Values = ["ada@example.com"],
                },
            },
        };
        AIChatSessionPrompt[] prompts =
        [
            new() { Role = ChatRole.User, Content = "older user" },
            new() { Role = ChatRole.Assistant, Content = "  nearest assistant  " },
            new() { Role = ChatRole.User, Content = "  latest user  " },
            new() { Role = ChatRole.Assistant, Content = "ignored later assistant" },
        ];

        // Act
        var result = await InvokeBuildExtractionPromptAsync(
            service,
            fields,
            session,
            prompts,
            cancellation.Token);

        // Assert
        Assert.Equal("many projection", result);
        Assert.NotNull(promptArguments);
        Assert.Equal(
            ["fields", "currentState", "lastUserMessage", "lastAssistantMessage"],
            promptArguments.Keys);
        Assert.Equal(
            """[{"Name":"customer_name","Description":"Aliases: fullName, display name. Examples: Ada Lovelace; \u674E \u5C0F\u9F8D.","AllowMultipleValues":false,"IsUpdatable":true},{"Name":"customer_name","Description":null,"AllowMultipleValues":true,"IsUpdatable":false},{"Name":"email","Description":"Aliases: e-mail. Examples: ada@example.com.","AllowMultipleValues":true,"IsUpdatable":true}]""",
            JsonSerializer.Serialize(promptArguments["fields"]));
        Assert.Equal(
            """[{"Name":"customer_name","Values":["Ada Lovelace",null,"","  \u674E \u5C0F\u9F8D  "]},{"Name":"email","Values":["ada@example.com"]}]""",
            JsonSerializer.Serialize(promptArguments["currentState"]));
        Assert.Equal("latest user", promptArguments["lastUserMessage"]);
        Assert.Equal("nearest assistant", promptArguments["lastAssistantMessage"]);
        templateService.Verify(service => service.RenderAsync(
            AITemplateIds.DataExtractionPrompt,
            It.Is<IDictionary<string, object>>(arguments => ReferenceEquals(arguments, promptArguments)),
            cancellation.Token), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task BuildExtractionPromptAsync_WhenLatestUserMessageIsEmpty_ShouldNotInvokeTemplate(string content)
    {
        // Arrange
        var templateService = new Mock<ITemplateService>();
        var service = CreateService(
            new Mock<IAIClientFactory>(),
            templateService,
            new Mock<IAIDeploymentManager>());

        // Act
        var result = await InvokeBuildExtractionPromptAsync(
            service,
            [new DataExtractionEntry { Name = "email" }],
            new AIChatSession(),
            [
                new AIChatSessionPrompt { Role = ChatRole.User, Content = "older usable message" },
                new AIChatSessionPrompt { Role = ChatRole.User, Content = content },
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        templateService.Verify(service => service.RenderAsync(
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildExtractionPromptAsync_WhenNoUserMessageExists_ShouldNotInvokeTemplate()
    {
        // Arrange
        var templateService = new Mock<ITemplateService>();
        var service = CreateService(
            new Mock<IAIClientFactory>(),
            templateService,
            new Mock<IAIDeploymentManager>());

        // Act
        var result = await InvokeBuildExtractionPromptAsync(
            service,
            [new DataExtractionEntry { Name = "email" }],
            new AIChatSession(),
            [new AIChatSessionPrompt { Role = ChatRole.Assistant, Content = "assistant only" }],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        templateService.Verify(service => service.RenderAsync(
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildExtractionPromptAsync_WhenTemplateIsCanceled_ShouldPropagateCancellation()
    {
        // Arrange
        var templateService = new Mock<ITemplateService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), cancellation.Token))
            .Returns((string _, IDictionary<string, object> _, CancellationToken token) => Task.FromCanceled<string>(token));
        var service = CreateService(
            new Mock<IAIClientFactory>(),
            templateService,
            new Mock<IAIDeploymentManager>());

        // Act and assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeBuildExtractionPromptAsync(
            service,
            [new DataExtractionEntry { Name = "email" }],
            new AIChatSession(),
            [new AIChatSessionPrompt { Role = ChatRole.User, Content = "value" }],
            cancellation.Token));
        templateService.Verify(service => service.RenderAsync(
            AITemplateIds.DataExtractionPrompt,
            It.IsAny<IDictionary<string, object>>(),
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task BuildExtractionPromptAsync_WhenTemplateThrows_ShouldPropagateError()
    {
        // Arrange
        var templateService = new Mock<ITemplateService>();
        var expected = new InvalidOperationException("template failure");
        templateService.Setup(service => service
            .RenderAsync(AITemplateIds.DataExtractionPrompt, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var service = CreateService(
            new Mock<IAIClientFactory>(),
            templateService,
            new Mock<IAIDeploymentManager>());

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeBuildExtractionPromptAsync(
            service,
            [new DataExtractionEntry { Name = "email" }],
            new AIChatSession(),
            [new AIChatSessionPrompt { Role = ChatRole.User, Content = "value" }],
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(expected, exception);
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

    /// <summary>
    /// Invokes the private name-merging implementation so its legacy behavior can be locked before optimization.
    /// </summary>
    /// <param name="existingValue">The currently extracted value.</param>
    /// <param name="newValue">The incoming name part.</param>
    /// <param name="resultFieldKind">The incoming field kind.</param>
    /// <returns>The merged value.</returns>
    private static string InvokeMergeNameParts(
        string existingValue,
        string newValue,
        DataExtractionService.ExtractionFieldKind resultFieldKind)
    {
        return (string)MergeNamePartsMethod.Invoke(null, [existingValue, newValue, resultFieldKind]);
    }

    /// <summary>
    /// Captures the pre-optimization name-merging implementation for differential verification.
    /// </summary>
    /// <param name="existingValue">The currently extracted value.</param>
    /// <param name="newValue">The incoming name part.</param>
    /// <param name="resultFieldKind">The incoming field kind.</param>
    /// <returns>The merged value.</returns>
    private static string MergeNamePartsLegacy(
        string existingValue,
        string newValue,
        DataExtractionService.ExtractionFieldKind resultFieldKind)
    {
        if (string.IsNullOrWhiteSpace(newValue))
        {
            return existingValue;
        }

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            return newValue.Trim();
        }

        var existingParts = existingValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var incomingValue = newValue.Trim();

        if (resultFieldKind == DataExtractionService.ExtractionFieldKind.FirstName)
        {
            return existingParts.Length > 1
                ? string.Join(' ', [incomingValue, .. existingParts.Skip(1)])
                : incomingValue;
        }

        if (existingParts.Length > 1)
        {
            return string.Join(' ', [.. existingParts.Take(existingParts.Length - 1), incomingValue]);
        }

        return string.Concat(existingParts[0], " ", incomingValue);
    }

    /// <summary>
    /// Invokes prompt construction directly so projection behavior can be tested independently of extraction matching.
    /// </summary>
    /// <param name="service">The data extraction service.</param>
    /// <param name="fieldsToExtract">The fields to project.</param>
    /// <param name="session">The current chat session.</param>
    /// <param name="prompts">The available chat prompts.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered prompt.</returns>
    private static Task<string> InvokeBuildExtractionPromptAsync(
        DataExtractionService service,
        List<DataExtractionEntry> fieldsToExtract,
        AIChatSession session,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        CancellationToken cancellationToken)
    {
        return (Task<string>)BuildExtractionPromptAsyncMethod.Invoke(
            service,
            [fieldsToExtract, session, prompts, cancellationToken]);
    }
}
