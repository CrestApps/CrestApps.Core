using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Mvc.Web.Areas.DataSources.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Mvc.Web.Areas.DataSources.Controllers;

[Area("DataSources")]
[Authorize(Policy = "Admin")]
public sealed class AIDataSourceController : Controller
{
    private readonly ICatalogManager<AIDataSource> _manager;
    private readonly ISearchIndexProfileStore _indexProfileStore;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<AIDataSourceSourceDescriptor> _sourceDescriptors;

    public AIDataSourceController(
        ICatalogManager<AIDataSource> manager,
        ISearchIndexProfileStore indexProfileStore,
        IAIDataSourceIndexingQueue indexingQueue,
        IServiceProvider serviceProvider,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider,
        IOptions<AIDataSourceSourceOptions> sourceOptions)
    {
        _manager = manager;
        _indexProfileStore = indexProfileStore;
        _indexingQueue = indexingQueue;
        _serviceProvider = serviceProvider;
        _dataProtectionProvider = dataProtectionProvider;
        _timeProvider = timeProvider;
        _sourceDescriptors = sourceOptions.Value.Sources
            .OrderBy(source => source.DisplayName.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IActionResult> Index()
    {
        var dataSources = await _manager.GetAllAsync();

        return View(dataSources);
    }

    public async Task<IActionResult> Create()
    {
        var model = new AIDataSourceViewModel
        {
            Source = string.Empty,
        };

        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AIDataSourceViewModel model)
    {
        await ValidateAsync(model);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        var dataSource = await _manager.NewAsync();
        dataSource.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        model.ApplyTo(dataSource, _dataProtectionProvider.CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose));

        await _manager.CreateAsync(dataSource);
        TempData["SuccessMessage"] = "Data source created successfully. Initial synchronization has been queued.";

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var dataSource = await _manager.FindByIdAsync(id);

        if (dataSource == null)
        {
            return NotFound();
        }

        var model = AIDataSourceViewModel.FromDataSource(dataSource);
        await PopulateDropdownsAsync(model);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AIDataSourceViewModel model)
    {
        var dataSource = await _manager.FindByIdAsync(model.ItemId);

        if (dataSource == null)
        {
            return NotFound();
        }

        await ValidateAsync(model);

        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(model);

            return View(model);
        }

        model.ApplyTo(dataSource, _dataProtectionProvider.CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose));

        await _manager.UpdateAsync(dataSource);
        TempData["SuccessMessage"] = "Data source updated successfully. Synchronization has been queued.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync(string id)
    {
        var dataSource = await _manager.FindByIdAsync(id);

        if (dataSource == null)
        {
            return NotFound();
        }

        await _indexingQueue.QueueSyncDataSourceAsync(dataSource, HttpContext.RequestAborted);
        TempData["SuccessMessage"] = "Data source synchronization has been queued.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var dataSource = await _manager.FindByIdAsync(id);

        if (dataSource != null)
        {
            await _manager.DeleteAsync(dataSource);
            TempData["SuccessMessage"] = "Data source deleted successfully. Knowledge-base cleanup has been queued.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateAsync(AIDataSourceViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.DisplayText))
        {
            ModelState.AddModelError(nameof(model.DisplayText), "Display text is required.");
        }

        if (string.IsNullOrWhiteSpace(model.Source))
        {
            ModelState.AddModelError(nameof(model.Source), "Source type is required.");
        }

        if (string.IsNullOrWhiteSpace(model.ContentFieldName))
        {
            ModelState.AddModelError(nameof(model.ContentFieldName), "Content field name is required.");
        }

        if (!string.IsNullOrWhiteSpace(model.AIKnowledgeBaseIndexProfileName))
        {
            var knowledgeBaseIndexProfile = await _indexProfileStore.FindByNameAsync(model.AIKnowledgeBaseIndexProfileName);

            if (knowledgeBaseIndexProfile == null)
            {
                ModelState.AddModelError(nameof(model.AIKnowledgeBaseIndexProfileName), "The selected knowledge base index profile could not be found.");
            }
            else if (!string.Equals(knowledgeBaseIndexProfile.Type, IndexProfileTypes.DataSource, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.AIKnowledgeBaseIndexProfileName), "The selected knowledge base index profile must be an AI Data Source profile.");
            }
        }

        if (string.IsNullOrWhiteSpace(model.Source))
        {
            return;
        }

        var handler = _serviceProvider.GetKeyedService<IAIDataSourceSourceHandler>(model.Source);
        if (handler == null)
        {
            ModelState.AddModelError(nameof(model.Source), "The selected source type is not supported.");

            return;
        }

        var probe = new AIDataSource();
        model.ApplyTo(probe, _dataProtectionProvider.CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose));

        var validation = new CrestApps.Core.Models.ValidationResultDetails();
        await handler.ValidateAsync(probe, validation, HttpContext.RequestAborted);

        foreach (var error in validation.Errors)
        {
            var memberNames = error.MemberNames?.Any() == true ? error.MemberNames : [string.Empty];
            foreach (var memberName in memberNames)
            {
                ModelState.AddModelError(MapValidationMemberName(model.Source, memberName), error.ErrorMessage);
            }
        }
    }

    private static string MapValidationMemberName(string sourceType, string memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return string.Empty;
        }

        if (string.Equals(sourceType, AIDataSourceSourceTypes.Elasticsearch, StringComparison.OrdinalIgnoreCase))
        {
            return memberName switch
            {
                nameof(ElasticsearchSourceMetadata) => nameof(AIDataSourceViewModel.Source),
                nameof(ElasticsearchSourceMetadata.EnvironmentType) => nameof(AIDataSourceViewModel.ElasticsearchEnvironmentType),
                nameof(ElasticsearchSourceMetadata.Url) => nameof(AIDataSourceViewModel.ElasticsearchUrl),
                nameof(ElasticsearchSourceMetadata.CloudId) => nameof(AIDataSourceViewModel.ElasticsearchCloudId),
                nameof(ElasticsearchSourceMetadata.AuthenticationType) => nameof(AIDataSourceViewModel.ElasticsearchAuthenticationType),
                nameof(ElasticsearchSourceMetadata.IndexName) => nameof(AIDataSourceViewModel.ElasticsearchIndexName),
                nameof(ElasticsearchSourceMetadata.Username) => nameof(AIDataSourceViewModel.ElasticsearchUsername),
                nameof(ElasticsearchSourceMetadata.Password) => nameof(AIDataSourceViewModel.ElasticsearchPassword),
                nameof(ElasticsearchSourceMetadata.ApiKey) => nameof(AIDataSourceViewModel.ElasticsearchApiKey),
                nameof(ElasticsearchSourceMetadata.Base64ApiKey) => nameof(AIDataSourceViewModel.ElasticsearchBase64ApiKey),
                nameof(ElasticsearchSourceMetadata.ApiKeyId) => nameof(AIDataSourceViewModel.ElasticsearchApiKeyId),
                nameof(ElasticsearchSourceMetadata.CertificateFingerprint) => nameof(AIDataSourceViewModel.ElasticsearchCertificateFingerprint),
                _ => memberName,
            };
        }

        if (string.Equals(sourceType, AIDataSourceSourceTypes.AzureAISearch, StringComparison.OrdinalIgnoreCase))
        {
            return memberName switch
            {
                nameof(AzureAISearchSourceMetadata) => nameof(AIDataSourceViewModel.Source),
                nameof(AzureAISearchSourceMetadata.Endpoint) => nameof(AIDataSourceViewModel.AzureAISearchEndpoint),
                nameof(AzureAISearchSourceMetadata.AuthenticationType) => nameof(AIDataSourceViewModel.AzureAISearchAuthenticationType),
                nameof(AzureAISearchSourceMetadata.IndexName) => nameof(AIDataSourceViewModel.AzureAISearchIndexName),
                nameof(AzureAISearchSourceMetadata.IdentityClientId) => nameof(AIDataSourceViewModel.AzureAISearchIdentityClientId),
                nameof(AzureAISearchSourceMetadata.ApiKey) => nameof(AIDataSourceViewModel.AzureAISearchApiKey),
                _ => memberName,
            };
        }

        if (string.Equals(sourceType, AIDataSourceSourceTypes.PostgreSQL, StringComparison.OrdinalIgnoreCase))
        {
            return memberName switch
            {
                nameof(PostgreSQLSourceMetadata) => nameof(AIDataSourceViewModel.Source),
                nameof(PostgreSQLSourceMetadata.ConnectionString) => nameof(AIDataSourceViewModel.PostgreSQLConnectionString),
                nameof(PostgreSQLSourceMetadata.TableName) => nameof(AIDataSourceViewModel.PostgreSQLTableName),
                _ => memberName,
            };
        }

        return memberName;
    }

    private async Task PopulateDropdownsAsync(AIDataSourceViewModel model)
    {
        var indexProfiles = await _indexProfileStore.GetAllAsync();

        model.SourceTypes = _sourceDescriptors.Select(descriptor => new SelectListItem(descriptor.DisplayName.Value, descriptor.SourceType)).ToList();
        model.SourceIndexProfiles = indexProfiles
            .Where(p =>
                !string.Equals(p.Type, IndexProfileTypes.AIDocuments, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(p.Type, IndexProfileTypes.AIMemory, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(p.Type, IndexProfileTypes.DataSource, StringComparison.OrdinalIgnoreCase))
            .Select(p => new SelectListItem(p.DisplayText ?? p.Name, p.Name))
            .ToList();

        // Knowledge base index profiles: only DataSource type profiles
        model.KnowledgeBaseIndexProfiles = indexProfiles
            .Where(p => string.Equals(p.Type, IndexProfileTypes.DataSource, StringComparison.OrdinalIgnoreCase))
            .Select(p => new SelectListItem(p.DisplayText ?? p.Name, p.Name))
            .ToList();
    }
}
