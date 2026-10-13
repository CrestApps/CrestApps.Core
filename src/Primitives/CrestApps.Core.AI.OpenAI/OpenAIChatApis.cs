namespace CrestApps.Core.AI.OpenAI;

/// <summary>
/// The values of a connection's <c>ChatApi</c> setting, which selects the API that chat requests are sent to.
/// </summary>
public static class OpenAIChatApis
{
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
}
