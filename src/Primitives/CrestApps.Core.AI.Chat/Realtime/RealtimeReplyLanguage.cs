#nullable enable
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Resolves the language a realtime assistant should reply in from what the browser already tells us.
/// </summary>
/// <remarks>
/// Realtime models drift into other languages on their own, and asking the model to infer the user's language
/// from a few seconds of audio is not reliable — an English question has come back answered in Vietnamese. The
/// browser locale is a deterministic answer to "what language does this user expect", and every hub request
/// carries it in <c>Accept-Language</c>, so no client change is needed. It pins only the reply: transcription
/// keeps auto-detecting, so a user whose browser is in one language and who speaks another is still heard
/// correctly. A language the user chose explicitly always wins.
/// </remarks>
public static class RealtimeReplyLanguage
{
    /// <summary>
    /// Picks the reply language: the explicitly chosen one when present, otherwise the most preferred valid
    /// culture in the request's <c>Accept-Language</c> header, otherwise <see langword="null"/> (mirror the user).
    /// </summary>
    /// <param name="explicitLanguage">The language the user chose on the client, if any.</param>
    /// <param name="httpContext">The connection's HTTP context, when available.</param>
    public static string? Resolve(string? explicitLanguage, HttpContext? httpContext)
    {
        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return explicitLanguage.Trim();
        }

        return FromAcceptLanguage(httpContext?.Request.Headers.AcceptLanguage.ToString());
    }

    /// <summary>
    /// Returns the first entry of an <c>Accept-Language</c> header that names a real culture, or
    /// <see langword="null"/>. Entries are taken in header order, which is the browser's preference order.
    /// </summary>
    /// <param name="acceptLanguage">The raw header value, e.g. <c>en-US,en;q=0.9,vi;q=0.8</c>.</param>
    public static string? FromAcceptLanguage(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
        {
            return null;
        }

        foreach (var entry in acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = entry.Split(';', 2)[0].Trim();

            // "*" means "anything", which is no preference at all.
            if (tag.Length == 0 || tag == "*")
            {
                continue;
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(tag);

                if (!string.IsNullOrEmpty(culture.Name))
                {
                    return culture.Name;
                }
            }
            catch (CultureNotFoundException)
            {
                // Not a culture the runtime knows; try the next preference.
            }
        }

        return null;
    }
}
