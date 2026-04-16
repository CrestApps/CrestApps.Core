using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.Blazor.Web.Areas.AIChat.Models;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

public static class ClaudeSettingsExtensions
{
    public static bool IsConfigured(this ClaudeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.AuthenticationType switch
        {
            ClaudeAuthenticationType.ApiKey => !string.IsNullOrWhiteSpace(settings.ProtectedApiKey),
            _ => false,
        };
    }

    public static bool IsConfigured(this ClaudeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !string.IsNullOrWhiteSpace(options.ApiKey);
    }
}
