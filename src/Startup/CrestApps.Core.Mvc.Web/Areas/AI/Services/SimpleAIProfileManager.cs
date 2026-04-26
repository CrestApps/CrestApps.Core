using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Data.YesSql.Indexes.AI;
using CrestApps.Core.Models;
using YesSql;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Services;

public sealed class SimpleAIProfileManager : IAIProfileManager
{
    private readonly ISession _session;

    public SimpleAIProfileManager(ISession session)
    {
        _session = session;
    }

    public async ValueTask<AIProfile> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

return await _session.Query<AIProfile, AIProfileIndex>(x => x.ItemId == id).FirstOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _session.Query<AIProfile, AIProfileIndex>().ListAsync(cancellationToken);

return items;
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAsync(AIProfileType type, CancellationToken cancellationToken = default)
    {
        var all = await _session.Query<AIProfile, AIProfileIndex>().ListAsync(cancellationToken);

return all.Where(p => p.Type == type);
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        var items = await _session.Query<AIProfile, AIProfileIndex>(x => x.Source == source).ListAsync(cancellationToken);

return items;
    }

    public async ValueTask<IEnumerable<AIProfile>> FindBySourceAsync(string source, CancellationToken cancellationToken = default)
    {
        return await GetAsync(source, cancellationToken);
    }

    public async ValueTask<AIProfile> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var all = await _session.Query<AIProfile, AIProfileIndex>(x => x.Name == name).ListAsync(cancellationToken);

return all.FirstOrDefault();
    }

    public async ValueTask<AIProfile> GetAsync(string name, string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(source);

        var items = await _session.Query<AIProfile, AIProfileIndex>(x => x.Name == name && x.Source == source).ListAsync(cancellationToken);

return items.FirstOrDefault();
    }

    public async ValueTask CreateAsync(AIProfile model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrEmpty(model.ItemId))
        {
            model.ItemId = Guid.NewGuid().ToString("N");
        }

        if (model.CreatedUtc == default)
        {
            model.CreatedUtc = DateTime.UtcNow;
        }

        await _session.SaveAsync(model);
        await _session.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask UpdateAsync(AIProfile model, JsonNode data = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        await _session.SaveAsync(model);
        await _session.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<bool> DeleteAsync(AIProfile model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        _session.Delete(model);
        await _session.SaveChangesAsync(cancellationToken);

return true;
    }

    public ValueTask<AIProfile> NewAsync(JsonNode data = null, CancellationToken cancellationToken = default)
    {
        var profile = new AIProfile
        {
            ItemId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
        };

return ValueTask.FromResult(profile);
    }

    public ValueTask<AIProfile> NewAsync(string source, JsonNode data = null, CancellationToken cancellationToken = default)
    {
        var profile = new AIProfile
        {
            ItemId = Guid.NewGuid().ToString("N"),
            Source = source,
            CreatedUtc = DateTime.UtcNow,
        };

return ValueTask.FromResult(profile);
    }

    public ValueTask<ValidationResultDetails> ValidateAsync(AIProfile model, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResultDetails();

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            result.Fail(new System.ComponentModel.DataAnnotations.ValidationResult("Name is required.", [nameof(model.Name)]));
        }

        return ValueTask.FromResult(result);
    }

    public async ValueTask<PageResult<AIProfile>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        var query = _session.Query<AIProfile, AIProfileIndex>();

        if (!string.IsNullOrEmpty(context?.Source))
        {
            query = query.Where(x => x.Source == context.Source);
        }

        var skip = (page - 1) * pageSize;
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(skip).Take(pageSize).ListAsync(cancellationToken);

return new PageResult<AIProfile>
        {
            Count = total,
            Entries = items.ToArray(),
        };
    }
}
