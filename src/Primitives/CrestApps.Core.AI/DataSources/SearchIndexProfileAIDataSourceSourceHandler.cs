using System.ComponentModel.DataAnnotations;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.DataSources;

internal sealed class SearchIndexProfileAIDataSourceSourceHandler : IAIDataSourceSourceHandler
{
    private readonly ISearchIndexProfileManager _indexProfileManager;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexProfileAIDataSourceSourceHandler"/> class.
    /// </summary>
    /// <param name="indexProfileManager">The index profile manager.</param>
    /// <param name="serviceProvider">The service provider.</param>
    public SearchIndexProfileAIDataSourceSourceHandler(
        ISearchIndexProfileManager indexProfileManager,
        IServiceProvider serviceProvider)
    {
        _indexProfileManager = indexProfileManager;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the source type.
    /// </summary>
    public string SourceType => AIDataSourceSourceTypes.SearchIndexProfile;

    /// <summary>
    /// Validates the operation.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask ValidateAsync(AIDataSource dataSource, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(result);

        if (string.IsNullOrWhiteSpace(dataSource.SourceIndexProfileName))
        {
            result.Fail(new ValidationResult("Source index profile is required.", [nameof(AIDataSource.SourceIndexProfileName)]));

            return;
        }

        var sourceProfile = await _indexProfileManager.FindByNameAsync(dataSource.SourceIndexProfileName, cancellationToken);
        if (sourceProfile == null)
        {
            result.Fail(new ValidationResult("The selected source index profile could not be found.", [nameof(AIDataSource.SourceIndexProfileName)]));

            return;
        }

        if (string.Equals(sourceProfile.Type, IndexProfileTypes.AIDocuments, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceProfile.Type, IndexProfileTypes.AIMemory, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceProfile.Type, IndexProfileTypes.DataSource, StringComparison.OrdinalIgnoreCase))
        {
            result.Fail(new ValidationResult("The selected source index profile type is not supported for data sources.", [nameof(AIDataSource.SourceIndexProfileName)]));
        }

        var documentReader = _serviceProvider.GetKeyedService<IDataSourceDocumentReader>(sourceProfile.ProviderName);
        if (documentReader == null)
        {
            result.Fail(new ValidationResult("The selected source index provider cannot read source documents.", [nameof(AIDataSource.SourceIndexProfileName)]));
        }
    }

    /// <summary>
    /// Gets reference type.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        var (sourceProfile, _) = await ResolveSourceProfileAsync(dataSource);

        return sourceProfile?.Type;
    }

    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(AIDataSource dataSource, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (sourceProfile, documentReader) = await ResolveRequiredAsync(dataSource);
        await foreach (var pair in documentReader.ReadAsync(sourceProfile, dataSource.KeyFieldName, dataSource.TitleFieldName, dataSource.ContentFieldName, cancellationToken))
        {
            yield return pair;
        }
    }

    /// <summary>
    /// Reads by ids.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(AIDataSource dataSource, IEnumerable<string> documentIds, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (sourceProfile, documentReader) = await ResolveRequiredAsync(dataSource);
        await foreach (var pair in documentReader.ReadByIdsAsync(sourceProfile, documentIds, dataSource.KeyFieldName, dataSource.TitleFieldName, dataSource.ContentFieldName, cancellationToken))
        {
            yield return pair;
        }
    }

    private async Task<(SearchIndexProfile SourceProfile, IDataSourceDocumentReader DocumentReader)> ResolveRequiredAsync(AIDataSource dataSource)
    {
        var (sourceProfile, documentReader) = await ResolveSourceProfileAsync(dataSource);

        return sourceProfile == null || documentReader == null
            ? throw new InvalidOperationException("The configured SearchIndexProfile data source could not be resolved.")
            : (sourceProfile, documentReader);
    }

    private async Task<(SearchIndexProfile SourceProfile, IDataSourceDocumentReader DocumentReader)> ResolveSourceProfileAsync(AIDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        if (string.IsNullOrWhiteSpace(dataSource.SourceIndexProfileName))
        {
            return (null, null);
        }

        var sourceProfile = await _indexProfileManager.FindByNameAsync(dataSource.SourceIndexProfileName);
        if (sourceProfile == null)
        {
            return (null, null);
        }

        var documentReader = _serviceProvider.GetKeyedService<IDataSourceDocumentReader>(sourceProfile.ProviderName);

        return (sourceProfile, documentReader);
    }
}
