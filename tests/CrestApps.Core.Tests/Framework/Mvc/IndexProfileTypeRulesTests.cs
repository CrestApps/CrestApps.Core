using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Mvc.Web.Areas.Indexing.Controllers;
using CrestApps.Core.Mvc.Web.Areas.Indexing.Services;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class IndexProfileTypeRulesTests
{
    [Theory]
    [InlineData(IndexProfileTypes.AIDocuments, true)]
    [InlineData(IndexProfileTypes.AIMemory, true)]
    [InlineData(IndexProfileTypes.DataSource, true)]
    [InlineData(IndexProfileTypes.Articles, false)]
    public void RequiresEmbedding_ShouldMatchExpectedTypes(string type, bool expected)
    {
        Assert.Equal(expected, IndexProfileTypeRules.RequiresEmbedding(type));
    }

    [Theory]
    [InlineData(IndexProfileTypes.AIDocuments, true)]
    [InlineData(IndexProfileTypes.AIMemory, true)]
    [InlineData(IndexProfileTypes.DataSource, true)]
    [InlineData(IndexProfileTypes.Articles, false)]
    public void SupportsEmbeddingSelection_ShouldHideArticles(string type, bool expected)
    {
        Assert.Equal(expected, IndexProfileTypeRules.SupportsEmbeddingSelection(type));
    }

    [Fact]
    public async Task Delete_ShouldDeleteRemoteIndexForConfiguredProvider()
    {
        // Arrange
        var profile = new SearchIndexProfile
        {
            ItemId = "1",
            ProviderName = ElasticsearchConstants.ProviderName,
            IndexName = "sample-index",
            IndexFullName = "sample-index",
            Type = IndexProfileTypes.Articles,
        };

        var remoteManager = new TestRemoteSearchIndexManager
        {
            ExistsResult = true,
        };

        var profileManager = new TestSearchIndexProfileManager(profile);
        var controller = CreateController(profileManager, remoteManager);

        // Act
        await controller.Delete(profile.ItemId);

        // Assert
        Assert.Equal("sample-index", remoteManager.DeletedIndexName);
        Assert.True(profileManager.DeleteCalled);
    }

    [Fact]
    public async Task Delete_ShouldDeleteLocalProfileWhenRemoteIndexIsAlreadyMissing()
    {
        // Arrange
        var profile = new SearchIndexProfile
        {
            ItemId = "1",
            ProviderName = ElasticsearchConstants.ProviderName,
            IndexName = "sample-index",
            IndexFullName = "sample-index",
            Type = IndexProfileTypes.Articles,
        };
        var remoteManager = new TestRemoteSearchIndexManager
        {
            ExistsResult = false,
        };

        var profileManager = new TestSearchIndexProfileManager(profile);
        var controller = CreateController(profileManager, remoteManager);

        // Act
        await controller.Delete(profile.ItemId);

        // Assert
        Assert.Null(remoteManager.DeletedIndexName);
        Assert.True(profileManager.DeleteCalled);
    }

    [Fact]
    public async Task Delete_ShouldDeleteLocalProfileWhenRemoteIndexNameCannotBeResolved()
    {
        // Arrange
        var profile = new SearchIndexProfile
        {
            ItemId = "1",
            ProviderName = ElasticsearchConstants.ProviderName,
            IndexName = null,
            IndexFullName = null,
            Type = IndexProfileTypes.Articles,
        };

        var remoteManager = new TestRemoteSearchIndexManager();
        var profileManager = new TestSearchIndexProfileManager(profile);
        var controller = CreateController(profileManager, remoteManager);

        // Act
        await controller.Delete(profile.ItemId);

        // Assert
        Assert.Null(remoteManager.DeletedIndexName);
        Assert.True(profileManager.DeleteCalled);
    }

    [Fact]
    public async Task Delete_ShouldKeepLocalProfileWhenRemoteDeleteFailsAndIndexExists()
    {
        // Arrange
        var profile = new SearchIndexProfile
        {
            ItemId = "1",
            ProviderName = ElasticsearchConstants.ProviderName,
            IndexName = "sample-index",
            IndexFullName = "sample-index",
            Type = IndexProfileTypes.Articles,
        };

        var remoteManager = new TestRemoteSearchIndexManager
        {
            ExistsResult = true,
            DeleteException = new InvalidOperationException("Delete failed."),
        };

        var profileManager = new TestSearchIndexProfileManager(profile);
        var controller = CreateController(profileManager, remoteManager);

        // Act
        var result = await controller.Delete(profile.ItemId);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(IndexProfileController.Index), redirect.ActionName);
        Assert.False(profileManager.DeleteCalled);
        Assert.Equal(
            "Unable to delete the remote index 'sample-index'. The index profile was not removed.",
            controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Delete_ShouldIgnoreMissingProvider()
    {
        // Arrange
        var profile = new SearchIndexProfile
        {
            ItemId = "1",
            ProviderName = "Missing",
            IndexName = "sample-index",
            IndexFullName = "sample-index",
            Type = IndexProfileTypes.Articles,
        };

        var profileManager = new TestSearchIndexProfileManager(profile);
        var controller = CreateController(profileManager, null);

        // Act
        await controller.Delete(profile.ItemId);

        // Assert
        Assert.True(profileManager.DeleteCalled);
    }

    [Fact]
    public async Task Rebuild_ShouldDeleteExistingRemoteIndexCreateItAndSynchronizeProfile()
    {
        var profile = new SearchIndexProfile
        {
            ItemId = "1",
            ProviderName = ElasticsearchConstants.ProviderName,
            IndexName = "sample-index",
            IndexFullName = "sample-index",
            Type = IndexProfileTypes.Articles,
        };

        var remoteManager = new TestRemoteSearchIndexManager
        {
            ExistsResult = true,
        };

        var profileManager = new TestSearchIndexProfileManager(profile)
        {
            Fields =
            [
                new SearchIndexField
                {
                    Name = "article_id",
                    FieldType = SearchFieldType.Keyword,
                    IsKey = true,
                },
            ],
        };

        var controller = CreateController(profileManager, remoteManager);

        var result = await controller.Rebuild(profile.ItemId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(IndexProfileController.Index), redirect.ActionName);
        Assert.Equal("sample-index", remoteManager.DeletedIndexName);
        Assert.Equal("sample-index", remoteManager.CreatedIndexName);
        Assert.True(profileManager.SynchronizeCalled);
        Assert.Equal("The index 'sample-index' was rebuilt successfully.", controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task Reindex_ShouldResetAndSynchronizeProfile()
    {
        var profile = new SearchIndexProfile
        {
            ItemId = "1",
            ProviderName = ElasticsearchConstants.ProviderName,
            IndexName = "sample-index",
            IndexFullName = "sample-index",
            Type = IndexProfileTypes.Articles,
        };

        var profileManager = new TestSearchIndexProfileManager(profile);
        var controller = CreateController(profileManager, null);

        var result = await controller.Reindex(profile.ItemId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(IndexProfileController.Index), redirect.ActionName);
        Assert.True(profileManager.ResetCalled);
        Assert.True(profileManager.SynchronizeCalled);
        Assert.Equal("The index 'sample-index' was reindexed successfully.", controller.TempData["SuccessMessage"]);
    }

    private static IndexProfileController CreateController(TestSearchIndexProfileManager profileManager, TestRemoteSearchIndexManager remoteManager)
    {
        var services = new ServiceCollection();
        if (remoteManager != null)
        {
            services.AddKeyedSingleton<ISearchIndexManager>(ElasticsearchConstants.ProviderName, remoteManager);
        }

        var serviceProvider = services.BuildServiceProvider();
        var controller = new IndexProfileController(
            profileManager,
            Mock.Of<ISearchIndexProfileProvisioningService>(),
            new TestDeploymentCatalog(),
            serviceProvider,
            Options.Create(new IndexProfileSourceOptions()),
            NullLogger<IndexProfileController>.Instance);
        controller.ControllerContext = new()
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = serviceProvider,
            },
        };
        controller.Url = Mock.Of<IUrlHelper>();
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private sealed class TestRemoteSearchIndexManager : ISearchIndexManager
    {
        public bool ExistsResult { get; set; }
        public Exception DeleteException { get; set; }
        public string ComposedIndexName { get; set; }
        public string DeletedIndexName { get; private set; }
        public string CreatedIndexName { get; private set; }

        public string ComposeIndexFullName(IIndexProfileInfo profile)
        {
            return ComposedIndexName ?? profile.IndexName;
        }

        public Task<bool> ExistsAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistsResult);
        }

        public Task CreateAsync(IIndexProfileInfo profile, IReadOnlyCollection<SearchIndexField> fields, CancellationToken cancellationToken = default)
        {
            CreatedIndexName = profile.IndexFullName;

            return Task.CompletedTask;
        }

        public Task DeleteAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
        {
            if (DeleteException != null)
            {
                throw DeleteException;
            }

            DeletedIndexName = profile.IndexFullName;
            return Task.CompletedTask;
        }
    }

    private sealed class TestSearchIndexProfileManager : ISearchIndexProfileManager
    {
        private readonly SearchIndexProfile _profile;
        public TestSearchIndexProfileManager(SearchIndexProfile profile)
        {
            _profile = profile;
        }

        public bool DeleteCalled { get; private set; }
        public bool ResetCalled { get; private set; }
        public bool SynchronizeCalled { get; private set; }
        public IReadOnlyCollection<SearchIndexField> Fields { get; set; } = [];

        public ValueTask CreateAsync(SearchIndexProfile model, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(SearchIndexProfile model, CancellationToken cancellationToken = default)
        {
            DeleteCalled = true;
            return ValueTask.FromResult(true);
        }

        public ValueTask<SearchIndexProfile> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(string.Equals(id, _profile.ItemId, StringComparison.Ordinal) ? _profile : null);
        }

        public ValueTask<SearchIndexProfile> FindByNameAsync(string name)
        {
            return ValueTask.FromResult<SearchIndexProfile>(null);
        }

        public ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Fields);
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
            ResetCalled = true;

            return Task.CompletedTask;
        }

        public Task SynchronizeAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
        {
            SynchronizeCalled = true;

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

    private sealed class TestDeploymentCatalog : ICatalog<AIDeployment>
    {
        public ValueTask CreateAsync(AIDeployment entry, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(AIDeployment entry, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask<AIDeployment> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<AIDeployment>(null);
        }

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>([]);
        }

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>([]);
        }

        public ValueTask<PageResult<AIDeployment>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            return ValueTask.FromResult(new PageResult<AIDeployment>());
        }

        public ValueTask UpdateAsync(AIDeployment entry, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
