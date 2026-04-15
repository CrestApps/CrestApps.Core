using CrestApps.Core.Blazor.Web.Areas.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Blazor.Web.Services;

public static class ArticleSeedExtensions
{
    public static async Task SeedArticlesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BlazorAppDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        if (await dbContext.Articles.AnyAsync()) return;

        dbContext.Articles.AddRange(
            new Article { ItemId = Guid.NewGuid().ToString("N"), Title = "Getting Started with CrestApps AI", Description = "Learn how to set up your first AI-powered application using the CrestApps framework.", CreatedUtc = DateTime.UtcNow },
            new Article { ItemId = Guid.NewGuid().ToString("N"), Title = "Understanding AI Profiles", Description = "AI Profiles define reusable chat behavior, tools, data sources, and orchestration settings.", CreatedUtc = DateTime.UtcNow },
            new Article { ItemId = Guid.NewGuid().ToString("N"), Title = "Working with Data Sources", Description = "Data Sources connect AI knowledge bases to existing search indexes for retrieval-augmented generation.", CreatedUtc = DateTime.UtcNow }
        );
        await dbContext.SaveChangesAsync();
    }
}
