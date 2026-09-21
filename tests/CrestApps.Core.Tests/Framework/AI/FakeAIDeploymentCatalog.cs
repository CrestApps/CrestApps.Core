using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Builds a real <see cref="DefaultAIDeploymentManager"/> over an in-memory catalog, so tests exercise the
/// slot registry and the resolution chain itself rather than a mock of them.
/// </summary>
internal static class FakeAIDeploymentCatalog
{
    /// <summary>
    /// Creates a deployment that declares the given capability features.
    /// </summary>
    public static AIDeployment CreateDeployment(string name, params string[] features)
    {
        var deployment = new AIDeployment
        {
            ItemId = name,
            Name = name,
            ModelName = name,
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Features = features,
        });

        return deployment;
    }

    /// <summary>
    /// Creates a deployment manager over the given deployments and site defaults.
    /// </summary>
    public static DefaultAIDeploymentManager CreateManager(DefaultAIDeploymentSettings settings, params AIDeployment[] deployments)
    {
        return new DefaultAIDeploymentManager(
            new FakeDeploymentStore(deployments),
            [],
            new StaticOptionsMonitor<DefaultAIDeploymentSettings>(settings),
            Options.Create(AIDeploymentSlotOptions.CreateDefault()),
            NullLogger<DefaultAIDeploymentManager>.Instance);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<T, string> listener) => null;
    }

    private sealed class FakeDeploymentStore : IAIDeploymentStore
    {
        private readonly IReadOnlyCollection<AIDeployment> _deployments;

        public FakeDeploymentStore(IReadOnlyCollection<AIDeployment> deployments)
        {
            _deployments = deployments;
        }

        public ValueTask<AIDeployment> FindByIdAsync(string id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_deployments.FirstOrDefault(d => string.Equals(d.ItemId, id, StringComparison.Ordinal)));

        public ValueTask<AIDeployment> FindByNameAsync(string name, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_deployments.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAllAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_deployments);

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>([.. _deployments.Where(d => ids.Contains(d.ItemId, StringComparer.Ordinal))]);

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(string source, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>([.. _deployments.Where(d => string.Equals(d.Source, source, StringComparison.Ordinal))]);

        public ValueTask<AIDeployment> GetAsync(string name, string source, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_deployments.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Source, source, StringComparison.Ordinal)));

        public ValueTask<PageResult<AIDeployment>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
            where TQuery : QueryContext
            => ValueTask.FromResult(new PageResult<AIDeployment>());

        public ValueTask CreateAsync(AIDeployment model, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask UpdateAsync(AIDeployment model, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<bool> DeleteAsync(AIDeployment model, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
