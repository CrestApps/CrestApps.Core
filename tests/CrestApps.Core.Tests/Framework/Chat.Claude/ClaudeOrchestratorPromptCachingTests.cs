using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Core;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Framework.Chat.Claude;

/// <summary>
/// Verifies the <see cref="ClaudeOrchestrator"/> prompt-cache breakpoints reach the JSON body the Anthropic
/// chat client sends, on the last tool, the end of the system prompt and the latest message only.
/// </summary>
public sealed class ClaudeOrchestratorPromptCachingTests
{
    private const string ResponseJson = """
        {
          "id": "msg_test",
          "type": "message",
          "role": "assistant",
          "model": "claude-test",
          "content": [{ "type": "text", "text": "ok" }],
          "stop_reason": "end_turn",
          "stop_sequence": null,
          "usage": { "input_tokens": 1, "output_tokens": 1 }
        }
        """;

    private static readonly Action<List<ChatMessage>, ChatOptions> _applyPromptCaching =
        CreateApplyPromptCachingDelegate();

    /// <summary>
    /// Verifies prompt caching is on unless it is turned off.
    /// </summary>
    [Fact]
    public void ClaudeOptions_EnablePromptCaching_DefaultsToTrue()
    {
        Assert.True(new ClaudeOptions().EnablePromptCaching);
    }

    /// <summary>
    /// Verifies the request carries no cache control when no breakpoints are applied, which is why the
    /// orchestrator has to mark them.
    /// </summary>
    [Fact]
    public async Task Request_WithoutPromptCaching_SendsNoCacheControl()
    {
        var prompts = CreatePrompts();
        var options = CreateOptions();

        var body = await SendAsync(prompts, options);

        Assert.DoesNotContain("cache_control", body.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the system prompt, the last tool and the last block of the latest message carry cache control,
    /// and nothing else does.
    /// </summary>
    [Fact]
    public async Task Request_WithPromptCaching_MarksSystemLastToolAndLatestMessage()
    {
        var prompts = CreatePrompts();
        var options = CreateOptions();

        _applyPromptCaching(prompts, options);
        var body = await SendAsync(prompts, options);

        var system = Assert.IsType<JsonArray>(body["system"]);
        Assert.Equal("ephemeral", (string)system[^1]["cache_control"]["type"]);

        var tools = Assert.IsType<JsonArray>(body["tools"]);
        Assert.Equal(2, tools.Count);
        Assert.Null(tools[0]["cache_control"]);
        Assert.Equal("ephemeral", (string)tools[1]["cache_control"]["type"]);

        var messages = Assert.IsType<JsonArray>(body["messages"]);
        var lastContent = Assert.IsType<JsonArray>(messages[^1]["content"]);
        Assert.Equal("current-user", (string)lastContent[^1]["text"]);
        Assert.Equal("ephemeral", (string)lastContent[^1]["cache_control"]["type"]);

        Assert.Equal(3, CountOccurrences(body.ToJsonString(), "\"cache_control\""));
    }

    /// <summary>
    /// Verifies a lone user message without a system prompt or tools still gets a breakpoint.
    /// </summary>
    [Fact]
    public async Task Request_WithOnlyUserMessage_MarksThatMessage()
    {
        var prompts = new List<ChatMessage> { new(ChatRole.User, "only-user") };

        _applyPromptCaching(prompts, new ChatOptions());
        var body = await SendAsync(prompts, new ChatOptions());

        Assert.Null(body["system"]);
        var content = Assert.IsType<JsonArray>(body["messages"][0]["content"]);
        Assert.Equal("ephemeral", (string)content[^1]["cache_control"]["type"]);
        Assert.Equal(1, CountOccurrences(body.ToJsonString(), "\"cache_control\""));
    }

    /// <summary>
    /// Verifies a lone system prompt is marked once.
    /// </summary>
    [Fact]
    public async Task Request_WithOnlySystemMessage_MarksSystemOnce()
    {
        var prompts = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
        };

        _applyPromptCaching(prompts, new ChatOptions());

        var content = Assert.IsType<TextContent>(Assert.Single(Assert.Single(prompts).Contents));
        Assert.True(content.AdditionalProperties?.ContainsKey("anthropic:cache_control"));

        prompts.Add(new ChatMessage(ChatRole.User, "user"));
        var body = await SendAsync(prompts, new ChatOptions());

        Assert.Equal(1, CountOccurrences(body.ToJsonString(), "\"cache_control\""));
    }

    /// <summary>
    /// Verifies the caller's conversation-history messages and tools are replaced by marked copies rather
    /// than modified in place, and that the wrapped tool still invokes the original.
    /// </summary>
    [Fact]
    public async Task ApplyPromptCaching_DoesNotModifyCallerMessagesOrTools()
    {
        var history = new ChatMessage(ChatRole.User, "current-user");
        var prompts = new List<ChatMessage>
        {
            new(ChatRole.System, "system"),
            history,
        };
        var tool = AIFunctionFactory.Create(() => "called", "second_tool");
        var options = new ChatOptions { Tools = [tool] };

        _applyPromptCaching(prompts, options);

        Assert.NotSame(history, prompts[1]);
        Assert.Null(history.Contents[0].AdditionalProperties);
        Assert.Equal("current-user", prompts[1].Text);
        Assert.Equal(ChatRole.User, prompts[1].Role);

        var wrapped = Assert.IsAssignableFrom<AIFunction>(Assert.Single(options.Tools));
        Assert.NotSame(tool, wrapped);
        Assert.False(tool.AdditionalProperties.ContainsKey("CacheControl"));
        Assert.True(wrapped.AdditionalProperties.ContainsKey("CacheControl"));
        Assert.Equal("second_tool", wrapped.Name);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal("called", result?.ToString());
    }

    private static List<ChatMessage> CreatePrompts()
    {
        return
        [
            new(ChatRole.System, "system"),
            new(ChatRole.User, "history-user"),
            new(ChatRole.Assistant, "history-assistant"),
            new(ChatRole.User, "current-user"),
        ];
    }

    private static ChatOptions CreateOptions()
    {
        return new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "first", "first_tool"),
                AIFunctionFactory.Create(() => "second", "second_tool"),
            ],
        };
    }

    private static async Task<JsonNode> SendAsync(List<ChatMessage> prompts, ChatOptions options)
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new AnthropicClient(new ClientOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://anthropic.test",
            HttpClient = httpClient,
            MaxRetries = 0,
        });

        using var chatClient = client.AsIChatClient("claude-test", 1024);

        await chatClient.GetResponseAsync(prompts, options, TestContext.Current.CancellationToken);

        return JsonNode.Parse(Assert.Single(handler.Bodies));
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static Action<List<ChatMessage>, ChatOptions> CreateApplyPromptCachingDelegate()
    {
        var method = typeof(ClaudeOrchestrator).GetMethod(
            "ApplyPromptCaching",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Unable to find the prompt-caching helper.");

        return method.CreateDelegate<Action<List<ChatMessage>, ChatOptions>>();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
