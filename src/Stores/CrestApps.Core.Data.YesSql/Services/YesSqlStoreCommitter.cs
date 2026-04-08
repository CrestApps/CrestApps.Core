using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// Commits all staged YesSql writes by flushing the current <see cref="ISession"/>.
/// Registered automatically by <c>AddYesSqlDataStore</c>.
/// </summary>
public sealed class YesSqlStoreCommitter : IStoreCommitter
{
    private readonly ISession _session;
    private readonly ILogger<YesSqlStoreCommitter> _logger;

    public YesSqlStoreCommitter(ISession session, ILogger<YesSqlStoreCommitter> logger)
    {
        _session = session;
        _logger = logger;
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YesSqlStoreCommitter flushing the current YesSql session.");
        await _session.SaveChangesAsync(cancellationToken);
    }
}
