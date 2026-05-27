namespace CrestApps.Core.Blazor.Web.Services;

/// <summary>
/// A scoped service that notifies subscribers when site settings have changed.
/// In Blazor Interactive Server, scoped services live for the duration of the circuit,
/// enabling cross-component communication within the same user session.
/// </summary>
public sealed class SiteSettingsChangedNotifier
{
    /// <summary>
    /// Raised when site settings are saved.
    /// </summary>
    public event Func<Task>? SettingsChanged;

    /// <summary>
    /// Notifies all subscribers that site settings have changed.
    /// </summary>
    public async Task NotifyAsync()
    {
        if (SettingsChanged != null)
        {
            await SettingsChanged.Invoke();
        }
    }
}
