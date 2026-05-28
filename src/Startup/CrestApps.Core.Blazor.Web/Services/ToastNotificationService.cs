namespace CrestApps.Core.Blazor.Web.Services;

internal sealed class ToastNotificationService
{
    private const int DefaultSuccessDelay = 5000;
    private const int MediumSuccessDelay = 7000;
    private const int LongSuccessDelay = 9000;
    private const int VeryLongSuccessDelay = 12000;
    private const int MaxSuccessDelay = 15000;

    public event Action<ToastNotification> ToastAdded;

    public void ShowSuccess(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A toast message is required.", nameof(message));
        }

        Show(new ToastNotification(
            ToastNotificationLevel.Success,
            message,
            AutoHide: true,
            Delay: GetSuccessDelay(message)));
    }

    public void ShowError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A toast message is required.", nameof(message));
        }

        Show(new ToastNotification(
            ToastNotificationLevel.Error,
            message,
            AutoHide: false,
            Delay: null));
    }

    public void ShowWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A toast message is required.", nameof(message));
        }

        Show(new ToastNotification(
            ToastNotificationLevel.Warning,
            message,
            AutoHide: false,
            Delay: null));
    }

    public void ShowInfo(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A toast message is required.", nameof(message));
        }

        Show(new ToastNotification(
            ToastNotificationLevel.Info,
            message,
            AutoHide: false,
            Delay: null));
    }

    private void Show(ToastNotification notification)
    {
        ToastAdded?.Invoke(notification);
    }

    private static int GetSuccessDelay(string message)
    {
        return message.Length switch
        {
            > 240 => MaxSuccessDelay,
            > 180 => VeryLongSuccessDelay,
            > 120 => LongSuccessDelay,
            > 80 => MediumSuccessDelay,
            _ => DefaultSuccessDelay,
        };
    }
}

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
            ToastNotificationLevel.Success => "bi bi-check-circle-fill",
            ToastNotificationLevel.Error => "bi bi-exclamation-triangle-fill",
            ToastNotificationLevel.Warning => "bi bi-exclamation-circle-fill",
            _ => "bi bi-info-circle-fill",
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

internal enum ToastNotificationLevel
{
    Success,
    Error,
    Warning,
    Info,
}
