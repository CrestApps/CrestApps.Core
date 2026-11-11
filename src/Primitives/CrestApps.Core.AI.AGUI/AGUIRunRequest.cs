using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.AGUI;

/// <summary>
/// What a CrestApps chat run reads from an AG-UI run input: the profile and session, the new user message, the
/// context and state the client sent, the tools it runs itself, and its answers to interrupts and tool calls.
/// </summary>
/// <remarks>
/// The chat keeps the conversation on the server, so only the newest user message of <see cref="RunAgentInput.Messages"/>
/// is read, plus the results of tool calls that waited for the client. The profile id and the session profile id
/// come from <see cref="RunAgentInput.ForwardedProperties"/> (<c>profileId</c>, <c>sessionProfileId</c>).
/// </remarks>
public sealed class AGUIRunRequest
{
    /// <summary>
    /// The most context entries read from the input.
    /// </summary>
    public const int MaxContextEntries = 20;

    /// <summary>
    /// Gets the run input.
    /// </summary>
    public RunAgentInput Input { get; private init; }

    /// <summary>
    /// Gets the thread (chat session) id, or <see langword="null"/> for a new session.
    /// </summary>
    public string ThreadId { get; private init; }

    /// <summary>
    /// Gets the run id.
    /// </summary>
    public string RunId { get; private init; }

    /// <summary>
    /// Gets the id of the AI profile to chat with.
    /// </summary>
    public string ProfileId { get; private init; }

    /// <summary>
    /// Gets the id of the profile a session-level prompt (such as a generated prompt) runs, or <see langword="null"/>.
    /// </summary>
    public string SessionProfileId { get; private init; }

    /// <summary>
    /// Gets the text of the newest user message, or <see langword="null"/> when the run only answers interrupts or
    /// tool calls.
    /// </summary>
    public string Prompt { get; private init; }

    /// <summary>
    /// Gets the context the client sent.
    /// </summary>
    public IReadOnlyList<ChatClientContextEntry> Context { get; private init; } = [];

    /// <summary>
    /// Gets the state the client shares with the agent, or <see langword="null"/> when it shares none.
    /// </summary>
    public JsonObject State { get; private init; }

    /// <summary>
    /// Gets the tools the client runs itself, as declarations the model may call.
    /// </summary>
    public IReadOnlyList<AITool> ClientTools { get; private init; } = [];

    /// <summary>
    /// Gets the client's answers to the interrupts of the previous run, keyed by interrupt id.
    /// </summary>
    public IReadOnlyDictionary<string, AGUIResume> Resume { get; private init; } = new Dictionary<string, AGUIResume>();

    /// <summary>
    /// Gets the results of the tool calls that waited for the client, keyed by tool call id.
    /// </summary>
    public IReadOnlyDictionary<string, string> ClientToolResults { get; private init; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets a value indicating whether the run continues a previous run that waited for approvals or for the client.
    /// </summary>
    public bool IsResume => Resume.Count > 0 || (ClientToolResults.Count > 0 && string.IsNullOrWhiteSpace(Prompt));

    /// <summary>
    /// Reads a run request from an AG-UI run input.
    /// </summary>
    /// <param name="input">The run input.</param>
    /// <returns>The run request.</returns>
    public static AGUIRunRequest From(RunAgentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var forwarded = input.ForwardedProperties is { ValueKind: JsonValueKind.Object } props
            ? props
            : default;

        return new AGUIRunRequest
        {
            Input = input,
            ThreadId = string.IsNullOrWhiteSpace(input.ThreadId) ? null : input.ThreadId,
            RunId = string.IsNullOrWhiteSpace(input.RunId) ? Guid.NewGuid().ToString("n") : input.RunId,
            ProfileId = GetString(forwarded, "profileId"),
            SessionProfileId = GetString(forwarded, "sessionProfileId"),
            Prompt = GetPrompt(input.Messages),
            Context = GetContext(input.Context),
            State = GetState(input.State),
            ClientTools = GetClientTools(input.Tools),
            Resume = GetResume(input.Resume),
            ClientToolResults = GetClientToolResults(input.Messages),
        };
    }

    /// <summary>
    /// Determines whether the client approved a tool call it was asked about.
    /// </summary>
    /// <param name="interruptId">The id of the interrupt (the approval request id).</param>
    /// <param name="approved">Whether the client approved the call.</param>
    /// <returns><see langword="true"/> when the client answered the interrupt.</returns>
    public bool TryGetApproval(string interruptId, out bool approved)
    {
        approved = false;

        if (interruptId is null || !Resume.TryGetValue(interruptId, out var resume))
        {
            return false;
        }

        approved = string.Equals(resume.Status, ResumeStatus.Resolved, StringComparison.OrdinalIgnoreCase) &&
            resume.Payload is { ValueKind: JsonValueKind.Object } payload &&
            payload.TryGetProperty("approved", out var value) &&
            value.ValueKind == JsonValueKind.True;

        return true;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;
    }

    private static string GetPrompt(IList<AGUIMessage> messages)
    {
        if (messages is null || messages.Count == 0 || messages[^1] is not AGUIUserMessage user)
        {
            return null;
        }

        var content = user.Content;

        if (content.IsText)
        {
            return content.ToString();
        }

        var texts = new List<string>();

        foreach (var part in content)
        {
            if (part is AGUITextInputContent text && !string.IsNullOrEmpty(text.Text))
            {
                texts.Add(text.Text);
            }
        }

        return texts.Count == 0 ? null : string.Join("\n", texts);
    }

    private static List<ChatClientContextEntry> GetContext(IList<AGUIContext> context)
    {
        if (context is null || context.Count == 0)
        {
            return [];
        }

        return context
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Value))
            .Take(MaxContextEntries)
            .Select(entry => new ChatClientContextEntry
            {
                Description = entry.Description,
                Value = entry.Value,
            })
            .ToList();
    }

    private static JsonObject GetState(JsonElement? state)
    {
        if (state is not { ValueKind: JsonValueKind.Object } value)
        {
            return null;
        }

        return JsonNode.Parse(value.GetRawText()) as JsonObject;
    }

    private static List<AITool> GetClientTools(IList<AGUITool> tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return [];
        }

        return tools
            .Where(tool => tool is not null && !string.IsNullOrWhiteSpace(tool.Name))
            .Select(tool => (AITool)AIFunctionFactory.CreateDeclaration(
                tool.Name,
                tool.Description,
                tool.Parameters is { ValueKind: JsonValueKind.Object } schema
                    ? schema
                    : JsonElement.Parse("""{"type":"object"}""")))
            .ToList();
    }

    private static Dictionary<string, AGUIResume> GetResume(IList<AGUIResume> resume)
    {
        var answers = new Dictionary<string, AGUIResume>(StringComparer.Ordinal);

        if (resume is null)
        {
            return answers;
        }

        foreach (var entry in resume)
        {
            if (entry is not null && !string.IsNullOrEmpty(entry.InterruptId))
            {
                answers[entry.InterruptId] = entry;
            }
        }

        return answers;
    }

    private static Dictionary<string, string> GetClientToolResults(IList<AGUIMessage> messages)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);

        if (messages is null)
        {
            return results;
        }

        foreach (var message in messages)
        {
            if (message is AGUIToolMessage tool && !string.IsNullOrEmpty(tool.ToolCallId))
            {
                results[tool.ToolCallId] = tool.Error ?? tool.Content.ToString();
            }
        }

        return results;
    }
}
