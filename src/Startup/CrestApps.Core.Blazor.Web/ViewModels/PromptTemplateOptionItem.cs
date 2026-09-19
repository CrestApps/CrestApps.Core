namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class PromptTemplateOptionItem
{
    public string TemplateId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string Category { get; set; }

    public List<PromptTemplateParameterItem> Parameters { get; set; } = [];
}
