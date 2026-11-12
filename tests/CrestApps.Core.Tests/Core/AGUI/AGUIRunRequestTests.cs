using System.Text.Json;
using CrestApps.Core.AI.AGUI;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.AGUI;

public sealed class AGUIRunRequestTests
{
    [Fact]
    public void From_ReadsTheNewestUserMessageTheProfileTheContextAndTheState()
    {
        var request = Read("""
            {
              "threadId": "session-1",
              "runId": "run-1",
              "messages": [
                { "id": "u0", "role": "user", "content": "Earlier" },
                { "id": "a0", "role": "assistant", "content": "Reply" },
                { "id": "u1", "role": "user", "content": "Fill in the title" }
              ],
              "tools": [],
              "context": [
                { "description": "Current page", "value": "Edit article (/admin/articles/1)" },
                { "description": "Empty", "value": "" }
              ],
              "state": { "title": "Draft" },
              "forwardedProps": { "profileId": "profile-1", "sessionProfileId": "child-1" }
            }
            """);

        Assert.Equal("session-1", request.ThreadId);
        Assert.Equal("run-1", request.RunId);
        Assert.Equal("profile-1", request.ProfileId);
        Assert.Equal("child-1", request.SessionProfileId);
        Assert.Equal("Fill in the title", request.Prompt);

        var context = Assert.Single(request.Context);
        Assert.Equal("Current page", context.Description);
        Assert.Equal("Draft", request.State["title"]!.GetValue<string>());
        Assert.False(request.IsResume);
    }

    [Fact]
    public void From_ReadsAContinuationOnTheNextPage()
    {
        var continuation = Read("""{ "threadId": "session-1", "messages": [], "tools": [], "context": [], "forwardedProps": { "profileId": "profile-1", "continueRun": true } }""");
        var normal = Read("""{ "threadId": "session-1", "messages": [], "tools": [], "context": [], "forwardedProps": { "profileId": "profile-1", "continueRun": "yes" } }""");

        Assert.True(continuation.IsContinuation);
        Assert.False(continuation.IsResume);
        Assert.False(normal.IsContinuation);
    }

    [Fact]
    public void From_TreatsAnEmptyThreadAsANewSessionAndMakesARunId()
    {
        var request = Read("""{ "threadId": "", "runId": "", "messages": [], "tools": [], "context": [], "forwardedProps": {} }""");

        Assert.Null(request.ThreadId);
        Assert.False(string.IsNullOrEmpty(request.RunId));
        Assert.Null(request.Prompt);
        Assert.Null(request.ProfileId);
        Assert.Null(request.State);
    }

    [Fact]
    public void From_ReadsTheClientToolsAsDeclarations()
    {
        var request = Read("""
            {
              "threadId": "t", "runId": "r", "messages": [], "context": [], "forwardedProps": {},
              "tools": [{ "name": "get_weather", "description": "Gets the weather.", "parameters": { "type": "object", "properties": { "city": { "type": "string" } } } }]
            }
            """);

        var tool = Assert.Single(request.ClientTools);
        Assert.Equal("get_weather", tool.Name);
        Assert.False(tool is AIFunction);
        Assert.IsAssignableFrom<AIFunctionDeclaration>(tool);
    }

    [Fact]
    public void TryGetApproval_ReadsApprovedDeniedAndCancelledAnswers()
    {
        var request = Read("""
            {
              "threadId": "t", "runId": "r", "messages": [], "tools": [], "context": [], "forwardedProps": {},
              "resume": [
                { "interruptId": "approve", "status": "resolved", "payload": { "approved": true } },
                { "interruptId": "deny", "status": "resolved", "payload": { "approved": false } },
                { "interruptId": "cancel", "status": "cancelled" }
              ]
            }
            """);

        Assert.True(request.IsResume);
        Assert.True(request.TryGetApproval("approve", out var approved) && approved);
        Assert.True(request.TryGetApproval("deny", out var denied) && !denied);
        Assert.True(request.TryGetApproval("cancel", out var cancelled) && !cancelled);
        Assert.False(request.TryGetApproval("missing", out _));
    }

    [Fact]
    public void From_ReadsTheResultsOfClientToolCallsAsAResume()
    {
        var request = Read("""
            {
              "threadId": "t", "runId": "r", "tools": [], "context": [], "forwardedProps": {},
              "messages": [{ "id": "tool-1", "role": "tool", "toolCallId": "call-1", "content": "Sunny" }]
            }
            """);

        Assert.True(request.IsResume);
        Assert.Equal("Sunny", request.ClientToolResults["call-1"]);
    }

    private static AGUIRunRequest Read(string json)
    {
        return AGUIRunRequest.From(AGUIJson.DeserializeInput(JsonElement.Parse(json)));
    }
}
