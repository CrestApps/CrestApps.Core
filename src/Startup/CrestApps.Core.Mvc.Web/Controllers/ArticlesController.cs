using CrestApps.Core.Services;
using CrestApps.Core.Startup.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Controllers;

public sealed class ArticlesController : Controller
{
    public const string DisplayRouteName = "PublicArticleDisplay";

    private readonly ICatalogManager<Article> _articleManager;

    public ArticlesController(ICatalogManager<Article> articleManager)
    {
        _articleManager = articleManager;
    }

    [HttpGet("articles/{id}", Name = DisplayRouteName)]
    public async Task<IActionResult> Display(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var article = await _articleManager.FindByIdAsync(id);

        if (article is null)
        {
            return NotFound();
        }

        return View(article);
    }
}
