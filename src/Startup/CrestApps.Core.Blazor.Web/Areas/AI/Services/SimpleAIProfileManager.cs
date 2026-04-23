using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Blazor.Web.Areas.AI.Services;

public sealed class SimpleAIProfileManager : IAIProfileManager
{
    private readonly INamedSourceCatalog<AIProfile> _catalog;

    public SimpleAIProfileManager(INamedSourceCatalog<AIProfile> catalog)
    {
        _catalog = catalog;
    }

    public async ValueTask<AIProfile> FindByIdAsync(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return await _catalog.FindByIdAsync(id);
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAllAsync()
    {
        return await _catalog.GetAllAsync();
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAsync(AIProfileType type)
    {
        var all = await _catalog.GetAllAsync();

        return all.Where(p => p.Type == type);
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAsync(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return await _catalog.GetAsync(source);
    }

    public async ValueTask<IEnumerable<AIProfile>> FindBySourceAsync(string source)
    {
        return await GetAsync(source);
    }

    public async ValueTask<AIProfile> FindByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return await _catalog.FindByNameAsync(name);
    }

    public async ValueTask<AIProfile> GetAsync(string name, string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return await _catalog.GetAsync(name, source);
    }

    public async ValueTask CreateAsync(AIProfile model)
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

        await _catalog.CreateAsync(model);
    }

    public async ValueTask UpdateAsync(AIProfile model, JsonNode data = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        await _catalog.UpdateAsync(model);
    }

    public async ValueTask<bool> DeleteAsync(AIProfile model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return await _catalog.DeleteAsync(model);
    }

    public ValueTask<AIProfile> NewAsync(JsonNode data = null)
    {
        var profile = new AIProfile
        {
            ItemId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
        };

        return ValueTask.FromResult(profile);
    }

    public ValueTask<AIProfile> NewAsync(string source, JsonNode data = null)
    {
        var profile = new AIProfile
        {
            ItemId = Guid.NewGuid().ToString("N"),
            Source = source,
            CreatedUtc = DateTime.UtcNow,
        };

        return ValueTask.FromResult(profile);
    }

    public ValueTask<ValidationResultDetails> ValidateAsync(AIProfile model)
    {
        var result = new ValidationResultDetails();

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            result.Fail(new System.ComponentModel.DataAnnotations.ValidationResult("Name is required.", [nameof(model.Name)]));
        }

        return ValueTask.FromResult(result);
    }

    public async ValueTask<PageResult<AIProfile>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        return await _catalog.PageAsync(page, pageSize, context);
    }
}
