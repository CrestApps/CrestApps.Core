using System.Text.Json.Nodes;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling.SharedState;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Tooling;

public sealed class SharedStateTests
{
    [Fact]
    public void Apply_AddsReplacesAndRemovesValues()
    {
        var document = JsonNode.Parse("""{ "title": "Draft", "tags": ["a"], "meta": { "old": 1 } }""")!.AsObject();
        var operations = JsonNode.Parse("""
            [
              { "op": "replace", "path": "/title", "value": "Final" },
              { "op": "add", "path": "/tags/-", "value": "b" },
              { "op": "add", "path": "/tags/0", "value": "first" },
              { "op": "remove", "path": "/meta/old" },
              { "op": "add", "path": "/meta/a~1b", "value": true }
            ]
            """)!.AsArray();

        var result = JsonPatchApplier.Apply(document, operations);

        Assert.Equal("Final", result["title"]!.GetValue<string>());
        Assert.Equal(["first", "a", "b"], result["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()));
        Assert.False(result["meta"]!.AsObject().ContainsKey("old"));
        Assert.True(result["meta"]!["a/b"]!.GetValue<bool>());

        // The document itself is not changed.
        Assert.Equal("Draft", document["title"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_ReplacesTheWholeStateWithAnObject()
    {
        var result = JsonPatchApplier.Apply(
            JsonNode.Parse("""{ "a": 1 }""")!.AsObject(),
            JsonNode.Parse("""[{ "op": "replace", "path": "", "value": { "b": 2 } }]""")!.AsArray());

        Assert.False(result.ContainsKey("a"));
        Assert.Equal(2, result["b"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("""[{ "op": "replace", "path": "/missing", "value": 1 }]""")]
    [InlineData("""[{ "op": "remove", "path": "/missing" }]""")]
    [InlineData("""[{ "op": "move", "from": "/a", "path": "/b" }]""")]
    [InlineData("""[{ "op": "add", "path": "no-slash", "value": 1 }]""")]
    [InlineData("""[{ "op": "add", "path": "/list/5", "value": 1 }]""")]
    [InlineData("""[{ "op": "add", "path": "/a" }]""")]
    public void Apply_RejectsInvalidOperations(string operations)
    {
        var document = JsonNode.Parse("""{ "a": 1, "list": [] }""")!.AsObject();

        Assert.Throws<JsonPatchException>(() => JsonPatchApplier.Apply(document, JsonNode.Parse(operations)!.AsArray()));
    }

    [Fact]
    public async Task UpdateSharedStateFunction_AppliesTheOperationsAndTellsTheClient()
    {
        JsonArray reportedOperations = null;
        var state = new ChatSharedState
        {
            Value = JsonNode.Parse("""{ "note": "" }""")!.AsObject(),
            OnChangedAsync = (operations, _, _) =>
            {
                reportedOperations = operations;

                return ValueTask.CompletedTask;
            },
        };
        var function = new UpdateSharedStateFunction(state);

        var result = await function.InvokeAsync(new AIFunctionArguments
        {
            ["operations"] = JsonNode.Parse("""[{ "op": "replace", "path": "/note", "value": "Leave at the door" }]"""),
        }, TestContext.Current.CancellationToken);

        Assert.Equal("The shared state was updated.", result?.ToString());
        Assert.Equal("Leave at the door", state.Value["note"]!.GetValue<string>());
        Assert.Equal("/note", reportedOperations![0]!["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateSharedStateFunction_KeepsTheStateWhenAnOperationFails()
    {
        var state = new ChatSharedState { Value = JsonNode.Parse("""{ "note": "Keep" }""")!.AsObject() };
        var function = new UpdateSharedStateFunction(state);

        var result = await function.InvokeAsync(new AIFunctionArguments
        {
            ["operations"] = """[{ "op": "remove", "path": "/missing" }]""",
        }, TestContext.Current.CancellationToken);

        Assert.StartsWith("The shared state was not changed", result?.ToString());
        Assert.Equal("Keep", state.Value["note"]!.GetValue<string>());
    }

    [Fact]
    public async Task SharedStateToolRegistryProvider_OffersTheToolOnlyWithASharedState()
    {
        var provider = new SharedStateToolRegistryProvider();
        var without = new AICompletionContext();
        var with = new AICompletionContext();

        with.AdditionalProperties[AICompletionContextKeys.SharedState] = new ChatSharedState();

        Assert.Empty(await provider.GetToolsAsync(without, TestContext.Current.CancellationToken));

        var entry = Assert.Single(await provider.GetToolsAsync(with, TestContext.Current.CancellationToken));
        Assert.Equal(UpdateSharedStateFunction.FunctionName, entry.Name);
        Assert.IsType<UpdateSharedStateFunction>(await entry.CreateAsync(null));
    }
}
