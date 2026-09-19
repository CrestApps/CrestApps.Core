using CrestApps.Core.Data.YesSql.Indexes;

namespace CrestApps.Core.Mvc.Web.Areas.Admin.Indexes;

public sealed class ArticleIndex : CatalogItemIndex
{
    public string Title { get; set; }
}
