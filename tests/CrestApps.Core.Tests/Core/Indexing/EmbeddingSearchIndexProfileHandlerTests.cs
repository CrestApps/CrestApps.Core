using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Indexing;

public sealed class EmbeddingSearchIndexProfileHandlerTests
{
    [Fact]
    public async Task ValidateAsync_ShouldResolveEmbeddingDeploymentById()
    {
        var deployment = CreateEmbeddingDeployment();
        var handler = new AIDocumentSearchIndexProfileHandler(
            new FakeDeploymentStore(deployment),
            new FakeAIClientFactory(new FakeEmbeddingGenerator([0.1f, 0.2f])),
            NullLogger<AIDocumentSearchIndexProfileHandler>.Instance);
        var profile = new SearchIndexProfile
        {
            Type = IndexProfileTypes.AIDocuments,
            EmbeddingDeploymentName = deployment.ItemId,
        };
        var result = new ValidationResultDetails();

        await handler.ValidateAsync(profile, result, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetFieldsAsync_ShouldResolveEmbeddingDeploymentById()
    {
        var deployment = CreateEmbeddingDeployment();
        var handler = new AIDocumentSearchIndexProfileHandler(
            new FakeDeploymentStore(deployment),
            new FakeAIClientFactory(new FakeEmbeddingGenerator([0.1f, 0.2f, 0.3f])),
            NullLogger<AIDocumentSearchIndexProfileHandler>.Instance);
        var profile = new SearchIndexProfile
        {
            Type = IndexProfileTypes.AIDocuments,
            EmbeddingDeploymentName = deployment.ItemId,
        };

        var fields = await handler.GetFieldsAsync(profile, TestContext.Current.CancellationToken);

        var embeddingField = Assert.Single(fields, field => field.Name == DocumentIndexConstants.ColumnNames.Embedding);
        Assert.Equal(3, embeddingField.VectorDimensions);
    }

    private static AIDeployment CreateEmbeddingDeployment()
    {
        return new AIDeployment
        {
            ItemId = "deployment-id",
            Name = "embedding-deployment",
            ClientName = "openai",
            ConnectionName = "default",
            ModelName = "text-embedding-3-small",
            Type = AIDeploymentType.Embedding,
        };
    }

    private sealed class FakeDeploymentStore : IAIDeploymentStore
    {
        private readonly AIDeployment _deployment;

        public FakeDeploymentStore(AIDeployment deployment)
        {
            _deployment = deployment;
        }

        public ValueTask<AIDeployment> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(string.Equals(_deployment.ItemId, id, StringComparison.Ordinal)
                ? _deployment
                : null);
        }

        public ValueTask<bool> DeleteAsync(AIDeployment model, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask CreateAsync(AIDeployment model, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(AIDeployment model, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<AIDeployment> deployments = [_deployment];

            return ValueTask.FromResult(deployments);
        }

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<AIDeployment> deployments = ids.Contains(_deployment.ItemId, StringComparer.Ordinal)
                ? [_deployment]
                : [];

            return ValueTask.FromResult(deployments);
        }

        public ValueTask<PageResult<AIDeployment>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            return ValueTask.FromResult(new PageResult<AIDeployment>());
        }

        public ValueTask<AIDeployment> FindByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(string.Equals(_deployment.Name, name, StringComparison.Ordinal)
                ? _deployment
                : null);
        }

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(string source, CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<AIDeployment> deployments = string.Equals(_deployment.Source, source, StringComparison.Ordinal)
                ? [_deployment]
                : [];

            return ValueTask.FromResult(deployments);
        }

        public ValueTask<AIDeployment> GetAsync(string name, string source, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(
                string.Equals(_deployment.Name, name, StringComparison.Ordinal) &&
                string.Equals(_deployment.Source, source, StringComparison.Ordinal)
                    ? _deployment
                    : null);
        }
    }

    private sealed class FakeAIClientFactory : IAIClientFactory
    {
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

        public FakeAIClientFactory(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            _embeddingGenerator = embeddingGenerator;
        }

        public ValueTask<IChatClient> CreateChatClientAsync(AIDeployment deployment)
        {
            return new((IChatClient)null);
        }

        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIDeployment deployment)
        {
            return new(_embeddingGenerator);
        }

#pragma warning disable MEAI001
        public ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment)
        {
            return new((IImageGenerator)null);
        }

        public ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment)
        {
            return new((ISpeechToTextClient)null);
        }

        public ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment)
        {
            return new((ITextToSpeechClient)null);
        }
#pragma warning restore MEAI001
    }

    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly float[] _vector;

        public FakeEmbeddingGenerator(float[] vector)
        {
            _vector = vector;
        }

        public EmbeddingGeneratorMetadata Metadata { get; } = new("fake");

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var _ in values)
            {
                embeddings.Add(new Embedding<float>(_vector));
            }

            return Task.FromResult(embeddings);
        }

        public object GetService(Type serviceType, object serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
