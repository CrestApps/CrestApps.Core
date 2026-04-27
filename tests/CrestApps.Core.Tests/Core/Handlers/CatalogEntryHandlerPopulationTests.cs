using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Handlers;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Handlers;

public sealed class CatalogEntryHandlerPopulationTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AIProfileHandler_MapsKnownPropertiesAndDefaults()
    {
        var handler = new AIProfileHandler(
            CreateHttpContextAccessor(),
            new StubTimeProvider(new DateTimeOffset(2026, 4, 27, 21, 0, 0, TimeSpan.Zero)),
            Mock.Of<IAIProfileStore>(),
            CreateStringLocalizer<AIProfileHandler>());
        var profile = new AIProfile();
        JsonObject data = new()
        {
            [nameof(AIProfile.Name)] = "agent-profile",
            [nameof(AIProfile.DisplayText)] = "Agent Profile",
            [nameof(AIProfile.Source)] = "test",
            [nameof(AIProfile.Description)] = "Does work",
            [nameof(AIProfile.ChatDeploymentName)] = "chat-deployment",
            [nameof(AIProfile.UtilityDeploymentName)] = "utility-deployment",
            [nameof(AIProfile.Type)] = "Agent",
            [nameof(AIProfile.TitleType)] = "Generated",
            [nameof(AIProfileMetadata.SystemMessage)] = "System",
            [nameof(AIProfileMetadata.InitialPrompt)] = "Initial",
            [nameof(AIProfileMetadata.Temperature)] = 0.5,
            [nameof(AIProfileSettings.LockSystemMessage)] = true,
            [nameof(ChatModeProfileSettings.ChatMode)] = "Conversation",
            [nameof(ChatModeProfileSettings.VoiceName)] = "alloy",
        };

        await handler.InitializingAsync(new InitializingContext<AIProfile>(profile, data), CancellationToken);
        await handler.InitializedAsync(new InitializedContext<AIProfile>(profile), CancellationToken);
        var validatingContext = new ValidatingContext<AIProfile>(profile);
        await handler.ValidatingAsync(validatingContext, CancellationToken);

        Assert.Equal("agent-profile", profile.Name);
        Assert.Equal("Agent Profile", profile.DisplayText);
        Assert.Equal("test", profile.Source);
        Assert.Equal("Does work", profile.Description);
        Assert.Equal("chat-deployment", profile.ChatDeploymentName);
        Assert.Equal("utility-deployment", profile.UtilityDeploymentName);
        Assert.Equal(AIProfileType.Agent, profile.Type);
        Assert.Equal(AISessionTitleType.Generated, profile.TitleType);
        Assert.Equal("System", profile.GetOrCreate<AIProfileMetadata>().SystemMessage);
        Assert.Equal("Initial", profile.GetOrCreate<AIProfileMetadata>().InitialPrompt);
        Assert.Equal(0.5f, profile.GetOrCreate<AIProfileMetadata>().Temperature);
        Assert.True(profile.GetSettings<AIProfileSettings>().LockSystemMessage);
        Assert.Equal(ChatMode.Conversation, profile.GetSettings<ChatModeProfileSettings>().ChatMode);
        Assert.Equal("alloy", profile.GetSettings<ChatModeProfileSettings>().VoiceName);
        Assert.Equal("user-1", profile.OwnerId);
        Assert.Equal("alice", profile.Author);
        Assert.True(profile.CreatedUtc != default);
        Assert.True(validatingContext.Result.Succeeded);
    }

    [Fact]
    public async Task AIDataSourceHandler_MapsKnownPropertiesAndValidatesRequiredValues()
    {
        var queue = new Mock<IAIDataSourceIndexingQueue>();
        var handler = new AIDataSourceCatalogIndexingHandler(
            CreateHttpContextAccessor(),
            new StubTimeProvider(new DateTimeOffset(2026, 4, 27, 21, 0, 0, TimeSpan.Zero)),
            queue.Object,
            NullLogger<AIDataSourceCatalogIndexingHandler>.Instance);
        var dataSource = new AIDataSource();
        JsonObject data = new()
        {
            [nameof(AIDataSource.DisplayText)] = "Docs",
            [nameof(AIDataSource.SourceIndexProfileName)] = "source-index",
            [nameof(AIDataSource.AIKnowledgeBaseIndexProfileName)] = "kb-index",
            [nameof(AIDataSource.KeyFieldName)] = "id",
            [nameof(AIDataSource.TitleFieldName)] = "title",
            [nameof(AIDataSource.ContentFieldName)] = "content",
        };

        await handler.InitializingAsync(new InitializingContext<AIDataSource>(dataSource, data), CancellationToken);
        await handler.InitializedAsync(new InitializedContext<AIDataSource>(dataSource), CancellationToken);
        var validatingContext = new ValidatingContext<AIDataSource>(dataSource);
        await handler.ValidatingAsync(validatingContext, CancellationToken);

        Assert.Equal("Docs", dataSource.DisplayText);
        Assert.Equal("source-index", dataSource.SourceIndexProfileName);
        Assert.Equal("kb-index", dataSource.AIKnowledgeBaseIndexProfileName);
        Assert.Equal("id", dataSource.KeyFieldName);
        Assert.Equal("title", dataSource.TitleFieldName);
        Assert.Equal("content", dataSource.ContentFieldName);
        Assert.Equal("user-1", dataSource.OwnerId);
        Assert.Equal("alice", dataSource.Author);
        Assert.True(dataSource.CreatedUtc != default);
        Assert.True(validatingContext.Result.Succeeded);
    }

    [Fact]
    public async Task DefaultSearchIndexProfileHandler_MapsKnownPropertiesAndDefaults()
    {
        var handler = new DefaultSearchIndexProfileHandler(
            CreateHttpContextAccessor(),
            new StubTimeProvider(new DateTimeOffset(2026, 4, 27, 21, 0, 0, TimeSpan.Zero)),
            Mock.Of<ISearchIndexProfileStore>());
        var profile = new SearchIndexProfile();
        JsonObject data = new()
        {
            [nameof(SearchIndexProfile.Name)] = "articles",
            [nameof(SearchIndexProfile.DisplayText)] = "Articles",
            [nameof(SearchIndexProfile.IndexName)] = "articles-index",
            [nameof(SearchIndexProfile.ProviderName)] = "provider",
            [nameof(SearchIndexProfile.Type)] = "Articles",
            [nameof(SearchIndexProfile.EmbeddingDeploymentId)] = "embedding",
        };

        await handler.InitializingAsync(new InitializingContext<SearchIndexProfile>(profile, data), CancellationToken);
        await handler.InitializedAsync(new InitializedContext<SearchIndexProfile>(profile), CancellationToken);
        var validatingContext = new ValidatingContext<SearchIndexProfile>(profile);
        await handler.ValidatingAsync(validatingContext, CancellationToken);

        Assert.Equal("articles", profile.Name);
        Assert.Equal("Articles", profile.DisplayText);
        Assert.Equal("articles-index", profile.IndexName);
        Assert.Equal("provider", profile.ProviderName);
        Assert.Equal("Articles", profile.Type);
        Assert.Equal("embedding", profile.EmbeddingDeploymentId);
        Assert.Equal("user-1", profile.OwnerId);
        Assert.Equal("alice", profile.Author);
        Assert.True(profile.CreatedUtc != default);
        Assert.True(validatingContext.Result.Succeeded);
    }

    [Fact]
    public async Task AIProfileHandler_FailsValidation_WhenNameAlreadyExists()
    {
        var store = new Mock<IAIProfileStore>();
        store.Setup(x => x.FindByNameAsync("agent-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIProfile { ItemId = "existing", Name = "agent-profile" });

        var handler = new AIProfileHandler(
            CreateHttpContextAccessor(),
            new StubTimeProvider(new DateTimeOffset(2026, 4, 27, 21, 0, 0, TimeSpan.Zero)),
            store.Object,
            CreateStringLocalizer<AIProfileHandler>());
        var profile = new AIProfile { ItemId = "new", Name = "agent-profile", Description = "Does work", Type = AIProfileType.Agent };
        var validatingContext = new ValidatingContext<AIProfile>(profile);

        await handler.ValidatingAsync(validatingContext, CancellationToken);

        Assert.False(validatingContext.Result.Succeeded);
        Assert.Contains(validatingContext.Result.Errors, error => error.MemberNames.Contains(nameof(AIProfile.Name)));
    }

    [Fact]
    public async Task DefaultSearchIndexProfileHandler_FailsValidation_WhenNameAlreadyExists()
    {
        var store = new Mock<ISearchIndexProfileStore>();
        store.Setup(x => x.FindByNameAsync("articles", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchIndexProfile { ItemId = "existing", Name = "articles" });

        var handler = new DefaultSearchIndexProfileHandler(
            CreateHttpContextAccessor(),
            new StubTimeProvider(new DateTimeOffset(2026, 4, 27, 21, 0, 0, TimeSpan.Zero)),
            store.Object);
        var profile = new SearchIndexProfile
        {
            ItemId = "new",
            Name = "articles",
            IndexName = "articles-index",
            ProviderName = "provider",
            Type = "Articles",
        };
        var validatingContext = new ValidatingContext<SearchIndexProfile>(profile);

        await handler.ValidatingAsync(validatingContext, CancellationToken);

        Assert.False(validatingContext.Result.Succeeded);
        Assert.Contains(validatingContext.Result.Errors, error => error.MemberNames.Contains(nameof(SearchIndexProfile.Name)));
    }

    [Fact]
    public async Task SseMcpConnectionSettingsHandler_MapsAndProtectsSensitiveFields()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var handler = new SseMcpConnectionSettingsHandler(
            CreateHttpContextAccessor(),
            new StubTimeProvider(new DateTimeOffset(2026, 4, 27, 21, 0, 0, TimeSpan.Zero)),
            dataProtectionProvider);
        var connection = new McpConnection();
        JsonObject data = new()
        {
            [nameof(McpConnection.DisplayText)] = "Server",
            [nameof(McpConnection.Source)] = McpConstants.TransportTypes.Sse,
            [nameof(SseMcpConnectionMetadata.Endpoint)] = "https://example.test/sse",
            [nameof(SseMcpConnectionMetadata.AuthenticationType)] = nameof(ClientAuthenticationType.ApiKey),
            [nameof(SseMcpConnectionMetadata.ApiKey)] = "secret",
            [nameof(SseMcpConnectionMetadata.ApiKeyHeaderName)] = "X-Api-Key",
        };

        await handler.InitializingAsync(new InitializingContext<McpConnection>(connection, data), CancellationToken);
        await handler.InitializedAsync(new InitializedContext<McpConnection>(connection), CancellationToken);
        var validatingContext = new ValidatingContext<McpConnection>(connection);
        await handler.ValidatingAsync(validatingContext, CancellationToken);

        var metadata = connection.GetOrCreate<SseMcpConnectionMetadata>();

        Assert.Equal("Server", connection.DisplayText);
        Assert.Equal(McpConstants.TransportTypes.Sse, connection.Source);
        Assert.Equal("https://example.test/sse", metadata.Endpoint?.ToString());
        Assert.Equal(ClientAuthenticationType.ApiKey, metadata.AuthenticationType);
        Assert.Equal("X-Api-Key", metadata.ApiKeyHeaderName);
        Assert.NotEqual("secret", metadata.ApiKey);
        Assert.Equal("secret", dataProtectionProvider.CreateProtector(McpConstants.DataProtectionPurpose).Unprotect(metadata.ApiKey));
        Assert.Equal("user-1", connection.OwnerId);
        Assert.Equal("alice", connection.Author);
        Assert.True(connection.CreatedUtc != default);
        Assert.True(validatingContext.Result.Succeeded);
    }

    private static HttpContextAccessor CreateHttpContextAccessor()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Name, "alice"),
        ], "Test");
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };

        return new HttpContextAccessor
        {
            HttpContext = context,
        };
    }

    private static PassThroughStringLocalizer<T> CreateStringLocalizer<T>()
        => new PassThroughStringLocalizer<T>();

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class PassThroughStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        public PassThroughStringLocalizer<T> WithCulture(System.Globalization.CultureInfo culture) => this;
    }
}
