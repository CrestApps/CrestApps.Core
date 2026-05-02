using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Support.Json;
using CrestApps.Core.Templates.Parsing;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Processes configured post-session tasks after a chat session is closed.
/// Analyzes the full conversation transcript using AI to produce structured results
/// such as disposition, summary, or sentiment.
/// </summary>
public sealed class PostSessionProcessingService
{
    private readonly IAIClientFactory _clientFactory;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIToolsService _toolsService;
    private readonly ITemplateService _aiTemplateService;
    private readonly ITemplateParser _markdownTemplateParser;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly DefaultAIOptions _defaultOptions;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PostSessionProcessingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostSessionProcessingService"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="toolsService">The tools service.</param>
    /// <param name="aiTemplateService">The ai template service.</param>
    /// <param name="templateParsers">The registered template parsers.</param>
    /// <param name="defaultOptions">The default options.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="deploymentManager">The deployment manager.</param>
    public PostSessionProcessingService(
        IAIClientFactory clientFactory,
        IAIToolsService toolsService,
        ITemplateService aiTemplateService,
        IEnumerable<ITemplateParser> templateParsers,
        DefaultAIOptions defaultOptions,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        IAIDeploymentManager deploymentManager = null)
    {
        _clientFactory = clientFactory;
        _deploymentManager = deploymentManager;
        _toolsService = toolsService;
        _aiTemplateService = aiTemplateService;
        _markdownTemplateParser = ResolveMarkdownTemplateParser(templateParsers);
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _defaultOptions = defaultOptions;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PostSessionProcessingService>();
    }

    /// <summary>
    /// Uses AI to determine whether the conversation was semantically resolved,
    /// regardless of how the session was closed (natural farewell, inactivity, etc.).
    /// Returns <see langword="true"/> if the AI determines the user's query was addressed.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="prompts">The prompts.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<bool> EvaluateResolutionAsync(
        AIProfile profile,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(prompts);

        if (!prompts.Any(p => p.Role == ChatRole.User))
        {
            return false;
        }

        var chatClient = await GetChatClientAsync(profile);

        if (chatClient == null)
        {
            throw new InvalidOperationException($"Unable to create a chat client for resolution analysis on profile '{profile.ItemId}'.");
        }

        var transcript = await RenderTranscriptAsync(AITemplateIds.ResolutionAnalysisPrompt, prompts, cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(transcript))
        {
            return false;
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, await _aiTemplateService.RenderAsync(AITemplateIds.ResolutionAnalysis, cancellationToken: cancellationToken)),
            new(ChatRole.User, transcript),
        };

        var response = await chatClient.GetResponseAsync<ResolutionAnalysisResponse>(messages, new ChatOptions
        {
            Temperature = 0f,
        }.AddUsageTracking(session: AIInvocationScope.Current?.ChatSession), null, cancellationToken);

        return response.Result?.Resolved ?? false;
    }

    /// <summary>
    /// Evaluates the conversation against configured conversion goals using AI.
    /// Returns a list of goal results with scores, or null if evaluation fails.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="prompts">The prompts.</param>
    /// <param name="goals">The goals.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<List<ConversionGoalResult>> EvaluateConversionGoalsAsync(
        AIProfile profile,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        List<ConversionGoal> goals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(prompts);

        if (goals is null || goals.Count == 0 || !prompts.Any(p => p.Role == ChatRole.User))
        {
            return null;
        }

        var chatClient = await GetChatClientAsync(profile);

        if (chatClient == null)
        {
            throw new InvalidOperationException($"Unable to create a chat client for conversion goal evaluation on profile '{profile.ItemId}'.");
        }

        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["goals"] = goals.Select(g => new Dictionary<string, object>
            {
                ["Name"] = g.Name,
                ["Description"] = g.Description,
                ["MinScore"] = g.MinScore,
                ["MaxScore"] = g.MaxScore,
            }).ToList(),
            ["prompts"] = ProjectPrompts(prompts),
        };

        var userPrompt = await _aiTemplateService.RenderAsync(AITemplateIds.ConversionGoalEvaluationPrompt, arguments, cancellationToken);

        if (string.IsNullOrEmpty(userPrompt))
        {
            return null;
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, await _aiTemplateService.RenderAsync(AITemplateIds.ConversionGoalEvaluation, cancellationToken: cancellationToken)),
            new(ChatRole.User, userPrompt),
        };

        var response = await chatClient.GetResponseAsync<ConversionGoalEvaluationResponse>(messages, new ChatOptions
        {
            Temperature = 0f,
        }.AddUsageTracking(session: AIInvocationScope.Current?.ChatSession), null, cancellationToken);

        if (response.Result?.Goals is null || response.Result.Goals.Count == 0)
        {
            return null;
        }

        var results = new List<ConversionGoalResult>();

        foreach (var result in response.Result.Goals)
        {
            var goal = goals.FirstOrDefault(g =>
            string.Equals(g.Name, result.Name, StringComparison.OrdinalIgnoreCase));

            if (goal == null)
            {
                continue;
            }

            // Clamp score to valid range.
            var score = Math.Clamp(result.Score, goal.MinScore, goal.MaxScore);

            results.Add(new ConversionGoalResult
            {
                Name = goal.Name,
                Score = score,
                MaxScore = goal.MaxScore,
                Reasoning = result.Reasoning,
            });
        }

        return results;
    }

    /// <summary>
    /// Runs all configured post-session tasks against the closed session.
    /// Tasks that have already succeeded (tracked in <see cref="AIChatSession.PostSessionResults"/>)
    /// are excluded from processing. Returns the results keyed by task name, or null if processing
    /// is not enabled or all tasks have already succeeded.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="session">The session.</param>
    /// <param name="prompts">The prompts.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<Dictionary<string, PostSessionResult>> ProcessAsync(
        AIProfile profile,
        AIChatSession session,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(prompts);

        var settings = profile.GetOrCreateSettings<AIProfilePostSessionSettings>();

        if (!settings.EnablePostSessionProcessing || settings.PostSessionTasks.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session processing skipped for session '{SessionId}': Enabled={Enabled}, TaskCount={TaskCount}.",
                    session.SessionId,
                    settings.EnablePostSessionProcessing,
                    settings.PostSessionTasks.Count);
            }

            return null;
        }

        if (!prompts.Any(x => x.Role == ChatRole.User))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session processing skipped for session '{SessionId}' because there isn't any user prompts.",
                    session.SessionId);
            }

            return null;
        }

        // Filter out tasks that have already succeeded from a previous attempt.
        var tasksToProcess = settings.PostSessionTasks
            .Where(t => !session.PostSessionResults.TryGetValue(t.Name, out var existing)
                || existing.Status != PostSessionTaskResultStatus.Succeeded)
            .ToList();

        if (tasksToProcess.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session processing skipped for session '{SessionId}': all {TaskCount} task(s) have already succeeded.",
                    session.SessionId,
                    settings.PostSessionTasks.Count);
            }

            return null;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session processing for session '{SessionId}': {PendingCount}/{TotalCount} task(s) to process: [{TaskNames}].",
                session.SessionId,
                tasksToProcess.Count,
                settings.PostSessionTasks.Count,
                string.Join(", ", tasksToProcess.Select(t => t.Name)));
        }

        var chatClient = await GetChatClientAsync(profile);

        if (chatClient == null)
        {
            throw new InvalidOperationException(
                $"Unable to create a chat client for post-session processing on profile '{profile.ItemId}'.");
        }

        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["tasks"] = tasksToProcess.Select(t => new Dictionary<string, object>
            {
                ["Name"] = t.Name,
                ["Type"] = t.Type.ToString(),
                ["Instructions"] = t.Instructions,
                ["AllowMultipleValues"] = t.AllowMultipleValues,
                ["Options"] = t.Options?.Select(o => new Dictionary<string, object>
                {
                    ["Value"] = o.Value,
                    ["Description"] = o.Description,
                }).ToList(),
            }).ToList(),
            ["prompts"] = ProjectPrompts(prompts),
        };

        var prompt = await _aiTemplateService.RenderAsync(AITemplateIds.PostSessionAnalysisPrompt, arguments, cancellationToken);

        if (string.IsNullOrEmpty(prompt))
        {
            _logger.LogWarning(
                "Post-session processing aborted for session '{SessionId}': rendered user prompt is empty. Template='{TemplateId}'.",
                session.SessionId,
                AITemplateIds.PostSessionAnalysisPrompt);

            return null;
        }

        var systemPrompt = await _aiTemplateService.RenderAsync(AITemplateIds.PostSessionAnalysis, cancellationToken: cancellationToken);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session prompt details for session '{SessionId}': SystemPromptPreview='{SystemPromptPreview}', UserPromptPreview='{UserPromptPreview}', Tasks=[{TaskNames}].",
                session.SessionId,
                CreateResponseLogPreview(systemPrompt),
                CreateResponseLogPreview(prompt),
                string.Join(", ", tasksToProcess.Select(t => t.Name)));
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, prompt),
        };

        var toolNames = GetConfiguredToolNames(settings.ToolNames, tasksToProcess);

        var tools = await ResolveToolsAsync(session.SessionId, toolNames);

        // When tools are configured (e.g., sendEmail), use non-generic GetResponseAsync
        // to allow tool execution. The generic version uses structured output which
        // conflicts with tool calls - the model may fail to call tools when forced
        // to produce structured JSON output.
        if (tools is not null && tools.Count > 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session processing for session '{SessionId}' using tools path with {ToolCount} tool(s): [{ToolNames}].",
                    session.SessionId,
                    tools.Count,
                    string.Join(", ", tools.Select(t => t.Name)));
            }

            return await ProcessWithToolsAsync(session, chatClient, messages, tools, tasksToProcess, cancellationToken);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session processing for session '{SessionId}' using structured output path (no tools configured or resolved).",
                session.SessionId);
        }

        return await ProcessStructuredAsync(
            session,
            chatClient,
            messages,
            tasksToProcess,
            "structured output path",
            false,
            cancellationToken);
    }

    private async Task<Dictionary<string, PostSessionResult>> ProcessWithToolsAsync(
        AIChatSession session,
        IChatClient chatClient,
        List<ChatMessage> messages,
        IList<AITool> tools,
        List<PostSessionTask> tasks,
        CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogResponseMessages(session.SessionId, "tools-request", messages);
        }

        // Wrap the raw client with FunctionInvokingChatClient so that tool_call
        // messages returned by the model are actually executed (e.g., sendEmail).
        var client = chatClient
            .AsBuilder()
            .UseFunctionInvocation(_loggerFactory, c =>
            {
                c.MaximumIterationsPerRequest = _defaultOptions.MaximumIterationsPerRequest;
            })
            .Build(_serviceProvider);

        var response = await client.GetResponseAsync(messages, new ChatOptions
        {
            Temperature = 0f,
            Tools = tools,
        }.AddUsageTracking(session: session), cancellationToken);

        var toolCallCount = response.Messages?
            .SelectMany(m => m.Contents?.OfType<FunctionCallContent>() ?? [])
            .Count() ?? 0;

        var toolResultCount = response.Messages?
            .SelectMany(m => m.Contents?.OfType<FunctionResultContent>() ?? [])
            .Count() ?? 0;

        // Log tool invocation details from the response messages.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session tools response for session '{SessionId}': MessageCount={MessageCount}, ToolCalls={ToolCallCount}, ToolResults={ToolResultCount}.",
                session.SessionId,
                response.Messages?.Count ?? 0,
                toolCallCount,
                toolResultCount);

            LogResponseMessages(session.SessionId, "tools", response.Messages);
        }

        // Extract the final assistant message text, ignoring intermediate tool
        // call and tool result messages. After FunctionInvokingChatClient resolves
        // all tool calls, the model produces a final assistant message with the JSON
        // task results - that is the only message we care about.
        var responseText = GetLastAssistantMessageText(response.Messages);

        // Always log the raw response text for troubleshooting.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session tools raw response for session '{SessionId}': '{ResponseText}'.",
                session.SessionId,
                CreateResponseLogPreview(responseText));
        }

        if (!string.IsNullOrEmpty(responseText))
        {
            var result = TryParsePostSessionResponse(session.SessionId, responseText);

            if (result?.Tasks != null)
            {
                if (result.Tasks.Count == 0)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Post-session tools response for session '{SessionId}' returned an empty tasks array. Attempting structured recovery.",
                            session.SessionId);
                    }
                }
                else
                {
                    var appliedResults = ApplyResults(tasks, result.Tasks);

                    if (appliedResults.Count > 0)
                    {
                        return appliedResults;
                    }

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Post-session tools response for session '{SessionId}' returned invalid task entries. Attempting structured recovery.",
                            session.SessionId);
                    }
                }
            }
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session tools response for session '{SessionId}' has no final text content. Attempting structured recovery from tool messages.",
                session.SessionId);
        }

        var recoveredResults = await TryRecoverStructuredToolsResponseAsync(
            session.SessionId,
            chatClient,
            messages,
            response.Messages,
            tasks,
            cancellationToken);

        if (recoveredResults is not null && recoveredResults.Count > 0)
        {
            var hasUsableRecoveredResults = recoveredResults.Values.Any(result =>
                result.Status == PostSessionTaskResultStatus.Succeeded
                && !string.IsNullOrWhiteSpace(result.Value));

            if (hasUsableRecoveredResults)
            {
                return recoveredResults;
            }

            if (toolCallCount == 0 && toolResultCount == 0)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Post-session tool processing for session '{SessionId}' produced no tool calls/results and no usable structured recovery output. Retrying without tools.",
                        session.SessionId);
                }

                return await ProcessStructuredAsync(
                    session,
                    chatClient,
                    messages,
                    tasks,
                    "no-tools fallback after tool path produced no usable output",
                    true,
                    cancellationToken);
            }

            return recoveredResults;
        }

        if (toolCallCount == 0 && toolResultCount == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session tool processing for session '{SessionId}' produced no tool calls/results and no recoverable structured output. Retrying without tools.",
                    session.SessionId);
            }

            return await ProcessStructuredAsync(
                session,
                chatClient,
                messages,
                tasks,
                "no-tools fallback after empty tool path result",
                true,
                cancellationToken);
        }

        return CreateFailedResults(session.SessionId, tasks, responseText);
    }

    private async Task<Dictionary<string, PostSessionResult>> ProcessStructuredAsync(
        AIChatSession session,
        IChatClient chatClient,
        List<ChatMessage> messages,
        List<PostSessionTask> tasksToProcess,
        string reason,
        bool failWhenStructuredResultMissing,
        CancellationToken cancellationToken)
    {
        var response = await chatClient.GetResponseAsync<PostSessionProcessingResponse>(messages, new ChatOptions
        {
            Temperature = 0f,
        }.AddUsageTracking(session: session), null, cancellationToken);

        var responseText = GetLastAssistantMessageText(response.Messages);
        PostSessionProcessingResponse result = null;

        try
        {
            result = response.Result;
        }
        catch (InvalidOperationException)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session structured output for session '{SessionId}' ({Reason}) did not contain JSON in the typed result path.",
                    session.SessionId,
                    reason);
            }
        }
        catch (JsonException)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session structured output for session '{SessionId}' ({Reason}) returned invalid JSON in the typed result path.",
                    session.SessionId,
                    reason);
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session structured raw response for session '{SessionId}' ({Reason}): '{ResponseText}'.",
                session.SessionId,
                reason,
                CreateResponseLogPreview(responseText));

            LogResponseMessages(session.SessionId, $"structured({reason})", response.Messages);
        }

        if (result?.Tasks == null)
        {
            var parsedResult = TryParsePostSessionResponse(session.SessionId, responseText);

            if (parsedResult?.Tasks != null)
            {
                result = parsedResult;
            }
        }

        if (result?.Tasks == null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session structured output for session '{SessionId}' ({Reason}) returned no tasks.",
                    session.SessionId,
                    reason);
            }

            if (failWhenStructuredResultMissing)
            {
                return CreateFailedResults(session.SessionId, tasksToProcess, responseText);
            }

            return null;
        }

        if (result.Tasks.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session structured output for session '{SessionId}' ({Reason}) returned an empty tasks array.",
                    session.SessionId,
                    reason);
            }

            return CreateEmptyStructuredResults(session.SessionId, tasksToProcess, responseText);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session structured output for session '{SessionId}' ({Reason}) returned {TaskCount} raw task result(s): [{TaskNames}].",
                session.SessionId,
                reason,
                result.Tasks.Count,
                CreateTaskResultSummary(result.Tasks));
        }

        var appliedResults = ApplyResults(tasksToProcess, result.Tasks);

        if (appliedResults.Count > 0)
        {
            return appliedResults;
        }

        return CreateInvalidStructuredResults(session.SessionId, tasksToProcess, result.Tasks, responseText);
    }

    /// <summary>
    /// Attempts to parse the AI response text as a <see cref="PostSessionProcessingResponse"/>
    /// using progressively lenient strategies:
    /// 1. Direct JSON deserialization.
    /// 2. Extract JSON from markdown code fences (```json ... ```).
    /// 3. Extract the first JSON object from surrounding text.
    /// </summary>
    private PostSessionProcessingResponse TryParsePostSessionResponse(string sessionId, string responseText)
    {
        // Strategy 1: Direct JSON deserialization.
        if (TryDeserializePostSessionResponse(responseText, out var directResult))
        {
            return directResult;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session response for session '{SessionId}' is not valid JSON. Trying fallback extraction.",
                sessionId);
        }

        // Strategy 2: Extract JSON from markdown code fences.
        var jsonBlock = JsonExtractor.ExtractFromCodeFence(responseText);

        if (TryDeserializePostSessionResponse(jsonBlock, out var fencedResult))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session response for session '{SessionId}' parsed successfully from code fence.",
                    sessionId);
            }

            return fencedResult;
        }

        // Strategy 3: Extract the first JSON object from surrounding text.
        var jsonObject = JsonExtractor.ExtractJsonObject(responseText);

        if (jsonObject != null &&
            jsonObject != responseText &&
            TryDeserializePostSessionResponse(jsonObject, out var objectResult))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session response for session '{SessionId}' parsed successfully from embedded JSON object.",
                    sessionId);
            }

            return objectResult;
        }

        // Strategy 4: Normalize markdown/front matter, then retry the same lenient extraction.
        var normalizedBody = _markdownTemplateParser.Parse(responseText).Body?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedBody) &&
            !string.Equals(normalizedBody, responseText.Trim(), StringComparison.Ordinal))
        {
            if (TryDeserializePostSessionResponse(normalizedBody, out var normalizedResult))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Post-session response for session '{SessionId}' parsed successfully from normalized markdown body.",
                        sessionId);
                }

                return normalizedResult;
            }

            var normalizedJsonBlock = JsonExtractor.ExtractFromCodeFence(normalizedBody);

            if (TryDeserializePostSessionResponse(normalizedJsonBlock, out var normalizedFencedResult))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Post-session response for session '{SessionId}' parsed successfully from normalized markdown code fence.",
                        sessionId);
                }

                return normalizedFencedResult;
            }

            var normalizedJsonObject = JsonExtractor.ExtractJsonObject(normalizedBody);

            if (normalizedJsonObject != null &&
                normalizedJsonObject != normalizedBody &&
                TryDeserializePostSessionResponse(normalizedJsonObject, out var normalizedObjectResult))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Post-session response for session '{SessionId}' parsed successfully from normalized markdown embedded JSON object.",
                        sessionId);
                }

                return normalizedObjectResult;
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session response for session '{SessionId}' could not be parsed as structured JSON after all extraction attempts.",
                sessionId);
        }

        return null;
    }

    private async Task<Dictionary<string, PostSessionResult>> TryRecoverStructuredToolsResponseAsync(
        string sessionId,
        IChatClient chatClient,
        List<ChatMessage> requestMessages,
        IList<ChatMessage> responseMessages,
        List<PostSessionTask> tasks,
        CancellationToken cancellationToken)
    {
        var followUpMessages = new List<ChatMessage>(requestMessages);

        var trailingAssistantText = GetLastAssistantMessage(responseMessages);

        if (responseMessages is not null)
        {
            foreach (var responseMessage in responseMessages)
            {
                if (ReferenceEquals(responseMessage, trailingAssistantText))
                {
                    continue;
                }

                followUpMessages.Add(responseMessage);
            }
        }

        // Add an explicit user message to guide the model into producing
        // structured JSON after tool calls have been executed.
        var taskNamesList = string.Join(", ", tasks.Select(t => t.Name));
        followUpMessages.Add(new ChatMessage(ChatRole.User,
            $"""
            The tool calls above have been completed. Now return ONLY the required JSON output with the "tasks" array. 
            Each task must have a "name" and "value" field. The tasks you must include are: {taskNamesList}.
            Do NOT wrap in markdown. Return raw JSON only.
            """));

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Attempting structured recovery for post-session tool response on session '{SessionId}' using the original post-session analysis context. TaskCount={TaskCount}, TotalMessages={MessageCount}.",
                sessionId,
                tasks.Count,
                followUpMessages.Count);
        }

        var response = await chatClient.GetResponseAsync<PostSessionProcessingResponse>(followUpMessages, new ChatOptions
        {
            Temperature = 0f,
        }, null, cancellationToken);

        var recoveryResponseText = GetLastAssistantMessageText(response.Messages);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Post-session structured recovery raw response for session '{SessionId}': '{ResponseText}'.",
                sessionId,
                CreateResponseLogPreview(recoveryResponseText));

            LogResponseMessages(sessionId, "structured-recovery", response.Messages);
        }

        PostSessionProcessingResponse result = null;

        try
        {
            result = response.Result;
        }
        catch (InvalidOperationException)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Structured recovery for post-session tool response on session '{SessionId}' did not return JSON content.",
                    sessionId);
            }
        }
        catch (JsonException)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Structured recovery for post-session tool response on session '{SessionId}' returned invalid JSON content.",
                    sessionId);
            }
        }

        if (result?.Tasks != null)
        {
            if (result.Tasks.Count == 0)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Structured recovery for post-session tool response on session '{SessionId}' returned an empty tasks array.",
                        sessionId);
                }

                return CreateEmptyStructuredResults(sessionId, tasks, recoveryResponseText);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Structured recovery for post-session tool response on session '{SessionId}' returned {TaskCount} raw task result(s).",
                    sessionId,
                    result.Tasks.Count);
            }

            var appliedResults = ApplyResults(tasks, result.Tasks);

            if (appliedResults.Count > 0)
            {
                return appliedResults;
            }

            return CreateInvalidStructuredResults(sessionId, tasks, result.Tasks, recoveryResponseText);
        }

        var recoveredFromText = TryParsePostSessionResponse(sessionId, recoveryResponseText);

        if (recoveredFromText?.Tasks is null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Structured recovery for post-session tool response on session '{SessionId}' returned no task results.",
                    sessionId);
            }

            return null;
        }

        if (recoveredFromText.Tasks.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Structured recovery for post-session tool response on session '{SessionId}' parsed an empty tasks array from assistant text.",
                    sessionId);
            }

            return CreateEmptyStructuredResults(sessionId, tasks, recoveryResponseText);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Structured recovery for post-session tool response on session '{SessionId}' parsed {TaskCount} raw task result(s) from assistant text.",
                sessionId,
                recoveredFromText.Tasks.Count);
        }

        var recoveredAppliedResults = ApplyResults(tasks, recoveredFromText.Tasks);

        if (recoveredAppliedResults.Count > 0)
        {
            return recoveredAppliedResults;
        }

        return CreateInvalidStructuredResults(sessionId, tasks, recoveredFromText.Tasks, recoveryResponseText);
    }

    private static bool TryDeserializePostSessionResponse(
        string responseText,
        out PostSessionProcessingResponse response)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            response = null;
            return false;
        }

        try
        {
            response = JsonSerializer.Deserialize<PostSessionProcessingResponse>(
                responseText,
                JSOptions.CaseInsensitive);

            return response is not null;
        }
        catch (JsonException)
        {
            response = null;
            return false;
        }
    }

    private static ChatMessage GetLastAssistantMessage(IList<ChatMessage> messages)
    {
        if (messages is null)
        {
            return null;
        }

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatRole.Assistant)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(GetMessageText(messages[i])))
            {
                return messages[i];
            }
        }

        return null;
    }

    private static string GetLastAssistantMessageText(IList<ChatMessage> messages)
    {
        return GetMessageText(GetLastAssistantMessage(messages))?.Trim();
    }

    private static string GetMessageText(ChatMessage message)
    {
        if (message == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            return message.Text;
        }

        var contentText = string.Concat(message.Contents?.OfType<TextContent>().Select(content => content.Text) ?? []);

        return string.IsNullOrWhiteSpace(contentText)
            ? null
            : contentText;
    }

    private static ITemplateParser ResolveMarkdownTemplateParser(IEnumerable<ITemplateParser> templateParsers)
    {
        ArgumentNullException.ThrowIfNull(templateParsers);

        return templateParsers.FirstOrDefault(parser =>
            parser.SupportedExtensions.Any(extension => string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException("No markdown template parser is registered for post-session response recovery.");
    }

    private Dictionary<string, PostSessionResult> CreateFailedResults(
        string sessionId,
        List<PostSessionTask> tasks,
        string responseText)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var errorMessage = string.IsNullOrWhiteSpace(responseText)

        ? "Tool execution completed, but the AI response did not contain the required structured JSON results."
        : "The AI response could not be parsed as structured JSON after tool execution.";

        _logger.LogWarning(
            "Post-session tool response for session '{SessionId}' failed structured parsing. Marking {TaskCount} task(s) as failed. ResponseLength={ResponseLength}.",
            sessionId,
            tasks.Count,
            responseText?.Length ?? 0);

        return tasks.ToDictionary(
                    task => task.Name,
                    task => new PostSessionResult
                    {
                        Name = task.Name,
                        Status = PostSessionTaskResultStatus.Failed,
                        ErrorMessage = errorMessage,
                        ProcessedAtUtc = now,
                    },
                    StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, PostSessionResult> CreateInvalidStructuredResults(
        string sessionId,
        List<PostSessionTask> tasks,
        List<PostSessionTaskResult> results,
        string responseText)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        const string errorMessage = "The AI returned structured task results, but none contained a usable task name and value that matched the configured post-session tasks.";

        _logger.LogWarning(
            "Post-session structured results for session '{SessionId}' were invalid. ReturnedResults=[{ReturnedResults}]. ResponsePreview='{ResponsePreview}'.",
            sessionId,
            CreateTaskResultSummary(results),
            CreateResponseLogPreview(responseText));

        return tasks.ToDictionary(
            task => task.Name,
            task => new PostSessionResult
            {
                Name = task.Name,
                Status = PostSessionTaskResultStatus.Failed,
                ErrorMessage = errorMessage,
                ProcessedAtUtc = now,
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, PostSessionResult> CreateEmptyStructuredResults(
        string sessionId,
        List<PostSessionTask> tasks,
        string responseText)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        const string errorMessage = "The AI returned structured JSON, but the tasks array was empty. Each configured post-session task must return a result, even when no tool call is needed.";

        _logger.LogWarning(
            "Post-session structured results for session '{SessionId}' were empty. ConfiguredTaskCount={ConfiguredTaskCount}. ResponsePreview='{ResponsePreview}'.",
            sessionId,
            tasks.Count,
            CreateResponseLogPreview(responseText));

        return tasks.ToDictionary(
            task => task.Name,
            task => new PostSessionResult
            {
                Name = task.Name,
                Status = PostSessionTaskResultStatus.Failed,
                ErrorMessage = errorMessage,
                ProcessedAtUtc = now,
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static string CreateResponseLogPreview(string responseText)
    {
        if (string.IsNullOrEmpty(responseText))
        {
            return "(empty)";
        }

        var normalized = responseText

            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return normalized.Length > 2000 ? normalized[..2000] + "..." : normalized;
    }

    private static string CreateTaskResultSummary(IEnumerable<PostSessionTaskResult> results)
    {
        if (results == null)
        {
            return "(none)";
        }

        var summaries = results.Select(result =>
            $"Name='{result?.Name ?? "(null)"}', HasValue={!string.IsNullOrWhiteSpace(result?.Value)}");

        return string.Join("; ", summaries);
    }

    private static string[] GetConfiguredToolNames(
        IEnumerable<string> profileToolNames,
        IEnumerable<PostSessionTask> tasks)
    {
        var configuredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (profileToolNames != null)
        {
            foreach (var toolName in profileToolNames)
            {
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    configuredNames.Add(toolName);
                }
            }
        }

        if (tasks != null)
        {
            foreach (var toolName in tasks
                         .Where(task => task?.ToolNames != null)
                         .SelectMany(task => task.ToolNames))
            {
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    configuredNames.Add(toolName);
                }
            }
        }

        return configuredNames.Count > 0 ? [.. configuredNames] : [];
    }

    private Dictionary<string, PostSessionResult> ApplyResults(
        List<PostSessionTask> tasks,
        List<PostSessionTaskResult> results)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var applied = new Dictionary<string, PostSessionResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Name) || string.IsNullOrWhiteSpace(result.Value))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Post-session task result skipped: Name='{Name}', HasValue={HasValue}.",
                        result.Name ?? "(null)",
                        !string.IsNullOrWhiteSpace(result.Value));
                }

                continue;
            }

            var task = tasks.FirstOrDefault(t =>
            string.Equals(t.Name, result.Name, StringComparison.OrdinalIgnoreCase));

            if (task == null)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Post-session task result skipped: no matching task definition for '{Name}'.",
                        result.Name);
                }

                continue;
            }

            // For PredefinedOptions type, validate the value(s) against the configured options.
            if (task.Type == PostSessionTaskType.PredefinedOptions && task.Options.Count > 0)
            {
                var optionValues = task.Options.Select(o => o.Value).ToList();

                if (task.AllowMultipleValues)
                {
                    // Validate each comma-separated value.

                    var selectedValues = result.Value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    var validValues = selectedValues
                        .Where(v => optionValues.Any(o => string.Equals(o, v, StringComparison.OrdinalIgnoreCase)))

                        .Select(v => optionValues.First(o => string.Equals(o, v, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (validValues.Count == 0)
                    {
                        continue;
                    }

                    result.Value = string.Join(", ", validValues);
                }
                else
                {
                    var matchedOption = optionValues.FirstOrDefault(o =>
                    string.Equals(o, result.Value, StringComparison.OrdinalIgnoreCase));

                    if (matchedOption == null)
                    {
                        continue;
                    }

                    result.Value = matchedOption;
                }
            }

            applied[task.Name] = new PostSessionResult
            {
                Name = task.Name,
                Value = result.Value,
                Status = PostSessionTaskResultStatus.Succeeded,
                ProcessedAtUtc = now,
            };

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Post-session task '{TaskName}' applied successfully (ValueLength={ValueLength}).",
                    task.Name,
                    result.Value.Length);
            }
        }

        return applied;
    }

    private async Task<IChatClient> GetChatClientAsync(AIProfile profile)
    {
        if (_deploymentManager != null)
        {
            var deployment = await _deploymentManager.ResolveUtilityOrDefaultAsync(
                utilityDeploymentName: profile.UtilityDeploymentName,
                chatDeploymentName: profile.ChatDeploymentName);

            if (deployment != null && !string.IsNullOrEmpty(deployment.ConnectionName) && !string.IsNullOrEmpty(deployment.ModelName))
            {
                return await _clientFactory.CreateChatClientAsync(deployment);
            }
        }

        return null;
    }

    private async Task<IList<AITool>> ResolveToolsAsync(string sessionId, string[] toolNames)
    {
        if (toolNames is null || toolNames.Length == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "No tool names configured for post-session processing of session '{SessionId}'.",
                    sessionId);
            }

            return null;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Resolving {ToolCount} tool(s) for post-session processing of session '{SessionId}': [{ToolNames}].",
                toolNames.Length,
                sessionId,
                string.Join(", ", toolNames));
        }

        var tools = new List<AITool>();

        foreach (var name in toolNames)
        {
            var tool = await _toolsService.GetByNameAsync(name);

            if (tool is not null)
            {
                tools.Add(tool);
            }
            else
            {
                _logger.LogWarning(
                    "Post-session tool '{ToolName}' could not be resolved for session '{SessionId}'. Ensure the tool is registered and its feature is enabled.",
                    name,
                    sessionId);
            }
        }

        return tools.Count > 0 ? tools : null;
    }

    private async Task<string> RenderTranscriptAsync(
        string templateId,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        Dictionary<string, object> extraArguments = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompts"] = ProjectPrompts(prompts),
        };

        if (extraArguments is not null)
        {
            foreach (var kvp in extraArguments)
            {
                arguments[kvp.Key] = kvp.Value;
            }
        }

        return await _aiTemplateService.RenderAsync(templateId, arguments, cancellationToken);
    }

    private static List<object> ProjectPrompts(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        return prompts
            .Where(p => !p.IsGeneratedPrompt)
            .Select(p => (object)new Dictionary<string, object>
            {
                ["Role"] = p.Role == ChatRole.User ? "User" : "Assistant",
                ["Content"] = p.Content?.Trim(),
            })
            .ToList();
    }

    private void LogResponseMessages(string sessionId, string phase, IList<ChatMessage> messages)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        if (messages is null || messages.Count == 0)
        {
            _logger.LogDebug(
                "Post-session {Phase} messages for session '{SessionId}': (no messages).",
                phase,
                sessionId);

            return;
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var textContent = GetMessageText(message);
            var functionCalls = message.Contents?.OfType<FunctionCallContent>().ToList() ?? [];
            var functionResults = message.Contents?.OfType<FunctionResultContent>().ToList() ?? [];

            if (functionCalls.Count > 0)
            {
                var callSummaries = string.Join("; ", functionCalls.Select(fc =>
                    $"{fc.Name}({CreateResponseLogPreview(fc.Arguments?.ToString())})"));

                _logger.LogDebug(
                    "Post-session {Phase} message[{Index}] for session '{SessionId}': Role={Role}, ToolCalls=[{ToolCalls}].",
                    phase,
                    i,
                    sessionId,
                    message.Role,
                    callSummaries);
            }
            else if (functionResults.Count > 0)
            {
                var resultSummaries = string.Join("; ", functionResults.Select(fr =>
                    $"{fr.CallId}={CreateResponseLogPreview(fr.Result?.ToString())}"));

                _logger.LogDebug(
                    "Post-session {Phase} message[{Index}] for session '{SessionId}': Role={Role}, ToolResults=[{ToolResults}].",
                    phase,
                    i,
                    sessionId,
                    message.Role,
                    resultSummaries);
            }
            else
            {
                _logger.LogDebug(
                    "Post-session {Phase} message[{Index}] for session '{SessionId}': Role={Role}, Text='{Text}'.",
                    phase,
                    i,
                    sessionId,
                    message.Role,
                    CreateResponseLogPreview(textContent));
            }
        }
    }

    private sealed class PostSessionProcessingResponse
    {
        public List<PostSessionTaskResult> Tasks { get; set; } = [];
    }

    private sealed class PostSessionTaskResult
    {
        public string Name { get; set; }

        public string Value { get; set; }
    }

    private sealed class ResolutionAnalysisResponse
    {
        public bool Resolved { get; set; }
    }

    private sealed class ConversionGoalEvaluationResponse
    {
        public List<ConversionGoalEvaluationResult> Goals { get; set; } = [];
    }

    private sealed class ConversionGoalEvaluationResult
    {
        public string Name { get; set; }

        public int Score { get; set; }

        public string Reasoning { get; set; }
    }
}
