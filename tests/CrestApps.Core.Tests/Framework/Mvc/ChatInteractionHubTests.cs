using System.Text.Json;
using System.Threading.Channels;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Handlers;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Exceptions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Hubs;
using CrestApps.Core.Services;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class ChatInteractionHubTests
{
    [Fact]
    public async Task SaveSettings_PersistsCoreAndTemplateSettings()
    {
        // Arrange
        var interaction = new ChatInteraction
        {
            ItemId = "chat-1",
            Title = "Original title",
        };

        var managerMock = new Mock<ICatalogManager<ChatInteraction>>();
        managerMock.Setup(manager => manager
            .FindByIdAsync(interaction.ItemId))
            .Returns(new ValueTask<ChatInteraction>(interaction));
        managerMock.Setup(manager => manager.UpdateAsync(interaction, null))
            .Returns(ValueTask.CompletedTask);

        var callerMock = new Mock<IChatInteractionHubClient>();
        callerMock.Setup(client => client
            .SettingsSaved(interaction.ItemId, "Updated title"))
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubCallerClients<IChatInteractionHubClient>>();
        clientsMock.SetupGet(clients => clients.Caller).Returns(callerMock.Object);

        var toolOptions = new AIToolDefinitionOptions();
        toolOptions.SetTool("selectable-tool", new AIToolDefinitionEntry(typeof(object)));
        toolOptions.SetTool("hidden-tool", new AIToolDefinitionEntry(typeof(object)) { Hidden = true });
        var profileManagerMock = new Mock<IAIProfileManager>();
        profileManagerMock.Setup(manager => manager.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                BuildAgent("agent-a", "Agent A"),
                BuildAgent("agent-b", "Agent B"),
            ]);

        var serviceProvider = new ServiceCollection()
            .AddSingleton(managerMock.Object)
            .AddSingleton(new Mock<IChatInteractionPromptStore>(MockBehavior.Strict).Object)
            .AddSingleton<IChatInteractionSettingsHandler>(new PromptTemplateChatInteractionSettingsHandler())
            .AddSingleton(profileManagerMock.Object)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(toolOptions))
            .BuildServiceProvider();

        var siteSettings = CreateSiteSettingsStore();

        var hub = new ChatInteractionHub(
            serviceProvider,
            TimeProvider.System,
            CreateCitationCollector(),
            siteSettings,
            NullLogger<ChatInteractionHub>.Instance)
        {
            Clients = clientsMock.Object,
        };

        using var json = JsonDocument.Parse("""
            {
              "title":"Updated title",
              "toolNames":["selectable-tool","hidden-tool"],
              "agentNames":["agent-a","agent-b"],
              "promptTemplates":[
                {
                  "templateId":"template-1",
                  "promptParameters":"{\"topic\":\"embeddings\"}"
                }
              ]
            }
            """);

        // Act
        await hub.SaveSettings(interaction.ItemId, json.RootElement.Clone());

        // Assert
        Assert.Equal("Updated title", interaction.Title);
        Assert.Equal(["selectable-tool"], interaction.ToolNames);
        Assert.Equal(["agent-a", "agent-b"], interaction.AgentNames);
        var promptTemplateMetadata = interaction.GetOrCreate<PromptTemplateMetadata>();
        var template = Assert.Single(promptTemplateMetadata.Templates);
        Assert.Equal("template-1", template.TemplateId);
        Assert.NotNull(template.Parameters);
        Assert.Equal("embeddings", Assert.IsType<string>(template.Parameters["topic"]));

        managerMock.Verify(manager => manager.FindByIdAsync(interaction.ItemId), Times.Once);
        managerMock.Verify(manager => manager.UpdateAsync(interaction, null), Times.Once);
        callerMock.Verify(client => client.SettingsSaved(interaction.ItemId, "Updated title"), Times.Once);
    }

    [Fact]
    public async Task SaveSettings_FiltersHiddenToolsAndSystemAgents()
    {
        var interaction = new ChatInteraction
        {
            ItemId = "chat-visibility",
            Title = "Visibility test",
        };

        var managerMock = new Mock<ICatalogManager<ChatInteraction>>();
        managerMock.Setup(manager => manager.FindByIdAsync(interaction.ItemId))
            .Returns(new ValueTask<ChatInteraction>(interaction));
        managerMock.Setup(manager => manager.UpdateAsync(interaction, null))
            .Returns(ValueTask.CompletedTask);

        var toolOptions = new AIToolDefinitionOptions();
        toolOptions.SetTool("selectable-tool", new AIToolDefinitionEntry(typeof(object)));
        toolOptions.SetTool("system-tool", new AIToolDefinitionEntry(typeof(object)) { IsSystemTool = true });
        toolOptions.SetTool("hidden-tool", new AIToolDefinitionEntry(typeof(object)) { Hidden = true });

        var profileManagerMock = new Mock<IAIProfileManager>();
        profileManagerMock.Setup(manager => manager.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                BuildAgent("selectable-agent", "Selectable Agent"),
                BuildAgent("system-agent", "System Agent", isSystem: true),
                BuildAgent("always-agent", "Always Agent", availability: AgentAvailability.AlwaysAvailable),
            ]);

        var callerMock = new Mock<IChatInteractionHubClient>();
        callerMock.Setup(client => client.SettingsSaved(interaction.ItemId, interaction.Title))
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubCallerClients<IChatInteractionHubClient>>();
        clientsMock.SetupGet(clients => clients.Caller).Returns(callerMock.Object);

        var serviceProvider = new ServiceCollection()
            .AddSingleton(managerMock.Object)
            .AddSingleton(new Mock<IChatInteractionPromptStore>(MockBehavior.Strict).Object)
            .AddSingleton(profileManagerMock.Object)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(toolOptions))
            .BuildServiceProvider();

        var hub = new ChatInteractionHub(
            serviceProvider,
            TimeProvider.System,
            CreateCitationCollector(),
            CreateSiteSettingsStore(),
            NullLogger<ChatInteractionHub>.Instance)
        {
            Clients = clientsMock.Object,
        };

        using var json = JsonDocument.Parse("""
            {
              "toolNames":["selectable-tool","system-tool","hidden-tool"],
              "agentNames":["selectable-agent","system-agent","always-agent"]
            }
            """);

        await hub.SaveSettings(interaction.ItemId, json.RootElement.Clone());

        Assert.Equal(["selectable-tool"], interaction.ToolNames);
        Assert.Equal(["selectable-agent"], interaction.AgentNames);
    }

    [Fact]
    public async Task SaveSettings_WithDataSourceSettings_PersistsRagMetadata()
    {
        // Arrange
        var interaction = new ChatInteraction
        {
            ItemId = "chat-2",
            Title = "Knowledge chat",
        };

        var managerMock = new Mock<ICatalogManager<ChatInteraction>>();
        managerMock.Setup(manager => manager
            .FindByIdAsync(interaction.ItemId))
            .Returns(new ValueTask<ChatInteraction>(interaction));
        managerMock.Setup(manager => manager.UpdateAsync(interaction, null))
            .Returns(ValueTask.CompletedTask);
        var dataSourceCatalog = new Mock<IAIDataSourceStore>();
        dataSourceCatalog.Setup(catalog => catalog
            .FindByIdAsync("datasource-1"))
            .ReturnsAsync(new AIDataSource { ItemId = "datasource-1" });
        var toolOptions = new AIToolDefinitionOptions();
        var profileManagerMock = new Mock<IAIProfileManager>();
        profileManagerMock.Setup(manager => manager.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var services = new ServiceCollection()
            .AddSingleton(managerMock.Object)
            .AddSingleton(new Mock<IChatInteractionPromptStore>(MockBehavior.Strict).Object)
            .AddSingleton(dataSourceCatalog.Object)
            .AddSingleton<IChatInteractionSettingsHandler, DataSourceChatInteractionSettingsHandler>()
            .AddSingleton(profileManagerMock.Object)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(toolOptions))
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var serviceProvider = services.BuildServiceProvider();

        var callerMock = new Mock<IChatInteractionHubClient>();
        callerMock.Setup(client => client
            .SettingsSaved(interaction.ItemId, interaction.Title))
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubCallerClients<IChatInteractionHubClient>>();
        clientsMock.SetupGet(clients => clients.Caller).Returns(callerMock.Object);

        var siteSettings = CreateSiteSettingsStore();

        var hub = new ChatInteractionHub(
            serviceProvider,
            TimeProvider.System,
            CreateCitationCollector(),
            siteSettings,
            NullLogger<ChatInteractionHub>.Instance)
        {
            Clients = clientsMock.Object,
        };

        using var json = JsonDocument.Parse("""
            {
              "dataSourceId":"datasource-1",
              "strictness":4,
              "topNDocuments":7,
              "isInScope":false,
              "filter":"category eq 'docs'"
            }
            """);

        // Act
        await hub.SaveSettings(interaction.ItemId, json.RootElement.Clone());

        // Assert
        var dataSourceMetadata = interaction.GetOrCreate<DataSourceMetadata>();
        Assert.Equal("datasource-1", dataSourceMetadata.DataSourceId);
        var ragMetadata = interaction.GetOrCreate<AIDataSourceRagMetadata>();
        Assert.Equal(4, ragMetadata.Strictness);
        Assert.Equal(7, ragMetadata.TopNDocuments);
        Assert.False(ragMetadata.IsInScope);
        Assert.Equal("category eq 'docs'", ragMetadata.Filter);

        managerMock.Verify(manager => manager.FindByIdAsync(interaction.ItemId), Times.Once);
        managerMock.Verify(manager => manager.UpdateAsync(interaction, null), Times.Once);
        callerMock.Verify(client => client.SettingsSaved(interaction.ItemId, interaction.Title), Times.Once);
    }

    [Fact]
    public void GetFriendlyErrorMessage_WithInvalidChatModelSettings_ReturnsInteractionGuidance()
    {
        var hub = new TestChatInteractionHub(new ServiceCollection().BuildServiceProvider())
        {
            Clients = new Mock<IHubCallerClients<IChatInteractionHubClient>>().Object,
        };

        var message = hub.GetFriendlyErrorMessageForTest(new AIDeploymentNotFoundException("Unable to resolve a chat deployment for the profile."));

        Assert.Equal("The chat model settings are missing or invalid. Update the Chat model in this chat interaction, the linked AI Profile, or the global AI settings.", message);
    }

    [Fact]
    public async Task ClearHistory_DeletesPromptsAndInvokesHistoryHandlers()
    {
        // Arrange
        var interaction = new ChatInteraction
        {
            ItemId = "chat-3",
            Title = "Chat with generated files",
        };

        var managerMock = new Mock<ICatalogManager<ChatInteraction>>();
        managerMock.Setup(manager => manager
            .FindByIdAsync(interaction.ItemId))
            .Returns(new ValueTask<ChatInteraction>(interaction));

        var prompts = new List<ChatInteractionPrompt>
        {
            new()
            {
                ItemId = "prompt-1",
                ChatInteractionId = interaction.ItemId,
                References = new Dictionary<string, AICompletionReference>
                {
                    ["[doc:1]"] = new() { ReferenceId = "gen-1", IsGenerated = true },
                },
            },
        };

        var promptStoreMock = new Mock<IChatInteractionPromptStore>();
        promptStoreMock
            .Setup(store => store.GetPromptsAsync(interaction.ItemId))
            .ReturnsAsync(prompts);
        promptStoreMock
            .Setup(store => store.DeleteAllPromptsAsync(interaction.ItemId))
            .ReturnsAsync(prompts.Count);

        var historyHandlerMock = new Mock<IChatInteractionHistoryHandler>();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(managerMock.Object)
            .AddSingleton(promptStoreMock.Object)
            .AddSingleton(historyHandlerMock.Object)
            .BuildServiceProvider();

        var callerMock = new Mock<IChatInteractionHubClient>();
        callerMock.Setup(client => client.HistoryCleared(interaction.ItemId))
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubCallerClients<IChatInteractionHubClient>>();
        clientsMock.SetupGet(clients => clients.Caller).Returns(callerMock.Object);

        var hub = new ChatInteractionHub(
            serviceProvider,
            TimeProvider.System,
            CreateCitationCollector(),
            CreateSiteSettingsStore(),
            NullLogger<ChatInteractionHub>.Instance)
        {
            Clients = clientsMock.Object,
        };

        // Act
        await hub.ClearHistory(interaction.ItemId);

        // Assert
        promptStoreMock.Verify(store => store.GetPromptsAsync(interaction.ItemId), Times.Once);
        promptStoreMock.Verify(store => store.DeleteAllPromptsAsync(interaction.ItemId), Times.Once);
        historyHandlerMock.Verify(
            handler => handler.HistoryClearedAsync(interaction, prompts, It.IsAny<CancellationToken>()),
            Times.Once);
        callerMock.Verify(client => client.HistoryCleared(interaction.ItemId), Times.Once);
    }

    /// <summary>
    /// Verifies prompt persistence, group membership, and handler dispatch retain the caller's
    /// cancellation token around conversation-history construction.
    /// </summary>
    [Fact]
    public async Task HandlePromptAsync_PropagatesCancellationTokenAroundHistoryConstruction()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var interaction = new ChatInteraction
        {
            ItemId = "interaction",
            Title = "Existing title",
        };
        var interactionManagerMock = new Mock<ICatalogManager<ChatInteraction>>();
        interactionManagerMock
            .Setup(manager => manager.FindByIdAsync(interaction.ItemId, cancellationToken))
            .Returns(new ValueTask<ChatInteraction>(interaction));
        var promptStoreMock = new Mock<IChatInteractionPromptStore>();
        promptStoreMock
            .Setup(store => store.CreateAsync(
                It.Is<ChatInteractionPrompt>(prompt => prompt.ItemId == "new-prompt"),
                cancellationToken))
            .Returns(ValueTask.CompletedTask);
        promptStoreMock
            .Setup(store => store.GetPromptsAsync(interaction.ItemId))
            .ReturnsAsync([]);
        ChatResponseHandlerContext handlerContext = null;
        var handlerMock = new Mock<IChatResponseHandler>();
        handlerMock
            .Setup(handler => handler.HandleAsync(
                It.IsAny<ChatResponseHandlerContext>(),
                cancellationToken))
            .Callback<ChatResponseHandlerContext, CancellationToken>(
                (context, _) => handlerContext = context)
            .ReturnsAsync(ChatResponseHandlerResult.Deferred());
        var handlerResolverMock = new Mock<IChatResponseHandlerResolver>();
        handlerResolverMock
            .Setup(resolver => resolver.Resolve(null, ChatMode.TextInput))
            .Returns(handlerMock.Object);
        var services = new ServiceCollection()
            .AddSingleton(interactionManagerMock.Object)
            .AddSingleton(promptStoreMock.Object)
            .AddSingleton(handlerResolverMock.Object)
            .BuildServiceProvider();
        var contextMock = new Mock<HubCallerContext>();
        contextMock.SetupGet(context => context.ConnectionId).Returns("connection");
        contextMock.SetupGet(context => context.ConnectionAborted).Returns(cancellationToken);
        var groupsMock = new Mock<IGroupManager>();
        groupsMock
            .Setup(groups => groups.AddToGroupAsync(
                "connection",
                ChatInteractionHubBase.GetInteractionGroupName(interaction.ItemId),
                cancellationToken))
            .Returns(Task.CompletedTask);
        var hub = new TestChatInteractionHub(services)
        {
            Context = contextMock.Object,
            Groups = groupsMock.Object,
        };
        var channel = Channel.CreateUnbounded<CompletionPartialMessage>();

        await hub.HandlePromptForTestAsync(
            channel.Writer,
            services,
            interaction.ItemId,
            "prompt",
            cancellationToken);

        Assert.NotNull(handlerContext);
        var historyMessage = Assert.Single(handlerContext.ConversationHistory);
        Assert.Equal(ChatRole.User, historyMessage.Role);
        Assert.Equal("prompt", historyMessage.Text);
        interactionManagerMock.Verify(
            manager => manager.FindByIdAsync(interaction.ItemId, cancellationToken),
            Times.Once);
        promptStoreMock.Verify(
            store => store.CreateAsync(It.IsAny<ChatInteractionPrompt>(), cancellationToken),
            Times.Once);
        groupsMock.Verify(
            groups => groups.AddToGroupAsync(
                "connection",
                ChatInteractionHubBase.GetInteractionGroupName(interaction.ItemId),
                cancellationToken),
            Times.Once);
        handlerMock.Verify(
            handler => handler.HandleAsync(
                It.IsAny<ChatResponseHandlerContext>(),
                cancellationToken),
            Times.Once);
    }

    private static CitationReferenceCollector CreateCitationCollector()
    {
        return new(new CompositeAIReferenceLinkResolver(new ServiceCollection().BuildServiceProvider()));
    }

    private static SiteSettingsStore CreateSiteSettingsStore()
    {
        var appDataPath = Path.Combine(Path.GetTempPath(), "copilot-chatinteractionhubtests", Guid.NewGuid().ToString("N"));

        return new SiteSettingsStore(appDataPath);
    }

    private static AIProfile BuildAgent(
        string name,
        string description,
        AgentAvailability availability = AgentAvailability.OnDemand,
        bool isSystem = false)
    {
        var profile = new AIProfile
        {
            Name = name,
            Description = description,
            Type = AIProfileType.Agent,
        };

        profile.Put(new AgentMetadata
        {
            Availability = availability,
            IsSystem = isSystem,
        });

        return profile;
    }

    private sealed class TestChatInteractionHub : ChatInteractionHubBase
    {
        public TestChatInteractionHub(IServiceProvider services)
            : base(services, TimeProvider.System, NullLogger.Instance)
        {
        }

        public string GetFriendlyErrorMessageForTest(Exception ex)
        {
            return GetFriendlyErrorMessage(ex);
        }

        /// <summary>
        /// Invokes prompt handling for tests.
        /// </summary>
        /// <param name="writer">The output channel writer.</param>
        /// <param name="services">The service provider.</param>
        /// <param name="itemId">The interaction identifier.</param>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing prompt handling.</returns>
        public Task HandlePromptForTestAsync(
            ChannelWriter<CompletionPartialMessage> writer,
            IServiceProvider services,
            string itemId,
            string prompt,
            CancellationToken cancellationToken)
        {
            return HandlePromptAsync(
                writer,
                services,
                itemId,
                prompt,
                cancellationToken);
        }

        /// <inheritdoc />
        protected override string GenerateId()
        {
            return "new-prompt";
        }
    }
}
