using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using Cysharp.Text;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured repeated <see cref="ChatMessage.Text"/> reads with the production Copilot
/// prompt builder.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class CopilotPromptHistoryBenchmarks
{
    private OrchestrationContext _context;

    /// <summary>
    /// Gets or sets the number of history messages.
    /// </summary>
    [Params(10, 100, 1_000, 10_000)]
    public int HistoryCount { get; set; }

    /// <summary>
    /// Gets or sets the history content and role distribution.
    /// </summary>
    [Params(
        CopilotPromptHistoryShape.SingleTextContent,
        CopilotPromptHistoryShape.MultipleTextContents,
        CopilotPromptHistoryShape.SparseRoles)]
    public CopilotPromptHistoryShape Shape { get; set; }

    /// <summary>
    /// Creates stable prompt inputs and verifies exact output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _context = new OrchestrationContext
        {
            UserMessage = "Summarize the release readiness and include the warning-free validation results.",
            ConversationHistory = CreateHistory(HistoryCount, Shape),
        };

        var legacy = BuildPromptWithHistoryLegacy(_context);
        var current = CopilotOrchestrator.BuildPromptWithHistory(_context);

        if (!string.Equals(legacy, current, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The production Copilot prompt changed the captured output.");
        }
    }

    /// <summary>
    /// Builds the prompt with the captured repeated aggregate-text reads.
    /// </summary>
    /// <returns>The complete Copilot prompt.</returns>
    [Benchmark(Baseline = true)]
    public string Legacy()
    {
        return BuildPromptWithHistoryLegacy(_context);
    }

    /// <summary>
    /// Builds the prompt with the production implementation.
    /// </summary>
    /// <returns>The complete Copilot prompt.</returns>
    [Benchmark]
    public string Current()
    {
        return CopilotOrchestrator.BuildPromptWithHistory(_context);
    }

    /// <summary>
    /// Preserves the original Copilot history-composition implementation as the benchmark baseline.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <returns>The complete Copilot prompt.</returns>
    private static string BuildPromptWithHistoryLegacy(OrchestrationContext context)
    {
        if (context.ConversationHistory is not { Count: > 0 })
        {
            return context.UserMessage;
        }

        using var sb = ZString.CreateStringBuilder();
        sb.AppendLine("[Conversation History]");

        foreach (var message in context.ConversationHistory)
        {
            if (string.IsNullOrEmpty(message.Text))
            {
                continue;
            }

            if (message.Role == ChatRole.User)
            {
                sb.Append("User: ");
            }
            else if (message.Role == ChatRole.Assistant)
            {
                sb.Append("Assistant: ");
            }
            else
            {
                continue;
            }

            sb.AppendLine(message.Text);
        }

        sb.AppendLine();
        sb.AppendLine("[Current Message]");
        sb.Append(context.UserMessage);

        return sb.ToString();
    }

    /// <summary>
    /// Creates a history with the requested content and role distribution.
    /// </summary>
    /// <param name="historyCount">The number of messages to create.</param>
    /// <param name="shape">The history shape.</param>
    /// <returns>The generated history.</returns>
    private static List<ChatMessage> CreateHistory(
        int historyCount,
        CopilotPromptHistoryShape shape)
    {
        var history = new List<ChatMessage>(historyCount);

        for (var index = 0; index < historyCount; index++)
        {
            history.Add(shape switch
            {
                CopilotPromptHistoryShape.SingleTextContent => CreateSingleTextMessage(index),
                CopilotPromptHistoryShape.MultipleTextContents => CreateMultipleTextMessage(
                    index,
                    index % 2 == 0
                        ? ChatRole.User
                        : ChatRole.Assistant),
                CopilotPromptHistoryShape.SparseRoles => CreateSparseRoleMessage(index),
                _ => throw new InvalidOperationException($"Unsupported history shape '{shape}'."),
            });
        }

        return history;
    }

    /// <summary>
    /// Creates a user or assistant message with one text-content item.
    /// </summary>
    /// <param name="index">The message index.</param>
    /// <returns>The generated message.</returns>
    private static ChatMessage CreateSingleTextMessage(int index)
    {
        return new ChatMessage(
            index % 2 == 0
                ? ChatRole.User
                : ChatRole.Assistant,
            [new TextContent($"Message {index}: validate deployment, tests, packages, and release notes.")]);
    }

    /// <summary>
    /// Creates a message with multiple text-content items and interleaved non-text content.
    /// </summary>
    /// <param name="index">The message index.</param>
    /// <param name="role">The message role.</param>
    /// <returns>The generated message.</returns>
    private static ChatMessage CreateMultipleTextMessage(
        int index,
        ChatRole role)
    {
        return new ChatMessage
        {
            Role = role,
            Contents =
            [
                new TextContent($"Message {index}: "),
                new DataContent(new byte[] { 1, 2, 3, 4 }, "application/octet-stream"),
                null,
                new TextContent("validate deployment and tests; "),
                new TextContent("include package and release-note status."),
            ],
        };
    }

    /// <summary>
    /// Creates a history where one message in ten has a supported role.
    /// </summary>
    /// <param name="index">The message index.</param>
    /// <returns>The generated message.</returns>
    private static ChatMessage CreateSparseRoleMessage(int index)
    {
        var role = (index % 10) switch
        {
            0 => ChatRole.User,
            1 => ChatRole.Assistant,
            2 or 3 or 4 => ChatRole.System,
            5 or 6 or 7 => ChatRole.Tool,
            _ => new ChatRole("observer"),
        };

        return CreateMultipleTextMessage(index, role);
    }
}

/// <summary>
/// Defines the prompt-history distributions measured by the Copilot benchmark.
/// </summary>
public enum CopilotPromptHistoryShape
{
    /// <summary>
    /// Every message has a supported role and one text-content item.
    /// </summary>
    SingleTextContent,

    /// <summary>
    /// Every message has a supported role and multiple text-content items.
    /// </summary>
    MultipleTextContents,

    /// <summary>
    /// One message in ten has a supported role, with multiple text-content items on every message.
    /// </summary>
    SparseRoles,
}

/// <summary>
/// Compares the captured intermediate-description concatenation with the production MCP system-message
/// composition.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class CopilotMcpSystemMessageBenchmarks
{
    private IReadOnlyCollection<McpConnection> _connections;
    private string _existingSystemMessage;

    /// <summary>
    /// Gets or sets the number of MCP connections.
    /// </summary>
    [Params(10, 100, 1_000)]
    public int ConnectionCount { get; set; }

    /// <summary>
    /// Gets or sets whether an existing 8-KB system message is present.
    /// </summary>
    [Params(false, true)]
    public bool HasExistingSystemMessage { get; set; }

    /// <summary>
    /// Creates stable connection inputs and verifies exact output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _connections = CreateConnections(ConnectionCount);
        _existingSystemMessage = HasExistingSystemMessage
            ? new string('s', 8 * 1024)
            : null;

        var legacy = Legacy();
        var current = Current();
        var candidate = SingleBuilderCandidate();

        if (!string.Equals(legacy, current, StringComparison.Ordinal) ||
            !string.Equals(legacy, candidate, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The production MCP description changed the captured output.");
        }
    }

    /// <summary>
    /// Builds the MCP system message with the captured intermediate description string.
    /// </summary>
    /// <returns>The complete system-message content.</returns>
    [Benchmark(Baseline = true)]
    public string Legacy()
    {
        var sessionConfig = CreateSessionConfig();

        AppendMcpServerDescriptionLegacy(sessionConfig, _connections);

        return sessionConfig.SystemMessage.Content;
    }

    /// <summary>
    /// Builds the MCP system message with the production implementation.
    /// </summary>
    /// <returns>The complete system-message content.</returns>
    [Benchmark]
    public string Current()
    {
        var sessionConfig = CreateSessionConfig();

        CopilotOrchestrator.AppendMcpServerDescription(sessionConfig, _connections);

        return sessionConfig.SystemMessage.Content;
    }

    /// <summary>
    /// Builds the MCP system message with the rejected single-builder candidate.
    /// </summary>
    /// <returns>The complete system-message content.</returns>
    [Benchmark]
    public string SingleBuilderCandidate()
    {
        var sessionConfig = CreateSessionConfig();

        AppendMcpServerDescriptionSingleBuilderCandidate(sessionConfig, _connections);

        return sessionConfig.SystemMessage.Content;
    }

    /// <summary>
    /// Creates a new session configuration for one benchmark operation.
    /// </summary>
    /// <returns>The session configuration.</returns>
    private SessionConfig CreateSessionConfig()
    {
        return new SessionConfig
        {
            SystemMessage = HasExistingSystemMessage
                ? new SystemMessageConfig
                {
                    Content = _existingSystemMessage,
                }
                : null,
        };
    }

    /// <summary>
    /// Preserves the original MCP system-message composition as the benchmark baseline.
    /// </summary>
    /// <param name="sessionConfig">The Copilot session configuration.</param>
    /// <param name="connections">The MCP connections to describe.</param>
    private static void AppendMcpServerDescriptionLegacy(
        SessionConfig sessionConfig,
        IReadOnlyCollection<McpConnection> connections)
    {
        using var mcpDescription = ZString.CreateStringBuilder();
        mcpDescription.AppendLine();
        mcpDescription.AppendLine("[Available MCP Servers]");

        foreach (var connection in connections)
        {
            mcpDescription.Append("- ");
            mcpDescription.Append(connection.DisplayText ?? connection.ItemId);

            if (connection.Source == McpConstants.TransportTypes.Sse)
            {
                if (connection.TryGet<SseMcpConnectionMetadata>(out var sseMetadata) &&
                    sseMetadata.Endpoint is not null)
                {
                    mcpDescription.Append(" (SSE: ");
                    mcpDescription.Append(sseMetadata.Endpoint);
                    mcpDescription.Append(')');
                }
            }
            else if (connection.Source == McpConstants.TransportTypes.StdIo)
            {
                if (connection.TryGet<StdioMcpConnectionMetadata>(out var stdioMetadata) &&
                    !string.IsNullOrEmpty(stdioMetadata.Command))
                {
                    mcpDescription.Append(" (StdIO: ");
                    mcpDescription.Append(stdioMetadata.Command);
                    mcpDescription.Append(')');
                }
            }

            mcpDescription.AppendLine();
        }

        if (sessionConfig.SystemMessage is not null)
        {
            sessionConfig.SystemMessage.Content += mcpDescription.ToString();
        }
        else
        {
            sessionConfig.SystemMessage = new SystemMessageConfig
            {
                Content = mcpDescription.ToString(),
            };
        }
    }

    /// <summary>
    /// Appends MCP descriptions after first copying existing content into the same builder.
    /// </summary>
    /// <param name="sessionConfig">The Copilot session configuration.</param>
    /// <param name="connections">The MCP connections to describe.</param>
    private static void AppendMcpServerDescriptionSingleBuilderCandidate(
        SessionConfig sessionConfig,
        IReadOnlyCollection<McpConnection> connections)
    {
        var systemMessage = sessionConfig.SystemMessage;
        using var systemMessageBuilder = ZString.CreateStringBuilder();

        if (!string.IsNullOrEmpty(systemMessage?.Content))
        {
            systemMessageBuilder.Append(systemMessage.Content);
        }

        systemMessageBuilder.AppendLine();
        systemMessageBuilder.AppendLine("[Available MCP Servers]");

        foreach (var connection in connections)
        {
            systemMessageBuilder.Append("- ");
            systemMessageBuilder.Append(connection.DisplayText ?? connection.ItemId);

            if (connection.Source == McpConstants.TransportTypes.Sse)
            {
                if (connection.TryGet<SseMcpConnectionMetadata>(out var sseMetadata) &&
                    sseMetadata.Endpoint is not null)
                {
                    systemMessageBuilder.Append(" (SSE: ");
                    systemMessageBuilder.Append(sseMetadata.Endpoint);
                    systemMessageBuilder.Append(')');
                }
            }
            else if (connection.Source == McpConstants.TransportTypes.StdIo)
            {
                if (connection.TryGet<StdioMcpConnectionMetadata>(out var stdioMetadata) &&
                    !string.IsNullOrEmpty(stdioMetadata.Command))
                {
                    systemMessageBuilder.Append(" (StdIO: ");
                    systemMessageBuilder.Append(stdioMetadata.Command);
                    systemMessageBuilder.Append(')');
                }
            }

            systemMessageBuilder.AppendLine();
        }

        if (systemMessage is not null)
        {
            systemMessage.Content = systemMessageBuilder.ToString();
        }
        else
        {
            sessionConfig.SystemMessage = new SystemMessageConfig
            {
                Content = systemMessageBuilder.ToString(),
            };
        }
    }

    /// <summary>
    /// Creates mixed SSE, standard-input/output, and metadata-free MCP connections.
    /// </summary>
    /// <param name="connectionCount">The number of connections to create.</param>
    /// <returns>The generated connections.</returns>
    private static McpConnection[] CreateConnections(int connectionCount)
    {
        var connections = new McpConnection[connectionCount];

        for (var index = 0; index < connectionCount; index++)
        {
            var connection = new McpConnection
            {
                ItemId = $"connection-{index}",
                DisplayText = index % 5 == 0
                    ? null
                    : $"Connection {index}",
            };

            switch (index % 3)
            {
                case 0:
                    connection.Source = McpConstants.TransportTypes.Sse;
                    connection.Put(new SseMcpConnectionMetadata
                    {
                        Endpoint = new Uri($"https://mcp.example.com/events/{index}"),
                    });
                    break;
                case 1:
                    connection.Source = McpConstants.TransportTypes.StdIo;
                    connection.Put(new StdioMcpConnectionMetadata
                    {
                        Command = $"dotnet tool-{index}.dll --serve",
                    });
                    break;
                default:
                    connection.Source = "custom";
                    break;
            }

            connections[index] = connection;
        }

        return connections;
    }
}
