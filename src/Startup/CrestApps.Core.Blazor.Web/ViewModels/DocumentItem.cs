namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class DocumentItem
{
    public string DocumentId { get; set; }

    public string FileName { get; set; }

    public string ContentType { get; set; }

    public long FileSize { get; set; }
}
