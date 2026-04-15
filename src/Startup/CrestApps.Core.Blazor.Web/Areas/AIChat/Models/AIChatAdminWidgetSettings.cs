namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Models;

public sealed class AIChatAdminWidgetSettings
{
    public const string DefaultSecondaryColor = "#6c757d";
    public string ProfileId { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = DefaultSecondaryColor;
    public bool IsEnabled => !string.IsNullOrWhiteSpace(ProfileId);
}
