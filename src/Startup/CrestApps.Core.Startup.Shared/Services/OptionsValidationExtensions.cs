using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

public static class OptionsValidationExtensions
{
    public static bool TryGetValidValue<TOptions>(this IOptions<TOptions> optionsAccessor, out TOptions value)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);

        try
        {
            value = optionsAccessor.Value;

            return true;
        }
        catch (OptionsValidationException)
        {
            value = null;

            return false;
        }
    }

    public static bool TryGetValidValue<TOptions>(this IOptionsSnapshot<TOptions> optionsAccessor, out TOptions value)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);

        try
        {
            value = optionsAccessor.Value;

            return true;
        }
        catch (OptionsValidationException)
        {
            value = null;

            return false;
        }
    }

    public static bool TryGetValidValue<TOptions>(this IOptionsMonitor<TOptions> optionsAccessor, out TOptions value)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);

        try
        {
            value = optionsAccessor.CurrentValue;

            return true;
        }
        catch (OptionsValidationException)
        {
            value = null;

            return false;
        }
    }
}
