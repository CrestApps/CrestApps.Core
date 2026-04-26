using System.ComponentModel.DataAnnotations;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Represents the search Index Profile Provisioning Service.
/// </summary>
public sealed class SearchIndexProfileProvisioningService : ISearchIndexProfileProvisioningService
{
    private readonly ISearchIndexProfileManager _indexProfileManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SearchIndexProfileProvisioningService> _logger;

    public SearchIndexProfileProvisioningService(
        ISearchIndexProfileManager indexProfileManager,
        IServiceProvider serviceProvider,
        ILogger<SearchIndexProfileProvisioningService> logger)
    {
        _indexProfileManager = indexProfileManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ValidationResultDetails> CreateAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ISearchIndexManager indexManager;
        try
        {
            indexManager = _serviceProvider.GetKeyedService<ISearchIndexManager>(profile.ProviderName);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Search provider '{ProviderName}' could not be resolved for remote index provisioning.",
                profile.ProviderName.SanitizeForLog());

            return Fail("The selected search provider is not configured for remote index provisioning.", nameof(SearchIndexProfile.ProviderName));
        }

        if (indexManager == null)
        {
            return Fail("The selected search provider is not configured for remote index provisioning.", nameof(SearchIndexProfile.ProviderName));
        }

        profile.IndexFullName = indexManager.ComposeIndexFullName(profile);

        var validationResult = await _indexProfileManager.ValidateAsync(profile, cancellationToken);
        if (!validationResult.Succeeded)
        {
            return validationResult;
        }

        IReadOnlyCollection<SearchIndexField> fields;
        try
        {
            fields = await _indexProfileManager.GetFieldsAsync(profile, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message, nameof(SearchIndexProfile.EmbeddingDeploymentId));
        }

        if (fields == null)
        {
            return Fail($"The index type '{profile.Type}' is not supported for remote provisioning.", nameof(SearchIndexProfile.Type));
        }

        try
        {
            if (await indexManager.ExistsAsync(profile, cancellationToken))
            {
                return Fail($"The remote index '{profile.IndexFullName}' already exists.", nameof(SearchIndexProfile.IndexName));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to validate remote index '{IndexName}' for provider '{ProviderName}'.",
                profile.IndexFullName.SanitizeForLog(),
                profile.ProviderName.SanitizeForLog());

            return Fail($"Unable to validate whether the remote index '{profile.IndexFullName}' already exists.", nameof(SearchIndexProfile.IndexName));
        }

        try
        {
            await indexManager.CreateAsync(profile, fields, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create remote index '{IndexName}' for provider '{ProviderName}'.",
                profile.IndexFullName.SanitizeForLog(),
                profile.ProviderName.SanitizeForLog());

            return Fail($"Unable to create the remote index '{profile.IndexFullName}'.", nameof(SearchIndexProfile.IndexName));
        }

        try
        {
            await _indexProfileManager.CreateAsync(profile, cancellationToken);
        }
        catch
        {
            await indexManager.DeleteAsync(profile, cancellationToken);

            throw;
        }

        await _indexProfileManager.SynchronizeAsync(profile, cancellationToken);

        return new ValidationResultDetails();
    }

    private static ValidationResultDetails Fail(string message, params string[] memberNames)
    {
        var result = new ValidationResultDetails();
        result.Fail(new ValidationResult(message, memberNames));

        return result;
    }
}
