using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.OpenAI;

/// <summary>
/// The values of a connection's <c>ChatApi</c> setting, which selects the API that chat requests are sent to.
/// </summary>
public static class OpenAIChatApis
{
    /// <summary>
    /// The name of the connection setting.
    /// </summary>
    public const string SettingName = "ChatApi";

    /// <summary>
    /// Requests that combine function tools with a reasoning effort go to the Responses API on OpenAI's own endpoint
    /// and on Azure OpenAI. Every other endpoint uses Chat Completions only. This is the default.
    /// </summary>
    public const string Auto = "Auto";

    /// <summary>
    /// Every request goes to Chat Completions.
    /// </summary>
    public const string ChatCompletions = "ChatCompletions";

    /// <summary>
    /// Requests that combine function tools with a reasoning effort go to the Responses API on any endpoint.
    /// </summary>
    public const string Responses = "Responses";

    /// <summary>
    /// Reads the connection's <c>ChatApi</c> setting. An empty value is <see cref="Auto"/>. A value that is not one of
    /// the known values is logged as a warning and treated as <see cref="Auto"/>, so a typo is visible in the logs.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="logger">The logger that reports an unknown value, if any.</param>
    /// <returns><see cref="Auto"/>, <see cref="ChatCompletions"/> or <see cref="Responses"/>.</returns>
    public static string GetChatApi(AIProviderConnectionEntry connection, ILogger logger = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var value = connection.GetStringValue(SettingName, false);

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, Auto, StringComparison.OrdinalIgnoreCase))
        {
            return Auto;
        }

        if (string.Equals(value, ChatCompletions, StringComparison.OrdinalIgnoreCase))
        {
            return ChatCompletions;
        }

        if (string.Equals(value, Responses, StringComparison.OrdinalIgnoreCase))
        {
            return Responses;
        }

        logger?.LogWarning(
            "The connection setting '{Setting}' has the unknown value '{Value}', so it is treated as Auto. The known values are Auto, ChatCompletions and Responses.",
            SettingName,
            value);

        return Auto;
    }
}
