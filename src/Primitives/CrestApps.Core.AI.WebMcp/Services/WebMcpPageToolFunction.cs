using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.WebMcp.Services;

/// <summary>
/// An AI function that runs a page tool in the browser tab of a chat connection.
/// </summary>
public sealed class WebMcpPageToolFunction : AIFunction
{
    private readonly WebMcpPageTool _tool;
    private readonly string _connectionId;
    private readonly IWebMcpPageToolClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebMcpPageToolFunction"/> class.
    /// </summary>
    /// <param name="tool">The page tool.</param>
    /// <param name="connectionId">The SignalR connection of the browser tab that runs it.</param>
    /// <param name="client">The client that runs page tools in browser tabs.</param>
    public WebMcpPageToolFunction(
        WebMcpPageTool tool,
        string connectionId,
        IWebMcpPageToolClient client)
    {
        _tool = tool;
        _connectionId = connectionId;
        _client = client;
    }

    /// <summary>
    /// Gets the function name of the page tool.
    /// </summary>
    public override string Name => _tool.Name;

    /// <summary>
    /// Gets what the page tool does.
    /// </summary>
    public override string Description => _tool.Description;

    /// <summary>
    /// Gets the JSON schema of the page tool's input.
    /// </summary>
    public override JsonElement JsonSchema => _tool.InputSchema;

    /// <summary>
    /// Runs the page tool in the browser tab and answers its text, or the reason it failed.
    /// </summary>
    /// <param name="arguments">The arguments the model chose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text the model reads.</returns>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);

        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                values[argument.Key] = argument.Value;
            }
        }

        var result = await _client.ExecuteAsync(_connectionId, _tool.Name, JsonSerializer.Serialize(values), cancellationToken);

        return result.IsError
            ? $"The page tool failed: {result.Text}"
            : result.Text;
    }
}
