using System.Text.Json.Nodes;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class SearchIndexProfileProvisioningServiceTests
{
    [Fact]
    public async Task Create_ShouldApplyProviderPrefixAndCreateRemoteIndex()
    {
        // Arrange
        var remoteManager = new TestRemoteSearchIndexManager
        {
            Prefix = "tenant-",
        };

        var profileManager = new TestSearchIndexProfileManager();
        var service = CreateService(profileManager, remoteManager);

        // Act
        var result = await service.CreateAsync(new SearchIndexProfile
        {
            Name = "articles",
            IndexName = "articles",
            ProviderName = ElasticsearchConstants.ProviderName,
            Type = IndexProfileTypes.Articles,
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("tenant-articles", remoteManager.CreatedIndexName);
        Assert.Single(profileManager.CreatedProfiles);
        Assert.Equal("tenant-articles", profileManager.CreatedProfiles[0].IndexFullName);
        Assert.Contains(remoteManager.CreatedFields, field => field.Name == "article_id" && field.IsKey);
    }

    [Fact]
    public async Task Create_ShouldRejectExistingRemoteIndex()
    {
        // Arrange
        var remoteManager = new TestRemoteSearchIndexManager
        {
            Prefix = "tenant-",
            ExistsResult = true,
        };

        var service = CreateService(new TestSearchIndexProfileManager(), remoteManager);

        // Act
        var result = await service.CreateAsync(
            new SearchIndexProfile
            {
                Name = "articles",
                IndexName = "articles",
                ProviderName = ElasticsearchConstants.ProviderName,
                Type = IndexProfileTypes.Articles,
            }, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("already exists", StringComparison.Ordinal));
        Assert.Null(remoteManager.CreatedIndexName);
    }

    [Fact]
    public async Task Create_ShouldReturnValidationFailureWhenProviderResolutionThrows()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISearchIndexManager>(ElasticsearchConstants.ProviderName, static (_, _) => throw new InvalidOperationException("Elasticsearch is not configured."));

        using var serviceProvider = services.BuildServiceProvider();
        var service = new SearchIndexProfileProvisioningService(
            new TestSearchIndexProfileManager(),
            serviceProvider,
            NullLogger<SearchIndexProfileProvisioningService>.Instance);

        var result = await service.CreateAsync(new SearchIndexProfile
        {
            Name = "articles",
            IndexName = "articles",
            ProviderName = ElasticsearchConstants.ProviderName,
            Type = IndexProfileTypes.Articles,
        }, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("not configured", StringComparison.Ordinal));
    }

    private static SearchIndexProfileProvisioningService CreateService(ISearchIndexProfileManager profileManager, TestRemoteSearchIndexManager remoteManager)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISearchIndexManager>(ElasticsearchConstants.ProviderName, remoteManager);
        var serviceProvider = services.BuildServiceProvider();

        return new SearchIndexProfileProvisioningService(
            profileManager,
            serviceProvider,
            NullLogger<SearchIndexProfileProvisioningService>.Instance);
    }

    private sealed class TestRemoteSearchIndexManager : ISearchIndexManager
    {
        public bool ExistsResult { get; set; }
        public string Prefix { get; set; }
        public string CreatedIndexName { get; private set; }
        public IReadOnlyCollection<SearchIndexField> CreatedFields { get; private set; }

        public string ComposeIndexFullName(IIndexProfileInfo profile)
        {
            return string.Concat(Prefix, profile.IndexName);
        }

        public Task<bool> ExistsAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistsResult);
        }

        public Task CreateAsync(IIndexProfileInfo profile, IReadOnlyCollection<SearchIndexField> fields, CancellationToken cancellationToken = default)
        {
            CreatedIndexName = profile.IndexFullName;
            CreatedFields = fields;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestSearchIndexProfileManager : ISearchIndexProfileManager
    {
        public List<SearchIndexProfile> CreatedProfiles { get; } = [];

        public ValueTask CreateAsync(SearchIndexProfile model, CancellationToken cancellationToken = default)
        {
            CreatedProfiles.Add(model.Clone());
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(SearchIndexProfile model, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask<SearchIndexProfile> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<SearchIndexProfile>(null);
        }

        public ValueTask<SearchIndexProfile> FindByNameAsync(string name)
        {
            return ValueTask.FromResult<SearchIndexProfile>(null);
        }

        public ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(profile.Type, IndexProfileTypes.Articles, StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult<IReadOnlyCollection<SearchIndexField>>(null);
            }

            IReadOnlyCollection<SearchIndexField> fields = [new SearchIndexField
            {
                Name = "article_id",
                FieldType = SearchFieldType.Keyword,
                IsKey = true,
                IsFilterable = true,
            }, ];
            return ValueTask.FromResult(fields);
        }

        public ValueTask<IEnumerable<SearchIndexProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IEnumerable<SearchIndexProfile>>([]);
        }

        public Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
        {
            return Task.FromResult<IReadOnlyCollection<SearchIndexProfile>>([]);
        }

        public ValueTask<SearchIndexProfile> NewAsync(JsonNode data = null, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new SearchIndexProfile());
        }

        public ValueTask<PageResult<SearchIndexProfile>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            return ValueTask.FromResult(new PageResult<SearchIndexProfile>());
        }

        public Task ResetAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SynchronizeAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask UpdateAsync(SearchIndexProfile model, JsonNode data = null, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<ValidationResultDetails> ValidateAsync(SearchIndexProfile model, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ValidationResultDetails());
        }
    }

}
