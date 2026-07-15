using System.Reflection;
using A2A;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.A2A.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Tests A2A tool registry discovery and mapping behavior.
/// </summary>
public sealed class A2AToolRegistryProviderTests
{
    /// <summary>
    /// Verifies that missing connection selections return no tools without querying dependencies.
    /// </summary>
    [Fact]
    public async Task GetToolsAsync_NullOrEmptyConnectionIds_ReturnsEmptyWithoutQueries()
    {
        var connectionStore = new Mock<ICatalog<A2AConnection>>();
        var agentCardCache = new Mock<IA2AAgentCardCacheService>();
        var provider = CreateProvider(connectionStore, agentCardCache);

        var nullResult = await provider.GetToolsAsync(
            new AICompletionContext { A2AConnectionIds = null },
            TestContext.Current.CancellationToken);
        var emptyResult = await provider.GetToolsAsync(
            new AICompletionContext { A2AConnectionIds = [] },
            TestContext.Current.CancellationToken);

        Assert.Empty(nullResult);
        Assert.Empty(emptyResult);
        connectionStore.Verify(
            store => store.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        agentCardCache.Verify(
            cache => cache.GetAgentCardAsync(
                It.IsAny<string>(),
                It.IsAny<A2AConnection>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that an already-valid tool name is preserved exactly.
    /// </summary>
    [Fact]
    public async Task GetToolsAsync_ValidSkillName_PreservesName()
    {
        var provider = CreateProvider(
            [CreateConnection("connection", "Agent")],
            new Dictionary<string, AgentCard>
            {
                ["connection"] = CreateCard(
                    "Card description",
                    new AgentSkill
                    {
                        Id = "Valid_Name9",
                        Name = "Ignored name",
                        Description = "Skill description",
                    }),
            });

        var result = await provider.GetToolsAsync(
            new AICompletionContext { A2AConnectionIds = ["connection"] },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(result);
        Assert.Equal("Valid_Name9", entry.Name);
        Assert.Equal("a2a:connection:Valid_Name9", entry.Id);
    }

    /// <summary>
    /// Verifies that invalid tool-name characters are replaced using the legacy rules.
    /// </summary>
    [Fact]
    public async Task GetToolsAsync_InvalidSkillName_SanitizesExactly()
    {
        var provider = CreateProvider(
            [CreateConnection("connection", "Agent")],
            new Dictionary<string, AgentCard>
            {
                ["connection"] = CreateCard(
                    "Card description",
                    new AgentSkill
                    {
                        Id = "sales-report.v1 / é",
                        Description = "Skill description",
                    }),
            });

        var result = await provider.GetToolsAsync(
            new AICompletionContext { A2AConnectionIds = ["connection"] },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(result);
        Assert.Equal("sales_report_v1___é", entry.Name);
        Assert.Equal("a2a:connection:sales_report_v1___é", entry.Id);
    }

    /// <summary>
    /// Verifies stable connection and skill ordering, metadata mapping, fallbacks, and tool factories.
    /// </summary>
    [Fact]
    public async Task GetToolsAsync_MultipleSkills_PreservesOrderingMetadataAndFactoryState()
    {
        var connections = new[]
        {
            CreateConnection("first", "First Agent", "https://first.example"),
            CreateConnection("second", "Second Agent", "https://second.example"),
        };
        var cards = new Dictionary<string, AgentCard>
        {
            ["first"] = CreateCard(
                "First card description",
                new AgentSkill
                {
                    Id = "first-skill",
                    Name = "Ignored",
                    Description = "First skill description",
                },
                new AgentSkill
                {
                    Id = null,
                    Name = "Second skill",
                    Description = null,
                }),
            ["second"] = CreateCard(
                "Second card description",
                new AgentSkill
                {
                    Id = null,
                    Name = null,
                    Description = null,
                }),
        };
        var provider = CreateProvider(connections, cards);

        var result = await provider.GetToolsAsync(
            new AICompletionContext { A2AConnectionIds = ["first", "second"] },
            TestContext.Current.CancellationToken);

        Assert.Collection(
            result,
            entry => AssertEntry(
                entry,
                "a2a:first:first_skill",
                "first_skill",
                "First skill description",
                "first",
                "https://first.example"),
            entry => AssertEntry(
                entry,
                "a2a:first:Second_skill",
                "Second_skill",
                "First card description",
                "first",
                "https://first.example"),
            entry => AssertEntry(
                entry,
                "a2a:second:Second_Agent",
                "Second_Agent",
                "Second card description",
                "second",
                "https://second.example"));
    }

    /// <summary>
    /// Verifies that unavailable connections and cards are skipped and cache failures are logged
    /// without preventing later connections from loading.
    /// </summary>
    [Fact]
    public async Task GetToolsAsync_SkipsUnavailableEntriesAndContinuesAfterCacheFailure()
    {
        var missingId = "missing";
        var blankId = "blank";
        var noSkillsId = "no-skills";
        var failingId = "failing";
        var availableId = "available";
        var connectionStore = new Mock<ICatalog<A2AConnection>>();
        connectionStore
            .Setup(store => store.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string id, CancellationToken _) => ValueTask.FromResult(id switch
            {
                "blank" => CreateConnection(blankId, "Blank", " "),
                "no-skills" => CreateConnection(noSkillsId, "No Skills"),
                "failing" => CreateConnection(failingId, "Failing"),
                "available" => CreateConnection(availableId, "Available"),
                _ => null,
            }));
        var agentCardCache = new Mock<IA2AAgentCardCacheService>();
        agentCardCache
            .Setup(cache => cache.GetAgentCardAsync(
                noSkillsId,
                It.IsAny<A2AConnection>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentCard
            {
                Name = "No Skills",
                Description = "No skills",
                Url = "https://example.com",
                Version = "1.0",
                Skills = null,
            });
        var exception = new InvalidOperationException("Agent card unavailable.");
        agentCardCache
            .Setup(cache => cache.GetAgentCardAsync(
                failingId,
                It.IsAny<A2AConnection>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        agentCardCache
            .Setup(cache => cache.GetAgentCardAsync(
                availableId,
                It.IsAny<A2AConnection>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCard("Available card", new AgentSkill { Id = "available" }));
        var logger = new RecordingLogger();
        var provider = CreateProvider(connectionStore, agentCardCache, logger);

        var result = await provider.GetToolsAsync(
            new AICompletionContext
            {
                A2AConnectionIds = [missingId, blankId, noSkillsId, failingId, availableId],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("a2a:available:available", Assert.Single(result).Id);
        agentCardCache.Verify(
            cache => cache.GetAgentCardAsync(
                missingId,
                It.IsAny<A2AConnection>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        agentCardCache.Verify(
            cache => cache.GetAgentCardAsync(
                blankId,
                It.IsAny<A2AConnection>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(LogLevel.Warning, logger.LogLevel);
        Assert.Same(exception, logger.Exception);
        Assert.Contains(failingId, logger.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that catalog failures retain their existing propagation behavior.
    /// </summary>
    [Fact]
    public async Task GetToolsAsync_CatalogFailure_Propagates()
    {
        var exception = new InvalidOperationException("Catalog unavailable.");
        var connectionStore = new Mock<ICatalog<A2AConnection>>();
        connectionStore
            .Setup(store => store.FindByIdAsync("failing", It.IsAny<CancellationToken>()))
            .Throws(exception);
        var provider = CreateProvider(
            connectionStore,
            new Mock<IA2AAgentCardCacheService>());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetToolsAsync(
                new AICompletionContext { A2AConnectionIds = ["failing"] },
                TestContext.Current.CancellationToken));

        Assert.Same(exception, thrown);
    }

    /// <summary>
    /// Creates a provider backed by the supplied in-memory connection and card data.
    /// </summary>
    /// <param name="connections">The available A2A connections.</param>
    /// <param name="cards">The agent cards keyed by connection identifier.</param>
    /// <returns>The configured provider.</returns>
    private static A2AToolRegistryProvider CreateProvider(
        IEnumerable<A2AConnection> connections,
        Dictionary<string, AgentCard> cards)
    {
        var connectionStore = new Mock<ICatalog<A2AConnection>>();
        var connectionsById = connections.ToDictionary(connection => connection.ItemId);
        connectionStore
            .Setup(store => store.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string id, CancellationToken _) =>
                ValueTask.FromResult(connectionsById.GetValueOrDefault(id)));
        var agentCardCache = new Mock<IA2AAgentCardCacheService>();
        agentCardCache
            .Setup(cache => cache.GetAgentCardAsync(
                It.IsAny<string>(),
                It.IsAny<A2AConnection>(),
                It.IsAny<CancellationToken>()))
            .Returns((string id, A2AConnection _, CancellationToken _) =>
                Task.FromResult(cards[id]));

        return CreateProvider(connectionStore, agentCardCache);
    }

    /// <summary>
    /// Creates a provider from mocked dependencies.
    /// </summary>
    /// <param name="connectionStore">The mocked connection catalog.</param>
    /// <param name="agentCardCache">The mocked agent-card cache.</param>
    /// <param name="logger">The optional mocked logger.</param>
    /// <returns>The configured provider.</returns>
    private static A2AToolRegistryProvider CreateProvider(
        Mock<ICatalog<A2AConnection>> connectionStore,
        Mock<IA2AAgentCardCacheService> agentCardCache,
        ILogger<A2AToolRegistryProvider> logger = null)
    {
        return new A2AToolRegistryProvider(
            connectionStore.Object,
            agentCardCache.Object,
            logger ?? NullLogger<A2AToolRegistryProvider>.Instance);
    }

    /// <summary>
    /// Creates an A2A connection for a test.
    /// </summary>
    /// <param name="id">The connection identifier.</param>
    /// <param name="displayText">The connection display text.</param>
    /// <param name="endpoint">The connection endpoint.</param>
    /// <returns>The connection.</returns>
    private static A2AConnection CreateConnection(
        string id,
        string displayText,
        string endpoint = "https://example.com")
    {
        return new A2AConnection
        {
            ItemId = id,
            DisplayText = displayText,
            Endpoint = endpoint,
        };
    }

    /// <summary>
    /// Creates an agent card for a test.
    /// </summary>
    /// <param name="description">The card description.</param>
    /// <param name="skills">The card skills.</param>
    /// <returns>The agent card.</returns>
    private static AgentCard CreateCard(string description, params AgentSkill[] skills)
    {
        return new AgentCard
        {
            Name = "Agent",
            Description = description,
            Url = "https://example.com",
            Version = "1.0",
            Skills = [.. skills],
        };
    }

    /// <summary>
    /// Verifies a registry entry and the state captured by its tool factory.
    /// </summary>
    /// <param name="entry">The registry entry.</param>
    /// <param name="id">The expected entry identifier.</param>
    /// <param name="name">The expected tool name.</param>
    /// <param name="description">The expected tool description.</param>
    /// <param name="connectionId">The expected connection identifier.</param>
    /// <param name="endpoint">The expected connection endpoint.</param>
    private static void AssertEntry(
        ToolRegistryEntry entry,
        string id,
        string name,
        string description,
        string connectionId,
        string endpoint)
    {
        Assert.Equal(id, entry.Id);
        Assert.Equal(name, entry.Name);
        Assert.Equal(description, entry.Description);
        Assert.Equal(ToolRegistryEntrySource.A2AAgent, entry.Source);
        Assert.Equal(connectionId, entry.SourceId);

        var tool = Assert.IsType<A2AAgentProxyTool>(
            entry.CreateAsync(Mock.Of<IServiceProvider>()).AsTask().GetAwaiter().GetResult());
        Assert.Equal(name, tool.Name);
        Assert.Equal(description, tool.Description);
        Assert.Equal(connectionId, GetPrivateField(tool, "_connectionId"));
        Assert.Equal(endpoint, GetPrivateField(tool, "_endpoint"));
    }

    /// <summary>
    /// Gets a private string field from an A2A proxy tool.
    /// </summary>
    /// <param name="tool">The proxy tool.</param>
    /// <param name="fieldName">The field name.</param>
    /// <returns>The field value.</returns>
    private static string GetPrivateField(A2AAgentProxyTool tool, string fieldName)
    {
        return Assert.IsType<string>(
            typeof(A2AAgentProxyTool)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(tool));
    }

    private sealed class RecordingLogger : ILogger<A2AToolRegistryProvider>
    {
        /// <summary>
        /// Gets the last recorded log level.
        /// </summary>
        public LogLevel? LogLevel { get; private set; }

        /// <summary>
        /// Gets the last recorded exception.
        /// </summary>
        public Exception Exception { get; private set; }

        /// <summary>
        /// Gets the last formatted log message.
        /// </summary>
        public string Message { get; private set; }

        /// <summary>
        /// Begins a no-op logging scope.
        /// </summary>
        /// <typeparam name="TState">The scope state type.</typeparam>
        /// <param name="state">The scope state.</param>
        /// <returns>The no-op scope.</returns>
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        /// <summary>
        /// Reports that logging is enabled.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <returns><see langword="true"/>.</returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <summary>
        /// Records the latest log event.
        /// </summary>
        /// <typeparam name="TState">The logging state type.</typeparam>
        /// <param name="logLevel">The log level.</param>
        /// <param name="eventId">The event identifier.</param>
        /// <param name="state">The logging state.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="formatter">The message formatter.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            LogLevel = logLevel;
            Exception = exception;
            Message = formatter(state, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        /// <summary>
        /// Gets the shared no-op scope.
        /// </summary>
        public static NullScope Instance { get; } = new();

        /// <summary>
        /// Performs no cleanup.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
