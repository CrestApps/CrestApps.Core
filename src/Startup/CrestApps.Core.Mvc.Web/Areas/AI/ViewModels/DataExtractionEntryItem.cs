namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

public sealed class DataExtractionEntryItem
{
    public string Name { get; set; }

    public string Description { get; set; }

    public bool AllowMultipleValues { get; set; }

    public bool IsUpdatable { get; set; }
}
