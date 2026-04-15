using CrestApps.Core.Models;

namespace CrestApps.Core.Blazor.Web.Areas.Admin.Models;

public sealed class Article : CatalogItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}
