using CrestApps.Core.Blazor.Web.Areas.Admin.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Blazor.Web.Services;

public sealed class ArticleCatalog : ICatalog<Article>
{
    private readonly BlazorAppDbContext _dbContext;

    public ArticleCatalog(BlazorAppDbContext dbContext) => _dbContext = dbContext;

    public async ValueTask<Article> FindByIdAsync(string id)
        => await _dbContext.Articles.FirstOrDefaultAsync(a => a.ItemId == id) ?? null!;

    public async ValueTask<IReadOnlyCollection<Article>> GetAllAsync()
        => await _dbContext.Articles.OrderByDescending(a => a.CreatedUtc).ToListAsync();

    public async ValueTask<IReadOnlyCollection<Article>> GetAsync(IEnumerable<string> ids)
    {
        var idList = ids.ToList();
        return await _dbContext.Articles.Where(a => idList.Contains(a.ItemId)).ToListAsync();
    }

    public async ValueTask<PageResult<Article>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var skip = (page - 1) * pageSize;
        var total = await _dbContext.Articles.CountAsync();
        var items = await _dbContext.Articles
            .OrderByDescending(a => a.CreatedUtc)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

        return new PageResult<Article> { Count = total, Entries = items.ToArray() };
    }

    public async ValueTask CreateAsync(Article entry)
    {
        if (string.IsNullOrEmpty(entry.ItemId)) entry.ItemId = Guid.NewGuid().ToString("N");
        if (entry.CreatedUtc == default) entry.CreatedUtc = DateTime.UtcNow;
        _dbContext.Articles.Add(entry);
        await _dbContext.SaveChangesAsync();
    }

    public async ValueTask UpdateAsync(Article entry)
    {
        _dbContext.Articles.Update(entry);
        await _dbContext.SaveChangesAsync();
    }

    public async ValueTask<bool> DeleteAsync(Article entry)
    {
        _dbContext.Articles.Remove(entry);
        await _dbContext.SaveChangesAsync();
        return true;
    }
}
