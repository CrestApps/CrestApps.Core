using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Verifies the exact Copilot prompt-composition compatibility contract.
/// </summary>
public sealed class CopilotOrchestratorPromptTests
{
    /// <summary>
    /// Verifies absent history returns the current message without creating a replacement string.
    /// </summary>
    /// <param name="useNullHistory">Whether the history reference should be null.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildPromptWithHistory_WithoutHistory_ReturnsCurrentMessageReference(
        bool useNullHistory)
    {
        var currentMessage = new string("current message".ToCharArray());
        var context = new OrchestrationContext
        {
            UserMessage = currentMessage,
            ConversationHistory = useNullHistory
                ? null
                : [],
        };

        var prompt = CopilotOrchestrator.BuildPromptWithHistory(context);

        Assert.Same(currentMessage, prompt);
    }

    /// <summary>
    /// Verifies role filtering, aggregate text projection, labels, ordering, and separators.
    /// </summary>
    [Fact]
    public void BuildPromptWithHistory_WithMixedMessages_PreservesExactPrompt()
    {
        var context = new OrchestrationContext
        {
            UserMessage = "current",
            ConversationHistory =
            [
                new ChatMessage(ChatRole.User, [new TextContent("single user")]),
                new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Contents =
                    [
                        new TextContent("assistant "),
                        new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream"),
                        null,
                        new TextContent("reply"),
                        new TextContent(null),
                        new TextContent("\ncontinued"),
                    ],
                },
                new ChatMessage(ChatRole.System, "ignored system"),
                new ChatMessage(ChatRole.Tool, "ignored tool"),
                new ChatMessage(new ChatRole("observer"), "ignored observer"),
                new ChatMessage(ChatRole.User, (string)null),
                new ChatMessage(ChatRole.Assistant, (IList<AIContent>)null),
                new ChatMessage(
                    ChatRole.User,
                    [new DataContent(new byte[] { 4, 5 }, "application/octet-stream")]),
                new ChatMessage(ChatRole.User, " \t"),
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent(string.Empty), new TextContent("tail")]),
            ],
        };
        var expected =
            "[Conversation History]" + Environment.NewLine +
            "User: single user" + Environment.NewLine +
            "Assistant: assistant reply\ncontinued" + Environment.NewLine +
            "User:  \t" + Environment.NewLine +
            "Assistant: tail" + Environment.NewLine +
            Environment.NewLine +
            "[Current Message]" + Environment.NewLine +
            "current";

        var prompt = CopilotOrchestrator.BuildPromptWithHistory(context);

        Assert.Equal(expected, prompt);
    }

    /// <summary>
    /// Verifies a non-empty history still emits both section headers when every entry is filtered out.
    /// </summary>
    [Fact]
    public void BuildPromptWithHistory_WithNoEligibleMessages_StillEmitsSections()
    {
        var context = new OrchestrationContext
        {
            UserMessage = null,
            ConversationHistory =
            [
                new ChatMessage(ChatRole.System, "ignored"),
                new ChatMessage(ChatRole.User, string.Empty),
                new ChatMessage(
                    ChatRole.Assistant,
                    [new DataContent(new byte[] { 1 }, "application/octet-stream")]),
            ],
        };
        var expected =
            "[Conversation History]" + Environment.NewLine +
            Environment.NewLine +
            "[Current Message]" + Environment.NewLine;

        var prompt = CopilotOrchestrator.BuildPromptWithHistory(context);

        Assert.Equal(expected, prompt);
    }

    /// <summary>
    /// Verifies a null history element retains the existing null-reference failure.
    /// </summary>
    [Fact]
    public void BuildPromptWithHistory_WithNullMessage_ThrowsNullReferenceException()
    {
        var context = new OrchestrationContext
        {
            UserMessage = "current",
            ConversationHistory =
            [
                new ChatMessage(ChatRole.User, "first"),
                null,
            ],
        };

        Assert.Throws<NullReferenceException>(
            () => CopilotOrchestrator.BuildPromptWithHistory(context));
    }

    /// <summary>
    /// Verifies MCP connection descriptions retain exact source-specific formatting and ordering.
    /// </summary>
    [Fact]
    public void AppendMcpServerDescription_WithMixedConnections_PreservesExactFormatting()
    {
        var systemMessage = new SystemMessageConfig
        {
            Content = "existing\r\nsystem",
        };
        var sessionConfig = new SessionConfig
        {
            SystemMessage = systemMessage,
        };
        var connections = new McpConnection[]
        {
            CreateSseConnection(
                "sse-id",
                "Remote tools",
                new Uri("https://mcp.example.com/events")),
            CreateStdioConnection(
                "stdio-id",
                null,
                "dotnet run --project tool"),
            new()
            {
                ItemId = "missing-sse",
                DisplayText = "Missing endpoint",
                Source = McpConstants.TransportTypes.Sse,
            },
            CreateStdioConnection("missing-command", "Missing command", null),
            new()
            {
                ItemId = "custom-id",
                Source = "custom",
            },
        };
        var expected =
            "existing\r\nsystem" + Environment.NewLine +
            "[Available MCP Servers]" + Environment.NewLine +
            "- Remote tools (SSE: https://mcp.example.com/events)" + Environment.NewLine +
            "- stdio-id (StdIO: dotnet run --project tool)" + Environment.NewLine +
            "- Missing endpoint" + Environment.NewLine +
            "- Missing command" + Environment.NewLine +
            "- custom-id" + Environment.NewLine;

        CopilotOrchestrator.AppendMcpServerDescription(sessionConfig, connections);

        Assert.Same(systemMessage, sessionConfig.SystemMessage);
        Assert.Equal(expected, sessionConfig.SystemMessage.Content);
    }

    /// <summary>
    /// Verifies absent and empty system-message states produce the same MCP-only text while preserving
    /// an existing configuration object.
    /// </summary>
    [Fact]
    public void AppendMcpServerDescription_WithoutExistingContent_PreservesConfigurationIdentity()
    {
        var connection = new McpConnection
        {
            ItemId = "connection",
            DisplayText = "Connection",
            Source = "custom",
        };
        var expected =
            Environment.NewLine +
            "[Available MCP Servers]" + Environment.NewLine +
            "- Connection" + Environment.NewLine;
        var withoutSystemMessage = new SessionConfig();
        var nullContentMessage = new SystemMessageConfig
        {
            Content = null,
        };
        var withNullContent = new SessionConfig
        {
            SystemMessage = nullContentMessage,
        };
        var emptyContentMessage = new SystemMessageConfig
        {
            Content = string.Empty,
        };
        var withEmptyContent = new SessionConfig
        {
            SystemMessage = emptyContentMessage,
        };

        CopilotOrchestrator.AppendMcpServerDescription(withoutSystemMessage, [connection]);
        CopilotOrchestrator.AppendMcpServerDescription(withNullContent, [connection]);
        CopilotOrchestrator.AppendMcpServerDescription(withEmptyContent, [connection]);

        Assert.Equal(expected, withoutSystemMessage.SystemMessage.Content);
        Assert.Same(nullContentMessage, withNullContent.SystemMessage);
        Assert.Equal(expected, withNullContent.SystemMessage.Content);
        Assert.Same(emptyContentMessage, withEmptyContent.SystemMessage);
        Assert.Equal(expected, withEmptyContent.SystemMessage.Content);
    }

    /// <summary>
    /// Verifies a connection failure does not partially replace the existing system message.
    /// </summary>
    [Fact]
    public void AppendMcpServerDescription_WithNullConnection_LeavesSystemMessageUnchanged()
    {
        var systemMessage = new SystemMessageConfig
        {
            Content = "existing",
        };
        var sessionConfig = new SessionConfig
        {
            SystemMessage = systemMessage,
        };
        var connections = new McpConnection[]
        {
            new()
            {
                ItemId = "first",
                DisplayText = "First",
                Source = "custom",
            },
            null,
        };

        Assert.Throws<NullReferenceException>(
            () => CopilotOrchestrator.AppendMcpServerDescription(sessionConfig, connections));
        Assert.Same(systemMessage, sessionConfig.SystemMessage);
        Assert.Equal("existing", systemMessage.Content);
    }

    /// <summary>
    /// Creates an SSE MCP connection for prompt-composition tests.
    /// </summary>
    /// <param name="itemId">The connection identifier.</param>
    /// <param name="displayText">The display text.</param>
    /// <param name="endpoint">The SSE endpoint.</param>
    /// <returns>The configured connection.</returns>
    private static McpConnection CreateSseConnection(
        string itemId,
        string displayText,
        Uri endpoint)
    {
        var connection = new McpConnection
        {
            ItemId = itemId,
            DisplayText = displayText,
            Source = McpConstants.TransportTypes.Sse,
        };
        connection.Put(new SseMcpConnectionMetadata
        {
            Endpoint = endpoint,
        });

        return connection;
    }

    /// <summary>
    /// Creates a standard-input/output MCP connection for prompt-composition tests.
    /// </summary>
    /// <param name="itemId">The connection identifier.</param>
    /// <param name="displayText">The display text.</param>
    /// <param name="command">The process command.</param>
    /// <returns>The configured connection.</returns>
    private static McpConnection CreateStdioConnection(
        string itemId,
        string displayText,
        string command)
    {
        var connection = new McpConnection
        {
            ItemId = itemId,
            DisplayText = displayText,
            Source = McpConstants.TransportTypes.StdIo,
        };
        connection.Put(new StdioMcpConnectionMetadata
        {
            Command = command,
        });

        return connection;
    }
}
