#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Extension methods that make function-invoking clients hand tool results to the model as text.
/// </summary>
public static class FunctionInvokingClientExtensions
{
    /// <summary>
    /// Makes the client hand every tool result to the model through <see cref="AIToolResultText.Normalize"/>,
    /// so a result that renders itself reaches the model as its text rather than as serialized JSON.
    /// </summary>
    /// <param name="client">The function-invoking chat client.</param>
    /// <returns>The client, for chaining.</returns>
    public static FunctionInvokingChatClient UseTextToolResults(this FunctionInvokingChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.FunctionInvoker = InvokeAsync;

        return client;
    }

    /// <summary>
    /// Makes the realtime client hand every tool result to the model through
    /// <see cref="AIToolResultText.Normalize"/>, so a result that renders itself reaches the model as its text
    /// rather than as serialized JSON.
    /// </summary>
    /// <param name="client">The function-invoking realtime client.</param>
    /// <returns>The client, for chaining.</returns>
    public static FunctionInvokingRealtimeClient UseTextToolResults(this FunctionInvokingRealtimeClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.FunctionInvoker = InvokeAsync;

        return client;
    }

    private static async ValueTask<object> InvokeAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        var result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);

        return AIToolResultText.Normalize(result);
    }
}
