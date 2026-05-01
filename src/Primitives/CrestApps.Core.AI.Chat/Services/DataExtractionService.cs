using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Support.Json;
using CrestApps.Core.Templates.Parsing;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Represents the data Extraction Service.
/// </summary>
public sealed class DataExtractionService
{
    private readonly IAIClientFactory _clientFactory;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly ITemplateService _aiTemplateService;
    private readonly ITemplateParser _markdownTemplateParser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DataExtractionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataExtractionService"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="aiTemplateService">The ai template service.</param>
    /// <param name="templateParsers">The registered template parsers.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="deploymentManager">The deployment manager.</param>
    public DataExtractionService(
        IAIClientFactory clientFactory,
        ITemplateService aiTemplateService,
        IEnumerable<ITemplateParser> templateParsers,
        TimeProvider timeProvider,
        ILogger<DataExtractionService> logger,
        IAIDeploymentManager deploymentManager = null)
    {
        _clientFactory = clientFactory;
        _deploymentManager = deploymentManager;
        _aiTemplateService = aiTemplateService;
        _markdownTemplateParser = ResolveMarkdownTemplateParser(templateParsers);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full extraction pipeline: checks whether extraction should run,
    /// determines which fields to extract, calls the AI model, and applies results
    /// to the session. Returns the change set (may be empty).
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="session">The session.</param>
    /// <param name="prompts">The prompts.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ExtractionChangeSet> ProcessAsync(
        AIProfile profile,
        AIChatSession session,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(prompts);

        var settings = profile.GetOrCreateSettings<AIProfileDataExtractionSettings>();
        var promptCount = prompts.Count(p => p.Role == ChatRole.User);

        if (!ShouldExtract(settings, promptCount))
        {
            return null;
        }

        var fieldsToExtract = GetFieldsToExtract(settings, session);

        if (fieldsToExtract.Count == 0)
        {
            return null;
        }

        var (results, sessionEnded) = await ExtractAsync(profile, session, prompts, fieldsToExtract, cancellationToken);

        var changeSet = ApplyExtraction(session, settings, results);
        changeSet.SessionEnded = sessionEnded;

        if (changeSet.NewFields.Count > 0)
        {
            changeSet.AllFieldsCollected = settings.DataExtractionEntries
                .All(entry => session.ExtractedData.TryGetValue(entry.Name, out var state) && state.Values.Count > 0);
        }

        return changeSet;
    }

    private static bool ShouldExtract(AIProfileDataExtractionSettings settings, int promptCount)
    {
        if (!settings.EnableDataExtraction)
        {
            return false;
        }

        if (settings.DataExtractionEntries.Count == 0)
        {
            return false;
        }

        if (settings.ExtractionCheckInterval < 1)
        {
            return false;
        }

        return promptCount % settings.ExtractionCheckInterval == 0;
    }

    private static List<DataExtractionEntry> GetFieldsToExtract(AIProfileDataExtractionSettings settings, AIChatSession session)
    {
        var fieldsToExtract = new List<DataExtractionEntry>();

        foreach (var entry in settings.DataExtractionEntries)
        {
            if (entry.AllowMultipleValues)
            {
                fieldsToExtract.Add(entry);
                continue;
            }

            if (!session.ExtractedData.TryGetValue(entry.Name, out var state) || state.Values.Count == 0)
            {
                fieldsToExtract.Add(entry);
                continue;
            }

            if (entry.IsUpdatable)
            {
                fieldsToExtract.Add(entry);
            }
        }

        return fieldsToExtract;
    }

    private async Task<(List<ExtractionResult> Results, bool SessionEnded)> ExtractAsync(
        AIProfile profile,
        AIChatSession session,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        List<DataExtractionEntry> fieldsToExtract,
        CancellationToken cancellationToken = default)
    {
        if (fieldsToExtract.Count == 0)
        {
            return ([], false);
        }

        var prompt = await BuildExtractionPromptAsync(fieldsToExtract, session, prompts, cancellationToken);

        if (string.IsNullOrEmpty(prompt))
        {
            return ([], false);
        }

        try
        {
            var chatClient = await GetChatClientAsync(profile);

            if (chatClient == null)
            {
                _logger.LogWarning("Unable to create a chat client for data extraction on profile '{ProfileId}'.", profile.ItemId);

                return ([], false);
            }

            var systemPrompt = await _aiTemplateService.RenderAsync(AITemplateIds.DataExtraction, cancellationToken: cancellationToken);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, prompt),
            };

            var response = await chatClient.GetResponseAsync<ExtractionResponse>(messages, new ChatOptions
            {
                Temperature = 0f,
                MaxOutputTokens = 1024,
            }.AddUsageTracking(session: session), null, cancellationToken);

            var responseText = GetLastAssistantMessageText(response.Messages);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Data extraction raw response for session '{SessionId}': '{ResponseText}'.",
                    session.SessionId,
                    CreateResponseLogPreview(responseText));
            }

            ExtractionResponse result = null;

            try
            {
                result = response.Result;
            }
            catch (InvalidOperationException)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Data extraction response for session '{SessionId}' did not return JSON content.",
                        session.SessionId);
                }
            }
            catch (JsonException)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Data extraction response for session '{SessionId}' returned invalid JSON content.",
                        session.SessionId);
                }
            }

            if (result?.Fields != null && result.Fields.Count > 0)
            {
                return (result.Fields, result.SessionEnded);
            }

            var recovered = TryParseExtractionResponse(session.SessionId, responseText);

            if (recovered?.Fields == null || recovered.Fields.Count == 0)
            {
                return ([], false);
            }

            return (recovered.Fields, recovered.SessionEnded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data extraction failed for session '{SessionId}'.", session.SessionId);

            return ([], false);
        }
    }

    private ExtractionChangeSet ApplyExtraction(
        AIChatSession session,
        AIProfileDataExtractionSettings settings,
        List<ExtractionResult> results)
    {
        var changeSet = new ExtractionChangeSet();

        foreach (var result in results)
        {
            if (result.Values == null || result.Values.Count == 0)
            {
                continue;
            }

            var entry = settings.DataExtractionEntries.FirstOrDefault(e =>
                string.Equals(e.Name, result.Name, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                continue;
            }

            if (!session.ExtractedData.TryGetValue(entry.Name, out var state))
            {
                state = new ExtractedFieldState();
                session.ExtractedData[entry.Name] = state;
            }

            if (entry.AllowMultipleValues)
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;

                foreach (var value in result.Values)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (!state.Values.Contains(value, StringComparer.OrdinalIgnoreCase))
                    {
                        state.Values.Add(value);
                        state.LastExtractedUtc = now;
                        changeSet.NewFields.Add(new ExtractedFieldChange(entry.Name, value, entry.AllowMultipleValues));
                    }
                }
            }
            else
            {
                var value = result.Values[0];

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var now = _timeProvider.GetUtcNow().UtcDateTime;

                if (state.Values.Count == 0)
                {
                    state.Values.Add(value);
                    state.LastExtractedUtc = now;
                    changeSet.NewFields.Add(new ExtractedFieldChange(entry.Name, value, false));
                }
                else if (entry.IsUpdatable)
                {
                    state.Values[0] = value;
                    state.LastExtractedUtc = now;
                    changeSet.NewFields.Add(new ExtractedFieldChange(entry.Name, value, false));
                }
            }
        }

        return changeSet;
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

    private async Task<string> BuildExtractionPromptAsync(
        List<DataExtractionEntry> fieldsToExtract,
        AIChatSession session,
        IReadOnlyList<AIChatSessionPrompt> prompts,
        CancellationToken cancellationToken)
    {
        string lastUserMessage = null;
        string lastAssistantMessage = null;

        for (var i = prompts.Count - 1; i >= 0; i--)
        {
            if (lastUserMessage is null && prompts[i].Role == ChatRole.User)
            {
                lastUserMessage = prompts[i].Content?.Trim();

                for (var j = i - 1; j >= 0; j--)
                {
                    if (prompts[j].Role == ChatRole.Assistant)
                    {
                        lastAssistantMessage = prompts[j].Content?.Trim();
                        break;
                    }
                }

                break;
            }
        }

        if (string.IsNullOrEmpty(lastUserMessage))
        {
            return null;
        }

        var arguments = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["fields"] = fieldsToExtract.Select(field => new
            {
                field.Name,
                field.Description,
                field.AllowMultipleValues,
                field.IsUpdatable,
            }).ToList(),
            ["currentState"] = session.ExtractedData?
                .Where(entry => entry.Value?.Values.Count > 0)
                .Select(entry => new
                {
                    Name = entry.Key,
                    Values = entry.Value.Values,
                })
                .ToList() ?? [],
            ["lastUserMessage"] = lastUserMessage,
        };

        if (!string.IsNullOrEmpty(lastAssistantMessage))
        {
            arguments["lastAssistantMessage"] = lastAssistantMessage;
        }

        return await _aiTemplateService.RenderAsync(AITemplateIds.DataExtractionPrompt, arguments, cancellationToken);
    }

    private ExtractionResponse TryParseExtractionResponse(string sessionId, string responseText)
    {
        if (TryDeserializeExtractionResponse(responseText, out var directResult))
        {
            return directResult;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Data extraction response for session '{SessionId}' is not valid JSON. Trying fallback extraction.",
                sessionId);
        }

        var jsonBlock = JsonExtractor.ExtractFromCodeFence(responseText);

        if (TryDeserializeExtractionResponse(jsonBlock, out var fencedResult))
        {
            return fencedResult;
        }

        var jsonObject = JsonExtractor.ExtractJsonObject(responseText);

        if (jsonObject != null &&
            jsonObject != responseText &&
            TryDeserializeExtractionResponse(jsonObject, out var objectResult))
        {
            return objectResult;
        }

        var normalizedBody = _markdownTemplateParser.Parse(responseText).Body?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedBody) &&
            !string.Equals(normalizedBody, responseText?.Trim(), StringComparison.Ordinal))
        {
            if (TryDeserializeExtractionResponse(normalizedBody, out var normalizedResult))
            {
                return normalizedResult;
            }

            var normalizedJsonBlock = JsonExtractor.ExtractFromCodeFence(normalizedBody);

            if (TryDeserializeExtractionResponse(normalizedJsonBlock, out var normalizedFencedResult))
            {
                return normalizedFencedResult;
            }

            var normalizedJsonObject = JsonExtractor.ExtractJsonObject(normalizedBody);

            if (normalizedJsonObject != null &&
                normalizedJsonObject != normalizedBody &&
                TryDeserializeExtractionResponse(normalizedJsonObject, out var normalizedObjectResult))
            {
                return normalizedObjectResult;
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Data extraction response for session '{SessionId}' could not be parsed as structured JSON after all extraction attempts.",
                sessionId);
        }

        return null;
    }

    private static bool TryDeserializeExtractionResponse(
        string responseText,
        out ExtractionResponse response)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            response = null;
            return false;
        }

        try
        {
            response = JsonSerializer.Deserialize<ExtractionResponse>(responseText, JSOptions.CaseInsensitive);

            return response is not null;
        }
        catch (JsonException)
        {
            response = null;
            return false;
        }
    }

    private static ITemplateParser ResolveMarkdownTemplateParser(IEnumerable<ITemplateParser> templateParsers)
    {
        ArgumentNullException.ThrowIfNull(templateParsers);

        return templateParsers.FirstOrDefault(parser =>
            parser.SupportedExtensions.Any(extension => string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException("No markdown template parser is registered for data extraction response recovery.");
    }

    private static string GetLastAssistantMessageText(IList<ChatMessage> messages)
    {
        if (messages is null)
        {
            return null;
        }

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var messageText = GetMessageText(messages[i]);

            if (messages[i].Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(messageText))
            {
                return messageText.Trim();
            }
        }

        return null;
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

    private static string CreateResponseLogPreview(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return "<empty>";
        }

        const int maxLength = 512;

        return responseText.Length <= maxLength
            ? responseText
            : responseText[..maxLength] + "...";
    }

    private sealed class ExtractionResponse
    {
        public List<ExtractionResult> Fields { get; set; } = [];

        public bool SessionEnded { get; set; }
    }

    private sealed class ExtractionResult
    {
        public string Name { get; set; }

        public List<string> Values { get; set; } = [];

        public double Confidence { get; set; }
    }
}
