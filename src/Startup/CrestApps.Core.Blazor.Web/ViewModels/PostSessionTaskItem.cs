using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class PostSessionTaskItem
{
    public string Name { get; set; }

    public PostSessionTaskType Type { get; set; }

    public string Instructions { get; set; }

    public bool AllowMultipleValues { get; set; }

    public string Options { get; set; }

    public string[] SelectedToolNames { get; set; } = [];

    public string[] SelectedToolInstanceNames { get; set; } = [];
}
