using System.Text.Json;
using A2A;
using CrestApps.Core.Mvc.Samples.A2AClient.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SampleA2AClientFactory = CrestApps.Core.Mvc.Samples.A2AClient.Services.A2AClientFactory;

namespace CrestApps.Core.Mvc.Samples.A2AClient.Pages;

public sealed class AgentsModel : PageModel
{
    private static readonly Action<ILogger, Exception> _authenticationFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1001, nameof(AuthenticationFailed)),
            "Authentication failed when communicating with the A2A agent.");

    private static readonly Action<ILogger, Exception> _accessDenied =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1002, nameof(AccessDenied)),
            "Access denied when communicating with the A2A agent.");

    private static readonly Action<ILogger, string, Exception> _failedToCommunicate =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1003, nameof(FailedToCommunicate)),
            "Failed to communicate with the A2A agent at '{AgentUrl}'.");

    private static readonly Action<ILogger, Exception> _failedToLoadAgentCards =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1004, nameof(FailedToLoadAgentCards)),
            "Failed to load agent cards.");

    private static readonly Action<ILogger, Exception> _streamingAuthenticationFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1005, "StreamingAuthenticationFailed"),
            "Authentication failed during streaming.");

    private static readonly Action<ILogger, Exception> _streamingAccessDenied =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1006, "StreamingAccessDenied"),
            "Access denied during streaming.");

    private static readonly Action<ILogger, Exception> _streamingError =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1007, "StreamingError"),
            "Error during A2A streaming.");

    private readonly SampleA2AClientFactory _clientFactory;
    private readonly ILogger<AgentsModel> _logger;

    public AgentsModel(
        SampleA2AClientFactory clientFactory,
        ILogger<AgentsModel> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public List<AgentCard> AgentCards { get; private set; } = [];

    public string ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAgentCardsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRefreshAsync(CancellationToken cancellationToken)
    {
        await LoadAgentCardsAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostSendMessageAsync(string agentUrl, string agentName, string message, bool stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new JsonResult(new { error = "Message is required." });
        }

        var selectedServer = _clientFactory.GetSelectedServer();

        try
        {
            var client = _clientFactory.Create(agentUrl);

            var agentMessage = new Message
            {
                Role = Role.User,
                MessageId = Guid.NewGuid().ToString(),
                ContextId = Guid.NewGuid().ToString(),
                Parts = [Part.FromText(message)],
            };

            if (!string.IsNullOrWhiteSpace(agentName))
            {
                agentMessage.Metadata = new Dictionary<string, JsonElement>
                {
                    ["agentName"] = JsonSerializer.SerializeToElement(agentName),
                };
            }

            var sendRequest = new SendMessageRequest
            {
                Message = agentMessage,
            };

            if (stream)
            {
                return new StreamingA2AResult(client, sendRequest, HttpContext.RequestServices.GetRequiredService<ILogger<StreamingA2AResult>>());
            }

            var response = await client.SendMessageAsync(sendRequest, cancellationToken);

            var responseText = ExtractTextFromResponse(response);

            return new JsonResult(new { response = responseText ?? "The agent did not produce a text response." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            AuthenticationFailed(_logger, ex);

            return new JsonResult(new
            {
                error = "Authentication failed (401 Unauthorized). " +
                            "The A2A host requires authentication. Check the agent card's security schemes for details."
            });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            AccessDenied(_logger, ex);

            return new JsonResult(new
            {
                error = "Access denied (403 Forbidden). " +
                            "You do not have permission to access this agent."
            });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new JsonResult(new
            {
                error = $"The selected server '{selectedServer.DisplayName}' did not expose an A2A host at '{selectedServer.Endpoint.TrimEnd('/')}/a2a'."
            });
        }
        catch (Exception ex)
        {
            FailedToCommunicate(_logger, agentUrl, ex);

            return new JsonResult(new { error = $"An error occurred while communicating with the agent: {ex.Message}" });
        }
    }

    private static string ExtractTextFromResponse(SendMessageResponse response)
    {
        if (response.Message is { } message)
        {
            var texts = message.Parts.Select(p => p.Text).OfType<string>();

            if (texts.Any())
            {
                return string.Join(string.Empty, texts);
            }
        }
        else if (response.Task is { } task)
        {
            if (task.Artifacts?.Count > 0)
            {
                var artifactTexts = task.Artifacts
                    .SelectMany(a => a.Parts ?? [])
                    .Select(p => p.Text)
                    .OfType<string>();

                var combined = string.Join(string.Empty, artifactTexts);

                if (!string.IsNullOrEmpty(combined))
                {
                    return combined;
                }
            }

            if (task.Status.Message?.Parts is not null)
            {
                var statusTexts = task.Status.Message.Parts
                    .Select(p => p.Text)
                    .OfType<string>();

                var combined = string.Join(string.Empty, statusTexts);

                if (!string.IsNullOrEmpty(combined))
                {
                    return combined;
                }
            }
        }

        return null;
    }

    private async Task LoadAgentCardsAsync(CancellationToken cancellationToken)
    {
        var selectedServer = _clientFactory.GetSelectedServer();

        try
        {
            AgentCards = await _clientFactory.GetAgentCardsAsync(cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            ErrorMessage = $"The selected server '{selectedServer.DisplayName}' did not expose an A2A host at '{selectedServer.Endpoint.TrimEnd('/')}/.well-known/agent-card.json'.";
        }
        catch (Exception ex)
        {
            FailedToLoadAgentCards(_logger, ex);
            ErrorMessage = $"An error occurred while loading agent cards from '{selectedServer.DisplayName}': {ex.Message}";
        }
    }

    private static void AuthenticationFailed(ILogger logger, Exception exception)
    {
        _authenticationFailed(logger, exception);
    }

    private static void AccessDenied(ILogger logger, Exception exception)
    {
        _accessDenied(logger, exception);
    }

    private static void FailedToCommunicate(ILogger logger, string agentUrl, Exception exception)
    {
        _failedToCommunicate(logger, agentUrl, exception);
    }

    private static void FailedToLoadAgentCards(ILogger logger, Exception exception)
    {
        _failedToLoadAgentCards(logger, exception);
    }

    /// <summary>
    /// Custom <see cref="IActionResult"/> that streams A2A events as text/event-stream
    /// so the browser receives chunks incrementally.
    /// </summary>
    private sealed class StreamingA2AResult : IActionResult
    {
        private readonly A2A.A2AClient _client;
        private readonly SendMessageRequest _sendRequest;
        private readonly ILogger<StreamingA2AResult> _logger;

        public StreamingA2AResult(
            A2A.A2AClient client,
            SendMessageRequest sendRequest,
            ILogger<StreamingA2AResult> logger)
        {
            _client = client;
            _sendRequest = sendRequest;
            _logger = logger;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var httpResponse = context.HttpContext.Response;
            httpResponse.ContentType = "text/event-stream";
            httpResponse.Headers.CacheControl = "no-cache";
            httpResponse.Headers.Connection = "keep-alive";

            var cancellationToken = context.HttpContext.RequestAborted;

            try
            {
                await foreach (var streamEvent in _client.SendStreamingMessageAsync(_sendRequest, cancellationToken))
                {
                    string chunk = null;

                    if (streamEvent.ArtifactUpdate is { } artifactUpdate)
                    {
                        chunk = string.Join(string.Empty,
                            artifactUpdate.Artifact.Parts.Select(p => p.Text).OfType<string>());
                    }
                    else if (streamEvent.StatusUpdate is { } statusUpdate)
                    {
                        if (IsTerminalState(statusUpdate.Status.State))
                        {
                            // If the task failed, send the error message.

                            if (statusUpdate.Status.State == TaskState.Failed)
                            {
                                var errorText = statusUpdate.Status.Message?.Parts
                                    ?.Select(p => p.Text)
                                    .OfType<string>()
                                    .FirstOrDefault() ?? "Agent task failed.";

                                await httpResponse.WriteAsync($"data: [ERROR]{errorText}\n\n", cancellationToken);
                                await httpResponse.Body.FlushAsync(cancellationToken);
                                break;
                            }

                            await httpResponse.WriteAsync("data: [DONE]\n\n", cancellationToken);
                            await httpResponse.Body.FlushAsync(cancellationToken);
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(chunk))
                    {
                        var escaped = chunk.Replace("\n", "\ndata: ");
                        await httpResponse.WriteAsync($"data: {escaped}\n\n", cancellationToken);
                        await httpResponse.Body.FlushAsync(cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected.
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                StreamingAuthenticationFailed(_logger, ex);
                await WriteErrorAsync(httpResponse, "Authentication failed (401 Unauthorized).");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                StreamingAccessDenied(_logger, ex);
                await WriteErrorAsync(httpResponse, "Access denied (403 Forbidden).");
            }
            catch (Exception ex)
            {
                StreamingError(_logger, ex);
                await WriteErrorAsync(httpResponse, ex.Message);
            }
        }

        private static async Task WriteErrorAsync(HttpResponse httpResponse, string message)
        {
            try
            {
                await httpResponse.WriteAsync($"data: [ERROR]{message}\n\n", CancellationToken.None);
                await httpResponse.Body.FlushAsync(CancellationToken.None);
            }
            catch
            {
                // Response may already be completed.
            }
        }

        private static bool IsTerminalState(TaskState state)
        {
            return state is TaskState.Completed
                or TaskState.Failed
                or TaskState.Canceled
                or TaskState.InputRequired
                or TaskState.Rejected
                or TaskState.AuthRequired;
        }

        private static void StreamingAuthenticationFailed(ILogger logger, Exception exception)
        {
            _streamingAuthenticationFailed(logger, exception);
        }

        private static void StreamingAccessDenied(ILogger logger, Exception exception)
        {
            _streamingAccessDenied(logger, exception);
        }

        private static void StreamingError(ILogger logger, Exception exception)
        {
            _streamingError(logger, exception);
        }
    }
}
