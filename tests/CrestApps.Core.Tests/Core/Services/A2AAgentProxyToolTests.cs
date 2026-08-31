using A2A;
using CrestApps.Core.AI.A2A.Services;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Tests A2A proxy tool response handling and immutable metadata.
/// </summary>
public sealed class A2AAgentProxyToolTests
{
    /// <summary>
    /// Verifies that proxy instances expose the same schema while retaining isolated mutable properties.
    /// </summary>
    [Fact]
    public void Metadata_MultipleInstances_PreservesSchemaAndPropertyIsolation()
    {
        var first = CreateTool("first");
        var second = CreateTool("second");

        Assert.Equal(first.JsonSchema.GetRawText(), second.JsonSchema.GetRawText());
        Assert.Equal("object", first.JsonSchema.GetProperty("type").GetString());
        Assert.Equal("message", first.JsonSchema.GetProperty("required")[0].GetString());

        var firstProperties = Assert.IsType<Dictionary<string, object>>(first.AdditionalProperties);
        var secondProperties = Assert.IsType<Dictionary<string, object>>(second.AdditionalProperties);
        Assert.NotSame(firstProperties, secondProperties);

        firstProperties["Strict"] = true;

        Assert.False(Assert.IsType<bool>(secondProperties["Strict"]));
    }

    /// <summary>
    /// Verifies that message text parts are concatenated in protocol order and null text is empty.
    /// </summary>
    [Fact]
    public void ExtractTextFromResponse_AgentMessage_ConcatenatesTextPartsExactly()
    {
        var response = CreateMessageResponse(
            Part.FromText("first"),
            new Part(),
            Part.FromText(string.Empty),
            Part.FromText("second"));

        var result = ExtractTextFromResponse(response);

        Assert.Equal("firstsecond", result);
    }

    /// <summary>
    /// Verifies that a message without text returns the null result used by the proxy fallback.
    /// </summary>
    [Fact]
    public void ExtractTextFromResponse_AgentMessageWithoutText_ReturnsNull()
    {
        var response = CreateMessageResponse(new Part());

        var result = ExtractTextFromResponse(response);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that non-empty artifact text takes precedence over task status text.
    /// </summary>
    [Fact]
    public void ExtractTextFromResponse_AgentTask_PrefersArtifactText()
    {
        var response = CreateTask(
            [
                new Artifact
                {
                    ArtifactId = "first",
                    Parts =
                    [
                        Part.FromText("artifact-"),
                        new Part(),
                    ],
                },
                new Artifact
                {
                    ArtifactId = "second",
                    Parts = [Part.FromText("text")],
                },
            ],
            CreateMessage(Part.FromText("status")));

        var result = ExtractTextFromResponse(response);

        Assert.Equal("artifact-text", result);
    }

    /// <summary>
    /// Verifies that empty artifact text falls back to concatenated task status text.
    /// </summary>
    [Fact]
    public void ExtractTextFromResponse_AgentTaskWithEmptyArtifacts_ReturnsStatusText()
    {
        var response = CreateTask(
            [
                new Artifact
                {
                    ArtifactId = "empty",
                    Parts =
                    [
                        new Part(),
                        Part.FromText(string.Empty),
                    ],
                },
            ],
            CreateMessage(
                Part.FromText("status-"),
                Part.FromText("text")));

        var result = ExtractTextFromResponse(response);

        Assert.Equal("status-text", result);
    }

    /// <summary>
    /// Verifies that responses without text retain the null result used by the proxy fallback.
    /// </summary>
    [Fact]
    public void ExtractTextFromResponse_ResponseWithoutText_ReturnsNull()
    {
        var messageResult = ExtractTextFromResponse(CreateMessageResponse());
        var taskResult = ExtractTextFromResponse(CreateTask([], CreateMessage()));

        Assert.Null(messageResult);
        Assert.Null(taskResult);
    }

    /// <summary>
    /// Creates an A2A proxy tool for metadata tests.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>The proxy tool.</returns>
    private static A2AAgentProxyTool CreateTool(string name)
    {
        return new A2AAgentProxyTool(
            name,
            "Description",
            "https://agent.example",
            "connection");
    }

    /// <summary>
    /// Creates an A2A message response with the supplied parts.
    /// </summary>
    /// <param name="parts">The message parts.</param>
    /// <returns>The A2A message response.</returns>
    private static SendMessageResponse CreateMessageResponse(params Part[] parts)
    {
        return new SendMessageResponse
        {
            Message = CreateMessage(parts),
        };
    }

    /// <summary>
    /// Creates an agent message with the supplied parts.
    /// </summary>
    /// <param name="parts">The message parts.</param>
    /// <returns>The agent message.</returns>
    private static Message CreateMessage(params Part[] parts)
    {
        return new Message
        {
            Role = Role.Agent,
            MessageId = "message",
            Parts = [.. parts],
        };
    }

    /// <summary>
    /// Creates an agent task with artifacts and a status message.
    /// </summary>
    /// <param name="artifacts">The task artifacts.</param>
    /// <param name="statusMessage">The task status message.</param>
    /// <returns>The A2A task response.</returns>
    private static SendMessageResponse CreateTask(
        List<Artifact> artifacts,
        Message statusMessage)
    {
        return new SendMessageResponse
        {
            Task = new AgentTask
            {
                Id = "task",
                ContextId = "context",
                Artifacts = artifacts,
                Status = new A2A.TaskStatus
                {
                    State = TaskState.Completed,
                    Message = statusMessage,
                },
            },
        };
    }

    /// <summary>
    /// Invokes the production response text extractor.
    /// </summary>
    /// <param name="response">The A2A response.</param>
    /// <returns>The extracted response text.</returns>
    private static string ExtractTextFromResponse(SendMessageResponse response)
    {
        return A2AAgentProxyTool.ExtractTextFromResponse(response);
    }
}
