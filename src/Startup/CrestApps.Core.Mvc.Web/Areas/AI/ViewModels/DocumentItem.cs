namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

public sealed class DocumentItem
{
    public string DocumentId { get; set; }

    public string FileName { get; set; }

    public string ContentType { get; set; }

    public long FileSize { get; set; }
}
