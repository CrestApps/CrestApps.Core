
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core;

/// <summary>
/// Shared helper methods for safely unprotecting data-protected values.
/// </summary>
public static class DataProtectionHelper
{
    /// <summary>
    /// Attempts to unprotect the given value using the specified <see cref="IDataProtector"/>.
    /// Returns the raw value when unprotection fails (e.g., when the value was stored unprotected).
    /// Failures are logged as warnings.
    /// </summary>
    /// <param name="protector">The data protector to use.</param>
    /// <param name="value">The value to unprotect, or <see langword="null"/>.</param>
    /// <param name="logger">The logger used to record unprotection failures.</param>
    /// <param name="errorMessage">The log message template used when unprotection fails.</param>
    /// <param name="args">The arguments for the log message template.</param>
    /// <returns>The unprotected string, the raw value if unprotection fails, or <see langword="null"/> when the input is null/empty.</returns>
#pragma warning disable CA2254 // The message template is intentionally caller-supplied.
    public static string? Unprotect(IDataProtector protector, string? value, ILogger logger, string errorMessage, params object?[] args)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, errorMessage, args);

            return value;
        }
    }
#pragma warning restore CA2254
}
