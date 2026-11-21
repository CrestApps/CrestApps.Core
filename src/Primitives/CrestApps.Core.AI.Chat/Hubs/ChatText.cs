namespace CrestApps.Core.AI.Chat.Hubs;

/// <summary>
/// Text helpers the chat hubs share.
/// </summary>
internal static class ChatText
{
    /// <summary>
    /// Cuts a value to a length.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="maxLength">The most characters to keep.</param>
    /// <returns>The value, cut when it is longer.</returns>
    public static string Truncate(string value, int maxLength)
    {
        return string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
