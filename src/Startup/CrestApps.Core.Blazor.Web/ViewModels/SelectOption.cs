namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class SelectOption
{
    public string Text { get; set; }

    public string Value { get; set; }

    public bool Selected { get; set; }

    public SelectOption()
    {
    }

    public SelectOption(
        string text,
        string value,
        bool selected = false)
    {
        Text = text;
        Value = value;
        Selected = selected;
    }
}
