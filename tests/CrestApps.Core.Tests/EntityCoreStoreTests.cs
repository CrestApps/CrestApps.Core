using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests;

public sealed class EntityCoreStoreTests
{
    [Fact]
    public async Task Generic_named_source_catalog_supports_round_trip_queries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await EntityCoreTestHarness.CreateAsync();
        using var scope = harness.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<INamedSourceCatalog<AIProfile>>();
        var committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();

        var profile = new AIProfile
        {
            Name = "support-agent",
            Source = "OpenAI",
            DisplayText = "Support agent",
            CreatedUtc = DateTime.UtcNow,
        };

        await catalog.CreateAsync(profile, TestContext.Current.CancellationToken);
        await committer.CommitAsync(cancellationToken);

        var byId = await catalog.FindByIdAsync(profile.ItemId, TestContext.Current.CancellationToken);
        var byName = await catalog.FindByNameAsync(profile.Name, TestContext.Current.CancellationToken);
        var byComposite = await catalog.GetAsync(profile.Name, profile.Source, TestContext.Current.CancellationToken);
        var page = await catalog.PageAsync(1, 10, new QueryContext
        {
            Name = "support",
            Source = "OpenAI",
            Sorted = true,
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(profile.ItemId);
        Assert.Equal(profile.ItemId, byId?.ItemId);
        Assert.Equal(profile.Name, byName?.Name);
        Assert.Equal(profile.ItemId, byComposite?.ItemId);
        Assert.Single(page.Entries);
        Assert.Equal(profile.ItemId, page.Entries.Single().ItemId);
    }

    [Fact]
    public async Task Entity_core_stores_support_specialized_queries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await EntityCoreTestHarness.CreateAsync();
        using var scope = harness.Services.CreateScope();
        var services = scope.ServiceProvider;

        var deploymentStore = services.GetRequiredService<IAIDeploymentStore>();
        var dataSourceStore = services.GetRequiredService<IAIDataSourceStore>();
        var memoryStore = services.GetRequiredService<IAIMemoryStore>();
        var documentStore = services.GetRequiredService<IAIDocumentStore>();
        var chunkStore = services.GetRequiredService<IAIDocumentChunkStore>();
        var sessionPromptStore = services.GetRequiredService<IAIChatSessionPromptStore>();
        var interactionPromptStore = services.GetRequiredService<IChatInteractionPromptStore>();
        var indexProfileStore = services.GetRequiredService<ISearchIndexProfileStore>();
        var profileCatalog = services.GetRequiredService<INamedSourceCatalog<AIProfile>>();
        var sessionManager = services.GetRequiredService<IAIChatSessionManager>();
        var committer = services.GetRequiredService<IStoreCommitter>();

        var deployment = new AIDeployment
        {
            Name = "chat-main",
            Source = "OpenAI",
            CreatedUtc = DateTime.UtcNow,
        };

        await deploymentStore.CreateAsync(deployment, cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Equal(deployment.ItemId, (await deploymentStore.GetAsync(deployment.Name, deployment.Source, cancellationToken))?.ItemId);

        var dataSource = new AIDataSource
        {
            DisplayText = "Knowledge base",
            CreatedUtc = DateTime.UtcNow,
        };

        await dataSourceStore.CreateAsync(dataSource, cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Single(await dataSourceStore.GetAllAsync(cancellationToken));

        var memory = new AIMemoryEntry
        {
            UserId = "user-1",
            Name = "favorite-language",
            Content = "C#",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        await memoryStore.CreateAsync(memory, cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Equal(1, await memoryStore.CountByUserAsync("user-1"));
        Assert.Equal(memory.ItemId, (await memoryStore.FindByUserAndNameAsync("user-1", "favorite-language"))?.ItemId);
        Assert.Single(await memoryStore.GetByUserAsync("user-1"));

        var document = new AIDocument
        {
            ReferenceId = "profile-1",
            ReferenceType = "profile",
            FileName = "guide.md",
            UploadedUtc = DateTime.UtcNow,
        };

        await documentStore.CreateAsync(document, cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Single(await documentStore.GetDocumentsAsync("profile-1", "profile"));

        var chunk = new AIDocumentChunk
        {
            AIDocumentId = document.ItemId,
            ReferenceId = "profile-1",
            ReferenceType = "profile",
            Content = "Chunk content",
            Index = 0,
            Embedding = [0.1f, 0.2f],
        };

        await chunkStore.CreateAsync(chunk, cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Single(await chunkStore.GetChunksByAIDocumentIdAsync(document.ItemId));
        Assert.Single(await chunkStore.GetChunksByReferenceAsync("profile-1", "profile"));
        await chunkStore.DeleteByDocumentIdAsync(document.ItemId);
        await committer.CommitAsync(cancellationToken);
        Assert.Empty(await chunkStore.GetChunksByAIDocumentIdAsync(document.ItemId));

        var sessionPrompt = new AIChatSessionPrompt
        {
            SessionId = "session-1",
            Role = Microsoft.Extensions.AI.ChatRole.User,
            Content = "Hello",
            CreatedUtc = DateTime.UtcNow,
        };

        await sessionPromptStore.CreateAsync(sessionPrompt, cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Equal(1, await sessionPromptStore.CountAsync("session-1"));
        Assert.Single(await sessionPromptStore.GetPromptsAsync("session-1"));
        Assert.Equal(1, await sessionPromptStore.DeleteAllPromptsAsync("session-1"));
        await committer.CommitAsync(cancellationToken);

        var interactionPrompt = new ChatInteractionPrompt
        {
            ChatInteractionId = "interaction-1",
            Role = Microsoft.Extensions.AI.ChatRole.Assistant,
            Text = "Hi",
            CreatedUtc = DateTime.UtcNow,
        };

        await interactionPromptStore.CreateAsync(interactionPrompt, cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Single(await interactionPromptStore.GetPromptsAsync("interaction-1"));
        Assert.Equal(1, await interactionPromptStore.DeleteAllPromptsAsync("interaction-1"));
        await committer.CommitAsync(cancellationToken);

        var indexProfile = new SearchIndexProfile
        {
            Name = "docs-index",
            DisplayText = "Docs index",
            Type = "AIDocuments",
            CreatedUtc = DateTime.UtcNow,
        };

        await indexProfileStore.CreateAsync(indexProfile, cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Equal(indexProfile.ItemId, (await indexProfileStore.FindByNameAsync("docs-index", cancellationToken))?.ItemId);
        Assert.Single(await indexProfileStore.GetByTypeAsync("AIDocuments"));

        var profile = new AIProfile
        {
            Name = "chat-profile",
            Source = "OpenAI",
            DisplayText = "Chat profile",
            CreatedUtc = DateTime.UtcNow,
        };

        await profileCatalog.CreateAsync(profile, cancellationToken);
        await committer.CommitAsync(cancellationToken);

        var session = await sessionManager.NewAsync(profile, new NewAIChatSessionContext(), cancellationToken);
        session.Title = "Welcome";

        await sessionManager.SaveAsync(session, cancellationToken);
        await committer.CommitAsync(cancellationToken);

        var pagedSessions = await sessionManager.PageAsync(1, 10, new AIChatSessionQueryContext
        {
            ProfileId = profile.ItemId,
        }, cancellationToken);

        Assert.Equal(session.SessionId, (await sessionManager.FindByIdAsync(session.SessionId, cancellationToken))?.SessionId);
        Assert.Single(pagedSessions.Sessions);
        Assert.Equal(1, await sessionManager.DeleteAllAsync(profile.ItemId, cancellationToken));
        await committer.CommitAsync(cancellationToken);
        Assert.Null(await sessionManager.FindAsync(session.SessionId, cancellationToken));
    }

    [Fact]
    public async Task Entity_core_store_committer_flushes_staged_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await EntityCoreTestHarness.CreateAsync();
        using var scope = harness.Services.CreateScope();
        var services = scope.ServiceProvider;
        var catalog = services.GetRequiredService<INamedSourceCatalog<AIProfile>>();
        var committer = services.GetRequiredService<IStoreCommitter>();
        var dbContext = services.GetRequiredService<CrestAppsEntityDbContext>();

        var profile = new AIProfile
        {
            Name = "staged-profile",
            Source = "OpenAI",
            DisplayText = "Staged profile",
            CreatedUtc = DateTime.UtcNow,
        };

        await catalog.CreateAsync(profile, cancellationToken);

        Assert.True(dbContext.ChangeTracker.HasChanges());

        await committer.CommitAsync(cancellationToken);

        Assert.NotNull(await catalog.FindByNameAsync(profile.Name, cancellationToken));
    }

    private sealed class EntityCoreTestHarness : IAsyncDisposable
    {
        private readonly string _databasePath;

        private EntityCoreTestHarness(
            string databasePath,
            ServiceProvider services)
        {
            _databasePath = databasePath;
            Services = services;
        }

        public ServiceProvider Services { get; }

        public static async Task<EntityCoreTestHarness> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();

            services.AddHttpContextAccessor();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddCoreAIServices();
            services.AddCoreEntityCoreSqliteDataStore($"Data Source={databasePath}");
            services.AddEntityCoreStores();

            var provider = services.BuildServiceProvider();
            await provider.InitializeEntityCoreSchemaAsync();

            return new EntityCoreTestHarness(databasePath, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();

            if (File.Exists(_databasePath))
            {
                try
                {
                    File.Delete(_databasePath);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
