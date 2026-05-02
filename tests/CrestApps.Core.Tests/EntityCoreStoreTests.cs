using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        var extractedDataStore = services.GetRequiredService<IAIChatSessionExtractedDataStore>();
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

        await extractedDataStore.SaveAsync(
            new AIChatSessionExtractedDataRecord
            {
                ItemId = "session-1",
                SessionId = "session-1",
                ProfileId = "profile-1",
                SessionStartedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                Values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["customer_name"] = ["Mike Alhayek"],
                },
            },
            cancellationToken);
        await committer.CommitAsync(cancellationToken);
        Assert.Single(await extractedDataStore.GetAsync("profile-1", null, null, cancellationToken));
        Assert.True(await extractedDataStore.DeleteAsync("session-1", cancellationToken));
        await committer.CommitAsync(cancellationToken);
        Assert.Empty(await extractedDataStore.GetAsync("profile-1", null, null, cancellationToken));

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

    [Fact]
    public async Task Initialize_entity_core_schema_async_adds_missing_tables_to_existing_database()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);

                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE "CA_CatalogRecords" (
                        "EntityType" TEXT NOT NULL,
                        "ItemId" TEXT NOT NULL,
                        "Payload" TEXT NOT NULL,
                        CONSTRAINT "PK_CA_CatalogRecords" PRIMARY KEY ("EntityType", "ItemId")
                    );
                    CREATE TABLE "CA_AIChatSessions" (
                        "SessionId" TEXT NOT NULL,
                        "Payload" TEXT NOT NULL,
                        CONSTRAINT "PK_CA_AIChatSessions" PRIMARY KEY ("SessionId")
                    );
                    """;

                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var services = CreateEntityCoreServices(databasePath);

            await using (var provider = services.BuildServiceProvider())
            {
                await provider.InitializeEntityCoreSchemaAsync();

                await using var verificationConnection = new SqliteConnection($"Data Source={databasePath}");
                await verificationConnection.OpenAsync(TestContext.Current.CancellationToken);

                var query = verificationConnection.CreateCommand();
                query.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

                await using var reader = await query.ExecuteReaderAsync(TestContext.Current.CancellationToken);

                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            Assert.Contains("CA_Documents", tableNames);
            Assert.Contains("CA_AIChatSessionEvents", tableNames);
            Assert.Contains("CA_AICompletionUsage", tableNames);
            Assert.Contains("CA_AIChatSessionExtractedData", tableNames);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                try
                {
                    File.Delete(databasePath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task Initialize_entity_core_schema_async_migrates_legacy_tables_to_document_schema()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);

                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE "CA_CatalogRecords" (
                        "EntityType" TEXT NOT NULL,
                        "ItemId" TEXT NOT NULL,
                        "Payload" TEXT NOT NULL,
                        "Name" TEXT NULL,
                        "DisplayText" TEXT NULL,
                        "Source" TEXT NULL,
                        "SessionId" TEXT NULL,
                        "ChatInteractionId" TEXT NULL,
                        "ReferenceId" TEXT NULL,
                        "ReferenceType" TEXT NULL,
                        "AIDocumentId" TEXT NULL,
                        "UserId" TEXT NULL,
                        "Type" TEXT NULL,
                        "CreatedUtc" TEXT NULL,
                        "UpdatedUtc" TEXT NULL,
                        CONSTRAINT "PK_CA_CatalogRecords" PRIMARY KEY ("EntityType", "ItemId")
                    );
                    CREATE TABLE "CA_AIChatSessions" (
                        "SessionId" TEXT NOT NULL,
                        "Payload" TEXT NOT NULL,
                        "Title" TEXT NULL,
                        "ProfileId" TEXT NULL,
                        "ProfileName" TEXT NULL,
                        "UserId" TEXT NULL,
                        "CreatedUtc" TEXT NULL,
                        CONSTRAINT "PK_CA_AIChatSessions" PRIMARY KEY ("SessionId")
                    );
                    INSERT INTO "CA_CatalogRecords" ("EntityType","ItemId","Payload","Name","Type")
                    VALUES ('CrestApps.Core.AI.Profiles.AIProfile','item1','{"Name":"test-profile"}','test-profile','Chat');
                    INSERT INTO "CA_AIChatSessions" ("SessionId","Payload","Title","ProfileId")
                    VALUES ('sess1','{"Title":"Hello"}','Hello','prof1');
                    """;

                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            var services = CreateEntityCoreServices(databasePath);

            await using (var provider = services.BuildServiceProvider())
            {
                await provider.InitializeEntityCoreSchemaAsync();

                await using var verificationConnection = new SqliteConnection($"Data Source={databasePath}");
                await verificationConnection.OpenAsync(TestContext.Current.CancellationToken);

                var hasDocuments = false;
                var catalogHasDocumentId = false;
                var catalogHasNoPayload = true;
                var sessionHasDocumentId = false;
                var documentCount = 0L;

                var tableQuery = verificationConnection.CreateCommand();
                tableQuery.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CA_Documents';";
                hasDocuments = Convert.ToInt64(await tableQuery.ExecuteScalarAsync(TestContext.Current.CancellationToken)) == 1;

                var catalogColQuery = verificationConnection.CreateCommand();
                catalogColQuery.CommandText = "PRAGMA table_info('CA_CatalogRecords');";

                await using (var colReader = await catalogColQuery.ExecuteReaderAsync(TestContext.Current.CancellationToken))
                {
                    while (await colReader.ReadAsync(TestContext.Current.CancellationToken))
                    {
                        var columnName = colReader.GetString(1);

                        if (columnName == "DocumentId")
                        {
                            catalogHasDocumentId = true;
                        }

                        if (columnName == "Payload")
                        {
                            catalogHasNoPayload = false;
                        }
                    }
                }

                var sessionColQuery = verificationConnection.CreateCommand();
                sessionColQuery.CommandText = "PRAGMA table_info('CA_AIChatSessions');";

                await using (var colReader = await sessionColQuery.ExecuteReaderAsync(TestContext.Current.CancellationToken))
                {
                    while (await colReader.ReadAsync(TestContext.Current.CancellationToken))
                    {
                        if (colReader.GetString(1) == "DocumentId")
                        {
                            sessionHasDocumentId = true;
                        }
                    }
                }

                var docCountQuery = verificationConnection.CreateCommand();
                docCountQuery.CommandText = "SELECT COUNT(*) FROM CA_Documents;";
                documentCount = Convert.ToInt64(await docCountQuery.ExecuteScalarAsync(TestContext.Current.CancellationToken));

                Assert.True(hasDocuments, "CA_Documents table should exist after migration.");
                Assert.True(catalogHasDocumentId, "CA_CatalogRecords should have DocumentId column.");
                Assert.True(catalogHasNoPayload, "CA_CatalogRecords should not have Payload column after migration.");
                Assert.True(sessionHasDocumentId, "CA_AIChatSessions should have DocumentId column.");
                Assert.Equal(2, documentCount);
            }
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                try
                {
                    File.Delete(databasePath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public async Task Entity_core_ai_profile_store_reads_legacy_profiles_with_missing_type_column()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await EntityCoreTestHarness.CreateAsync();
        using var scope = harness.Services.CreateScope();
        var services = scope.ServiceProvider;
        var profileCatalog = services.GetRequiredService<INamedSourceCatalog<AIProfile>>();
        var profileStore = services.GetRequiredService<IAIProfileStore>();
        var committer = services.GetRequiredService<IStoreCommitter>();
        var dbContext = services.GetRequiredService<CrestAppsEntityDbContext>();

        var profile = new AIProfile
        {
            Name = "legacy-chat-profile",
            DisplayText = "Legacy chat profile",
            Type = AIProfileType.Chat,
            CreatedUtc = DateTime.UtcNow,
        };

        await profileCatalog.CreateAsync(profile, cancellationToken);
        await committer.CommitAsync(cancellationToken);

        _ = await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE CA_CatalogRecords SET Type = NULL WHERE EntityType = {0} AND ItemId = {1}",
            typeof(AIProfile).FullName!,
            profile.ItemId);

        var profiles = await profileStore.GetByTypeAsync(AIProfileType.Chat, cancellationToken);

        Assert.Contains(profiles, item => item.ItemId == profile.ItemId);
    }

    [Fact]
    public async Task Entity_core_search_index_profile_store_reads_legacy_embedding_deployment_id_payload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await EntityCoreTestHarness.CreateAsync();
        using var scope = harness.Services.CreateScope();
        var services = scope.ServiceProvider;
        var indexProfileStore = services.GetRequiredService<ISearchIndexProfileStore>();
        var committer = services.GetRequiredService<IStoreCommitter>();
        var dbContext = services.GetRequiredService<CrestAppsEntityDbContext>();

        var indexProfile = new SearchIndexProfile
        {
            Name = "legacy-docs-index",
            DisplayText = "Legacy docs index",
            IndexName = "legacy-docs-index",
            ProviderName = "Elasticsearch",
            Type = IndexProfileTypes.AIDocuments,
            EmbeddingDeploymentName = "legacy-embedding-id",
            CreatedUtc = DateTime.UtcNow,
        };

        await indexProfileStore.CreateAsync(indexProfile, cancellationToken);
        await committer.CommitAsync(cancellationToken);

        _ = await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE CA_Documents
            SET Content = REPLACE(Content, '"EmbeddingDeploymentName":"legacy-embedding-id"', '"EmbeddingDeploymentId":"legacy-embedding-id"')
            WHERE Id = (SELECT DocumentId FROM CA_CatalogRecords WHERE EntityType = {0} AND ItemId = {1})
            """,
            typeof(SearchIndexProfile).FullName!,
            indexProfile.ItemId);

        var storedProfile = await indexProfileStore.FindByNameAsync(indexProfile.Name, cancellationToken);

        Assert.NotNull(storedProfile);
        Assert.Equal("legacy-embedding-id", storedProfile.EmbeddingDeploymentName);
    }

    [Fact]
    public async Task EnforceNamedSourceUniqueness_RejectsDuplicateNameAndSourceWithinEntityType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await EntityCoreTestHarness.CreateAsync(options => options.EnforceNamedSourceUniqueness = true);
        using var scope = harness.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CrestAppsEntityDbContext>();

        dbContext.CatalogRecords.Add(new CrestApps.Core.Data.EntityCore.Models.CatalogRecord
        {
            EntityType = "TestEntity",
            ItemId = Guid.NewGuid().ToString("N"),
            Name = "duplicate",
            Source = "OpenAI",
            Document = new CrestApps.Core.Data.EntityCore.Models.DocumentRecord
            {
                Type = "TestEntity",
                Content = "{}",
            },
        });
        dbContext.CatalogRecords.Add(new CrestApps.Core.Data.EntityCore.Models.CatalogRecord
        {
            EntityType = "TestEntity",
            ItemId = Guid.NewGuid().ToString("N"),
            Name = "duplicate",
            Source = "OpenAI",
            Document = new CrestApps.Core.Data.EntityCore.Models.DocumentRecord
            {
                Type = "TestEntity",
                Content = "{}",
            },
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () => await dbContext.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task EnforceNamedSourceUniqueness_DefaultsToFalseAndAllowsDuplicates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await EntityCoreTestHarness.CreateAsync();
        using var scope = harness.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CrestAppsEntityDbContext>();

        dbContext.CatalogRecords.Add(new CrestApps.Core.Data.EntityCore.Models.CatalogRecord
        {
            EntityType = "TestEntity",
            ItemId = Guid.NewGuid().ToString("N"),
            Name = "shared",
            Source = "OpenAI",
            Document = new CrestApps.Core.Data.EntityCore.Models.DocumentRecord
            {
                Type = "TestEntity",
                Content = "{}",
            },
        });
        dbContext.CatalogRecords.Add(new CrestApps.Core.Data.EntityCore.Models.CatalogRecord
        {
            EntityType = "TestEntity",
            ItemId = Guid.NewGuid().ToString("N"),
            Name = "shared",
            Source = "OpenAI",
            Document = new CrestApps.Core.Data.EntityCore.Models.DocumentRecord
            {
                Type = "TestEntity",
                Content = "{}",
            },
        });

        await dbContext.SaveChangesAsync(cancellationToken);
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

        public static async Task<EntityCoreTestHarness> CreateAsync(Action<EntityCoreDataStoreOptions> configureStore = null)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            var services = CreateEntityCoreServices(databasePath, configureStore);

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

    private static ServiceCollection CreateEntityCoreServices(
        string databasePath,
        Action<EntityCoreDataStoreOptions> configureStore = null)
    {
        var services = new ServiceCollection();

        services.AddHttpContextAccessor();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCoreAIServices();
        services.AddCoreEntityCoreDataStore(options => options.UseSqlite($"Data Source={databasePath}"), store =>
        {
            store.TablePrefix = "CA_";
            configureStore?.Invoke(store);
        });
        services.AddEntityCoreStores();

        return services;
    }
}
