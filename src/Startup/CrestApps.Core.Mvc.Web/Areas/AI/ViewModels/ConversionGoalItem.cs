namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

public sealed class ConversionGoalItem
{
    public string Name { get; set; }

    public string Description { get; set; }

    public int MinScore { get; set; }
    public int MaxScore { get; set; } = 10;

}
