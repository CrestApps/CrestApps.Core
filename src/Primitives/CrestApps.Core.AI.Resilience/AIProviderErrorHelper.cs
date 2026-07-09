using System.Net;

namespace CrestApps.Core.AI.Resilience;

/// <summary>
/// Provides shared AI-provider exception inspection helpers.
/// </summary>
public static class AIProviderErrorHelper
{
    private const string ClientResultExceptionName = "ClientResultException";

    private static readonly string[] _rateLimitIndicators = ["ratelimitreached", "rate limit", "too many requests"];

    /// <summary>
    /// Determines whether the specified exception represents a rate-limit failure.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns><see langword="true"/> when the exception indicates provider rate limiting; otherwise, <see langword="false"/>.</returns>
    public static bool IsRateLimitException(Exception ex)
    {
        if (ex is null)
        {
            return false;
        }

        foreach (var current in EnumerateExceptions(ex))
        {
            if (TryGetClientResultStatusCode(current) == (int)HttpStatusCode.TooManyRequests)
            {
                return true;
            }

            if (current is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            {
                return true;
            }

            if (ContainsRateLimitIndicator(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to read a provider status code from a known client exception shape.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns>The provider status code when available; otherwise, <see langword="null"/>.</returns>
    public static int? TryGetClientResultStatusCode(Exception ex)
    {
        if (ex is null)
        {
            return null;
        }

        var type = ex.GetType();
        if (!string.Equals(type.Name, ClientResultExceptionName, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var statusProperty = type.GetProperty("Status") ?? type.GetProperty("StatusCode");
            if (statusProperty?.GetValue(ex) is int status)
            {
                return status;
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Determines whether the specified message contains a rate-limit indicator.
    /// </summary>
    /// <param name="message">The message to inspect.</param>
    /// <returns><see langword="true"/> when the message appears to describe a rate-limit failure; otherwise, <see langword="false"/>.</returns>
    public static bool ContainsRateLimitIndicator(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        foreach (var indicator in _rateLimitIndicators)
        {
            if (message.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates the provided exception and its inner exceptions.
    /// </summary>
    /// <param name="ex">The starting exception.</param>
    /// <returns>A sequence containing the exception chain.</returns>
    public static IEnumerable<Exception> EnumerateExceptions(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
