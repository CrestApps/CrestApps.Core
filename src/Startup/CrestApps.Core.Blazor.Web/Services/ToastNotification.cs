namespace CrestApps.Core.Blazor.Web.Services;

internal sealed record ToastNotification(
    ToastNotificationLevel Level,
    string Message,
    bool AutoHide,
    int? Delay)
{
    public Guid Id { get; } = Guid.NewGuid();

    public string IconClass =>
        Level switch
        {
            ToastNotificationLevel.Success => "fa-solid fa-circle-check",
            ToastNotificationLevel.Error => "fa-solid fa-triangle-exclamation",
            ToastNotificationLevel.Warning => "fa-solid fa-circle-exclamation",
            _ => "fa-solid fa-circle-info",
        };

    public string ColorClass =>
        Level switch
        {
            ToastNotificationLevel.Success => "text-bg-success",
            ToastNotificationLevel.Error => "text-bg-danger",
            ToastNotificationLevel.Warning => "text-bg-warning",
            _ => "text-bg-info",
        };

    public string CloseButtonClass =>
        Level switch
        {
            ToastNotificationLevel.Warning or ToastNotificationLevel.Info => string.Empty,
            _ => "btn-close-white",
        };
}
