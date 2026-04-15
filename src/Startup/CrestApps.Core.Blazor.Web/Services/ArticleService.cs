using CrestApps.Core.Blazor.Web.Areas.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Blazor.Web.Services;

public sealed class ArticleService
{
    private readonly BlazorAppDbContext _dbContext;

    public ArticleService(BlazorAppDbContext dbContext) => _dbContext = dbContext;

    public async Task<List<Article>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Articles.OrderByDescending(a => a.CreatedUtc).ToListAsync(cancellationToken);

    public async Task<(List<Article> Items, int Total)> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var total = await _dbContext.Articles.CountAsync(cancellationToken);
        var items = await _dbContext.Articles.OrderByDescending(a => a.CreatedUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<Article?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        => await _dbContext.Articles.FirstOrDefaultAsync(a => a.ItemId == id, cancellationToken);

    public async Task CreateAsync(Article article, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(article.ItemId)) article.ItemId = Guid.NewGuid().ToString("N");
        if (article.CreatedUtc == default) article.CreatedUtc = DateTime.UtcNow;
        _dbContext.Articles.Add(article);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Article article, CancellationToken cancellationToken = default)
    {
        _dbContext.Articles.Update(article);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var article = await FindByIdAsync(id, cancellationToken);
        if (article is null) return false;
        _dbContext.Articles.Remove(article);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
