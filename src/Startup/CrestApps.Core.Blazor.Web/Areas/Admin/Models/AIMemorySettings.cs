namespace CrestApps.Core.Blazor.Web.Areas.Admin.Models;

public sealed class AIMemorySettings
{
    public string IndexProfileName { get; set; } = string.Empty;
    public int TopN { get; set; } = 5;
}
