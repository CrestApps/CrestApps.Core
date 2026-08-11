using System.Net;
using System.Text.Json;

namespace CrestApps.Core.AI.Resilience;

/// <summary>
/// Provides shared AI-provider exception inspection helpers.
/// </summary>
public static class AIProviderErrorHelper
{
    private const string ClientResultExceptionName = "ClientResultException";
    private const string RequestFailedExceptionName = "RequestFailedException";

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
        if (!string.Equals(type.Name, ClientResultExceptionName, StringComparison.Ordinal)
             && !string.Equals(type.Name, RequestFailedExceptionName, StringComparison.Ordinal))
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
    /// Extracts the most specific human-readable error message from any AI provider exception.
    /// Works for OpenAI (<c>ClientResultException</c>), Azure (<c>RequestFailedException</c>),
    /// and any other provider that surfaces errors via <see cref="Exception.Message"/>.
    /// </summary>
    /// <param name="ex">The exception to inspect.</param>
    /// <returns>The extracted message, or <see langword="null"/> when none can be determined.</returns>
    public static string TryExtractProviderMessage(Exception ex)
    {
        if (ex is null)
        {
            return null;
        }

        foreach (var currentException in EnumerateExceptions(ex))
        {
            if (!string.Equals(currentException.GetType().Name, ClientResultExceptionName, StringComparison.Ordinal) && !string.Equals(currentException.GetType().Name, RequestFailedExceptionName, StringComparison.Ordinal))
            {
                continue;
            }

            // Prefer the structured error.message from the JSON body when present.
            var jsonBodyIndex = currentException.Message?.LastIndexOf('{') ?? -1;

            if (jsonBodyIndex >= 0)
            {
                try
                {
                    using var logs = JsonDocument.Parse(currentException.Message.Substring(jsonBodyIndex));

                    var errorBody = logs.RootElement;

                    // OpenAI / Azure OpenAI / Anthropic: {"error":{"message":"..."}}
                    if (errorBody.TryGetProperty("error", out var errorNode) && errorNode.TryGetProperty("message", out var errorMessage))
                    {
                        return errorMessage.GetString();
                    }

                    // Azure AI Inference / generic: {"message":"..."}
                    if (errorBody.TryGetProperty("message", out var directMessage))
                    {
                        return directMessage.GetString();
                    }
                }
                catch (JsonException)
                {
                    // Not valid JSON — fall through to raw message.
                }
            }

            if (!string.IsNullOrWhiteSpace(currentException.Message))
            {
                var rawMessage = currentException.Message.TrimEnd();
                var lastNewLineIndex = rawMessage.LastIndexOfAny(['\n', '\r']);
                var lastLine = lastNewLineIndex >= 0 ? rawMessage.Substring(lastNewLineIndex + 1).Trim() : rawMessage.Trim();
                var firstPeriodIndex = lastLine.IndexOf('.');

                return firstPeriodIndex >= 0 ? lastLine.Substring(0, firstPeriodIndex + 1) : lastLine;
            }
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
