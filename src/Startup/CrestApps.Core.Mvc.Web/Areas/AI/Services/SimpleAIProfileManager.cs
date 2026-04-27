using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Services;

public sealed class SimpleAIProfileManager : IAIProfileManager
{
    private readonly INamedSourceCatalog<AIProfile> _catalog;

    public SimpleAIProfileManager(INamedSourceCatalog<AIProfile> catalog)
    {
        _catalog = catalog;
    }

    public async ValueTask<AIProfile> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _catalog.FindByIdAsync(id, cancellationToken);
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _catalog.GetAllAsync(cancellationToken);
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAsync(AIProfileType type, CancellationToken cancellationToken = default)
    {
        var all = await _catalog.GetAllAsync(cancellationToken);

        return all.Where(p => p.Type == type);
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return await _catalog.GetAsync(source, cancellationToken);
    }

    public async ValueTask<IEnumerable<AIProfile>> FindBySourceAsync(string source, CancellationToken cancellationToken = default)
    {
        return await GetAsync(source, cancellationToken);
    }

    public async ValueTask<AIProfile> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return await _catalog.FindByNameAsync(name, cancellationToken);
    }

    public async ValueTask<AIProfile> GetAsync(string name, string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return await _catalog.GetAsync(name, source, cancellationToken);
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

        await _catalog.CreateAsync(model, cancellationToken);
    }

    public async ValueTask UpdateAsync(AIProfile model, JsonNode data = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        await _catalog.UpdateAsync(model, cancellationToken);
    }

    public async ValueTask<bool> DeleteAsync(AIProfile model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        return await _catalog.DeleteAsync(model, cancellationToken);
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
        return await _catalog.PageAsync(page, pageSize, context, cancellationToken);
    }
}
