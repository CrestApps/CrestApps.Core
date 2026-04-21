using System.Text.Json;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using CrestApps.Core.Startup.Shared.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Startup.Shared.Services;

public static class ArticleSeeder
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Seeds the database with sample articles on first run. Subsequent runs
    /// skip seeding because articles already exist.
    /// </summary>
    public static async Task SeedArticlesAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalog<Article>>();
        var existing = await catalog.GetAllAsync();

        if (existing.Count > 0)
        {
            return;
        }

        var articles = LoadSeedArticles();

        foreach (var article in articles)
        {
            await catalog.CreateAsync(article);
        }

        var committer = scope.ServiceProvider.GetService<IStoreCommitter>();

        if (committer != null)
        {
            await committer.CommitAsync();
        }
    }

    private static List<Article> LoadSeedArticles()
    {
        var assembly = typeof(ArticleSeeder).Assembly;
        var resourceName = "CrestApps.Core.Startup.Shared.Data.articles-seed.json";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

        var seedEntries = JsonSerializer.Deserialize<List<ArticleSeedEntry>>(stream, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize seed articles.");

        return seedEntries.Select(entry => new Article
        {
            ItemId = UniqueId.GenerateId(),
            Title = entry.Title,
            Description = entry.Description,
            CreatedUtc = DateTime.UtcNow,
        }).ToList();
    }

    private sealed class ArticleSeedEntry
    {
        public string Title { get; set; }

        public string Description { get; set; }
    }
}
