using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Extensions;

/// <summary>
/// Provides extension methods for AI Function Arguments.
/// </summary>
public static class AIFunctionArgumentsExtensions
{
    /// <summary>
    /// Tries to get first.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public static bool TryGetFirst(this AIFunctionArguments arguments, string key, out object value)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(key);

        return arguments.TryGetValue(key, out value) && value is not null;
    }

    /// <summary>
    /// Gets first value or default.
    /// </summary>
    public static T GetFirstValueOrDefault<T>(this AIFunctionArguments arguments, string key, T fallbackValue = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(key);

        if (arguments.TryGetFirst<T>(key, out var value))
        {
            return value;
        }

        return fallbackValue;
    }

    /// <summary>
    /// Tries to get first string.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public static bool TryGetFirstString(this AIFunctionArguments arguments, string key, out string value)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(key);

        return arguments.TryGetFirstString(key, false, out value);
    }

    /// <summary>
    /// Tries to get first string.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="key">The key.</param>
    /// <param name="allowEmptyString">The allow empty string.</param>
    /// <param name="value">The value.</param>
    public static bool TryGetFirstString(this AIFunctionArguments arguments, string key, bool allowEmptyString, out string value)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(key);

        if (arguments.TryGetFirst(key, out value))
        {
            if (!allowEmptyString && string.IsNullOrEmpty(value))
            {
                value = null;
                return false;
            }

            return true;
        }

        value = null;

        return false;
    }

    /// <summary>
    /// Tries to get first.
    /// </summary>
    public static bool TryGetFirst<T>(this AIFunctionArguments arguments, string key, out T value)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(key);

        value = default;
        if (!arguments.TryGetValue(key, out var unsafeValue) || unsafeValue is null)
        {
            return false;
        }

        try
        {
            if (unsafeValue is T alreadyTyped)
            {
                value = alreadyTyped;
                return true;
            }

            if (unsafeValue is JsonElement je)
            {
                value = JsonSerializer.Deserialize<T>(je.GetRawText(), JSOptions.CaseInsensitive);
                return true;
            }

            // Handle nullable types (e.g. int?, DateTime?).
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            var safeValue = Convert.ChangeType(unsafeValue, targetType);
            value = (T)safeValue;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
