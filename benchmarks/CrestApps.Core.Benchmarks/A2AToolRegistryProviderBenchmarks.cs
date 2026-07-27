using A2A;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.A2A.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures A2A tool registry construction with in-memory connections and agent cards.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class A2AToolRegistryProviderBenchmarks
{
    private AICompletionContext _context;
    private BenchmarkAgentCardCache _agentCardCache;
    private BenchmarkConnectionCatalog _connectionCatalog;
    private A2AToolRegistryProvider _provider;

    /// <summary>
    /// Gets or sets the number of A2A connections included in each registry request.
    /// </summary>
    [Params(100, 1000)]
    public int ConnectionCount { get; set; }

    /// <summary>
    /// Gets or sets the percentage of skill names that require sanitization.
    /// </summary>
    [Params(0, 20)]
    public int InvalidNamePercentage { get; set; }

    /// <summary>
    /// Creates in-memory connections and agent cards with ten skills per connection.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        const int skillsPerConnection = 10;

        var connections = new Dictionary<string, A2AConnection>(ConnectionCount, StringComparer.Ordinal);
        var cards = new Dictionary<string, Task<AgentCard>>(ConnectionCount, StringComparer.Ordinal);
        var connectionIds = new string[ConnectionCount];

        for (var connectionIndex = 0; connectionIndex < ConnectionCount; connectionIndex++)
        {
            var connectionId = $"connection_{connectionIndex}";
            var skills = new List<AgentSkill>(skillsPerConnection);

            for (var skillIndex = 0; skillIndex < skillsPerConnection; skillIndex++)
            {
                var ordinal = (connectionIndex * skillsPerConnection) + skillIndex;
                var requiresSanitization = ordinal % 100 < InvalidNamePercentage;
                skills.Add(new AgentSkill
                {
                    Id = requiresSanitization
                        ? $"skill-{connectionIndex}-{skillIndex}"
                        : $"skill_{connectionIndex}_{skillIndex}",
                    Name = $"Skill {connectionIndex} {skillIndex}",
                    Description = $"Handles benchmark task {ordinal}.",
                });
            }

            connectionIds[connectionIndex] = connectionId;
            connections.Add(connectionId, new A2AConnection
            {
                ItemId = connectionId,
                DisplayText = $"Connection {connectionIndex}",
                Endpoint = $"https://agent-{connectionIndex}.example",
            });
            cards.Add(connectionId, Task.FromResult(new AgentCard
            {
                Name = $"Agent {connectionIndex}",
                Description = $"Benchmark agent {connectionIndex}.",
                Url = $"https://agent-{connectionIndex}.example",
                Version = "1.0",
                Skills = skills,
            }));
        }

        _context = new AICompletionContext
        {
            A2AConnectionIds = connectionIds,
        };
        _connectionCatalog = new BenchmarkConnectionCatalog(connections);
        _agentCardCache = new BenchmarkAgentCardCache(cards);
        _provider = new A2AToolRegistryProvider(
            _connectionCatalog,
            _agentCardCache,
            NullLogger<A2AToolRegistryProvider>.Instance);
    }

    /// <summary>
    /// Builds registry entries with the original list and name-sanitization implementation.
    /// </summary>
    /// <returns>The registry entries.</returns>
    [Benchmark(Baseline = true)]
    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsLegacyAsync()
    {
        var connectionIds = _context?.A2AConnectionIds;

        if (connectionIds is null || connectionIds.Length == 0)
        {
            return [];
        }

        var entries = new List<ToolRegistryEntry>();

        foreach (var connectionId in connectionIds)
        {
            var connection = await _connectionCatalog.FindByIdAsync(connectionId);

            if (connection is null || string.IsNullOrWhiteSpace(connection.Endpoint))
            {
                continue;
            }

            try
            {
                var agentCard = await _agentCardCache.GetAgentCardAsync(connectionId, connection);

                if (agentCard?.Skills is null)
                {
                    continue;
                }

                foreach (var skill in agentCard.Skills)
                {
                    var skillName = SanitizeToolNameLegacy(skill.Id ?? skill.Name ?? connection.DisplayText);
                    var capturedConnectionId = connectionId;

                    entries.Add(new ToolRegistryEntry
                    {
                        Id = $"a2a:{connectionId}:{skillName}",
                        Name = skillName,
                        Description = skill.Description ?? agentCard.Description,
                        Source = ToolRegistryEntrySource.A2AAgent,
                        SourceId = connectionId,
                        CreateAsync = _ => ValueTask.FromResult<AITool>(
                            new A2AAgentProxyTool(
                                skillName,
                                skill.Description ?? agentCard.Description,
                                connection.Endpoint,
                                capturedConnectionId)),
                    });
                }
            }
            catch (Exception ex)
            {
                NullLogger<A2AToolRegistryProvider>.Instance.LogWarning(
                    ex,
                    "Failed to load agent card for A2A connection '{ConnectionId}'.",
                    connectionId);
            }
        }

        return entries;
    }

    /// <summary>
    /// Builds registry entries with the production implementation.
    /// </summary>
    /// <returns>The registry entries.</returns>
    [Benchmark]
    public Task<IReadOnlyList<ToolRegistryEntry>> GetToolsOptimizedAsync()
    {
        return _provider.GetToolsAsync(_context);
    }

    /// <summary>
    /// Sanitizes a tool name with the original allocation behavior.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>The sanitized tool name.</returns>
    private static string SanitizeToolNameLegacy(string name)
    {
        var sanitized = new char[name.Length];

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            sanitized[i] =
                char.IsLetterOrDigit(c) || c == '_'
                    ? c
                    : '_';
        }

        return new string(sanitized);
    }

    private sealed class BenchmarkAgentCardCache : IA2AAgentCardCacheService
    {
        private readonly Dictionary<string, Task<AgentCard>> _cards;

        /// <summary>
        /// Initializes a new instance of the <see cref="BenchmarkAgentCardCache"/> class.
        /// </summary>
        /// <param name="cards">The agent cards keyed by connection identifier.</param>
        public BenchmarkAgentCardCache(Dictionary<string, Task<AgentCard>> cards)
        {
            _cards = cards;
        }

        /// <summary>
        /// Gets a prebuilt agent card task.
        /// </summary>
        /// <param name="connectionId">The connection identifier.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The agent card task.</returns>
        public Task<AgentCard> GetAgentCardAsync(
            string connectionId,
            A2AConnection connection,
            CancellationToken cancellationToken = default)
        {
            return _cards[connectionId];
        }

        /// <summary>
        /// Performs no invalidation because benchmark cards are immutable.
        /// </summary>
        /// <param name="connectionId">The connection identifier.</param>
        public void Invalidate(string connectionId)
        {
        }
    }

    private sealed class BenchmarkConnectionCatalog : ICatalog<A2AConnection>
    {
        private readonly Dictionary<string, A2AConnection> _connections;

        /// <summary>
        /// Initializes a new instance of the <see cref="BenchmarkConnectionCatalog"/> class.
        /// </summary>
        /// <param name="connections">The connections keyed by identifier.</param>
        public BenchmarkConnectionCatalog(Dictionary<string, A2AConnection> connections)
        {
            _connections = connections;
        }

        /// <summary>
        /// Finds a connection by identifier.
        /// </summary>
        /// <param name="id">The connection identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The connection when found.</returns>
        public ValueTask<A2AConnection> FindByIdAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_connections.GetValueOrDefault(id));
        }

        /// <summary>
        /// Gets all benchmark connections.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>All connections.</returns>
        public ValueTask<IReadOnlyCollection<A2AConnection>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyCollection<A2AConnection>>(_connections.Values);
        }

        /// <summary>
        /// Indicates that creating entries is unsupported.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A value task that does not complete successfully.</returns>
        public ValueTask CreateAsync(
            A2AConnection entry,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Indicates that deleting entries is unsupported.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A value task that does not complete successfully.</returns>
        public ValueTask<bool> DeleteAsync(
            A2AConnection entry,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Indicates that multi-entry lookup is unsupported.
        /// </summary>
        /// <param name="ids">The entry identifiers.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A value task that does not complete successfully.</returns>
        public ValueTask<IReadOnlyCollection<A2AConnection>> GetAsync(
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Indicates that paging is unsupported.
        /// </summary>
        /// <typeparam name="TQuery">The query context type.</typeparam>
        /// <param name="page">The page number.</param>
        /// <param name="pageSize">The page size.</param>
        /// <param name="context">The query context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A value task that does not complete successfully.</returns>
        public ValueTask<PageResult<A2AConnection>> PageAsync<TQuery>(
            int page,
            int pageSize,
            TQuery context,
            CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Indicates that updating entries is unsupported.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A value task that does not complete successfully.</returns>
        public ValueTask UpdateAsync(
            A2AConnection entry,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
