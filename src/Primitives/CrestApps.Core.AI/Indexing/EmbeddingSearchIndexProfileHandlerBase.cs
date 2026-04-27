using System.ComponentModel.DataAnnotations;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Represents the embedding Search Index Profile Handler Base.
/// </summary>
public abstract class EmbeddingSearchIndexProfileHandlerBase : IndexProfileHandlerBase
{
    private readonly string _type;
    private readonly IAIDeploymentStore _deploymentCatalog;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly ILogger<EmbeddingSearchIndexProfileHandlerBase> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddingSearchIndexProfileHandlerBase"/> class.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="deploymentCatalog">The deployment catalog.</param>
    /// <param name="aiClientFactory">The ai client factory.</param>
    /// <param name="logger">The logger.</param>
    protected EmbeddingSearchIndexProfileHandlerBase(
        string type,
        IAIDeploymentStore deploymentCatalog,
        IAIClientFactory aiClientFactory,
        ILogger<EmbeddingSearchIndexProfileHandlerBase> logger)
    {
        _type = type;
        _deploymentCatalog = deploymentCatalog;
        _aiClientFactory = aiClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Validates the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async ValueTask ValidateAsync(SearchIndexProfile indexProfile, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(indexProfile))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(indexProfile.EmbeddingDeploymentId))
        {
            result.Fail(new ValidationResult("Embedding deployment is required for this index type.", [nameof(SearchIndexProfile.EmbeddingDeploymentId)]));

            return;
        }

        var deployment = await _deploymentCatalog.FindByIdAsync(indexProfile.EmbeddingDeploymentId, cancellationToken);
        if (deployment == null)
        {
            result.Fail(new ValidationResult("The selected embedding deployment could not be found.", [nameof(SearchIndexProfile.EmbeddingDeploymentId)]));

            return;
        }

        if (!deployment.SupportsType(AIDeploymentType.Embedding))
        {
            result.Fail(new ValidationResult("The selected deployment does not support embeddings.", [nameof(SearchIndexProfile.EmbeddingDeploymentId)]));

            return;
        }

        if (string.IsNullOrWhiteSpace(deployment.ClientName) || string.IsNullOrWhiteSpace(deployment.ConnectionName) || string.IsNullOrWhiteSpace(deployment.ModelName))
        {
            result.Fail(new ValidationResult("The selected embedding deployment is missing provider, connection, or model information.", [nameof(SearchIndexProfile.EmbeddingDeploymentId)]));
        }
    }

    /// <summary>
    /// Gets fields.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(indexProfile))
        {
            return null;
        }

        var vectorDimensions = await GetEmbeddingDimensionsAsync(indexProfile, cancellationToken);

        return BuildFields(vectorDimensions);
    }

    /// <summary>
    /// Determines whether handle.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    protected bool CanHandle(SearchIndexProfile indexProfile)
    {
        return string.Equals(indexProfile.Type, _type, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds fields.
    /// </summary>
    /// <param name="vectorDimensions">The vector dimensions.</param>
    protected abstract IReadOnlyCollection<SearchIndexField> BuildFields(int vectorDimensions);
    private async Task<int> GetEmbeddingDimensionsAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken)
    {
        var deployment = await _deploymentCatalog.FindByIdAsync(indexProfile.EmbeddingDeploymentId, cancellationToken);
        if (deployment == null)
        {
            throw new InvalidOperationException("The selected embedding deployment could not be found.");
        }

        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;
        try
        {
            embeddingGenerator = await _aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create an embedding generator for deployment '{DeploymentName}'.", deployment.Name);
            throw new InvalidOperationException("The selected embedding deployment could not be resolved.");
        }

        var embedding = await embeddingGenerator.GenerateAsync(["Sample"], cancellationToken: cancellationToken);
        if (embedding?.Count > 0 && embedding[0].Vector.Length > 0)
        {
            return embedding[0].Vector.Length;
        }

        throw new InvalidOperationException("The selected embedding deployment did not return a valid embedding vector.");
    }
}
