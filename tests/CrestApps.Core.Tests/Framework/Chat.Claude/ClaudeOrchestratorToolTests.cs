using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Chat.Claude;

/// <summary>
/// Verifies the <see cref="ClaudeOrchestrator"/> sends the scoped tools to Anthropic. The orchestrator calls the
/// Anthropic chat client directly rather than through the AI completion service, so it materializes the tools
/// itself instead of relying on the function-invocation completion handler.
/// </summary>
public sealed class ClaudeOrchestratorToolTests
{
    private const string StreamingResponse = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_test","type":"message","role":"assistant","model":"claude-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ok"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":1}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    /// <summary>
    /// Verifies the request body carries every scoped tool, with the cache breakpoint on the last one.
    /// </summary>
    [Fact]
    public async Task ExecuteStreamingAsync_WithScopedTools_SendsToolsWithCacheBreakpointOnLast()
    {
        var harness = new Harness(["first_tool", "second_tool"]);

        await harness.RunAsync(new AICompletionContext());

        var tools = Assert.IsType<JsonArray>(harness.Body["tools"]);
        Assert.Equal(2, tools.Count);
        Assert.Equal("first_tool", (string)tools[0]["name"]);
        Assert.Equal("second_tool", (string)tools[1]["name"]);
        Assert.Null(tools[0]["cache_control"]);
        Assert.Equal("ephemeral", (string)tools[1]["cache_control"]["type"]);
    }

    /// <summary>
    /// Verifies the tools are sent without a cache breakpoint when prompt caching is turned off.
    /// </summary>
    [Fact]
    public async Task ExecuteStreamingAsync_WithPromptCachingOff_SendsToolsWithoutCacheControl()
    {
        var harness = new Harness(["first_tool"], enablePromptCaching: false);

        await harness.RunAsync(new AICompletionContext());

        var tool = Assert.Single(Assert.IsType<JsonArray>(harness.Body["tools"]));
        Assert.Equal("first_tool", (string)tool["name"]);
        Assert.DoesNotContain("cache_control", harness.Body.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies no tools are sent when the completion context turns tools off.
    /// </summary>
    [Fact]
    public async Task ExecuteStreamingAsync_WithToolsDisabled_SendsNoTools()
    {
        var harness = new Harness(["first_tool"]);

        await harness.RunAsync(new AICompletionContext { DisableTools = true });

        Assert.Null(harness.Body["tools"]);
        harness.Materializer.Verify(
            materializer => materializer.MaterializeAsync(
                It.IsAny<IReadOnlyList<ToolRegistryEntry>>(),
                It.IsAny<ToolMaterializationOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies an AI Session request skips the per-user listable-access check.
    /// </summary>
    [Fact]
    public async Task ExecuteStreamingAsync_ForSession_DoesNotEnforceListableAccess()
    {
        var harness = new Harness(["first_tool"]);

        await harness.RunAsync(new AICompletionContext());

        Assert.False(harness.MaterializationOptions.EnforceListableAccess);
        Assert.Null(harness.MaterializationOptions.User);
    }

    /// <summary>
    /// Verifies a Chat Interaction request checks listable tools against the current user, and only the tools the
    /// materializer allows are sent.
    /// </summary>
    [Fact]
    public async Task ExecuteStreamingAsync_ForChatInteraction_EnforcesListableAccessForCurrentUser()
    {
        var harness = new Harness(["allowed_tool", "denied_tool"], deniedToolNames: ["denied_tool"]);
        var context = new AICompletionContext();
        context.AdditionalProperties[AICompletionContextKeys.Interaction] = new object();

        await harness.RunAsync(context);

        Assert.True(harness.MaterializationOptions.EnforceListableAccess);
        Assert.Same(harness.User, harness.MaterializationOptions.User);

        var tool = Assert.Single(Assert.IsType<JsonArray>(harness.Body["tools"]));
        Assert.Equal("allowed_tool", (string)tool["name"]);
    }

    private sealed class Harness
    {
        private readonly string[] _toolNames;
        private readonly string[] _deniedToolNames;
        private readonly bool _enablePromptCaching;
        private readonly CapturingHandler _handler = new();

        public Harness(string[] toolNames, bool enablePromptCaching = true, string[] deniedToolNames = null)
        {
            _toolNames = toolNames;
            _deniedToolNames = deniedToolNames ?? [];
            _enablePromptCaching = enablePromptCaching;

            Materializer = new Mock<IToolMaterializer>();
            Materializer
                .Setup(materializer => materializer.MaterializeAsync(
                    It.IsAny<IReadOnlyList<ToolRegistryEntry>>(),
                    It.IsAny<ToolMaterializationOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<ToolRegistryEntry> entries, ToolMaterializationOptions options, CancellationToken _) =>
                {
                    MaterializationOptions = options;

                    return new ToolMaterializationResult
                    {
                        Tools = entries
                            .Where(entry => !_deniedToolNames.Contains(entry.Name))
                            .Select(entry => (AITool)AIFunctionFactory.Create(() => "result", entry.Name, entry.Description))
                            .ToList(),
                        DeniedToolNames = _deniedToolNames,
                    };
                });
        }

        public Mock<IToolMaterializer> Materializer { get; }

        public ToolMaterializationOptions MaterializationOptions { get; private set; }

        public ClaimsPrincipal User { get; } = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "test"));

        public JsonNode Body { get; private set; }

        public async Task RunAsync(AICompletionContext completionContext)
        {
            var entries = _toolNames
                .Select(name => new ToolRegistryEntry
                {
                    Id = name,
                    Name = name,
                    Description = $"{name} description",
                    Source = ToolRegistryEntrySource.Local,
                })
                .ToArray();

            var toolRegistry = new Mock<IToolRegistry>();
            toolRegistry
                .Setup(registry => registry.GetAllAsync(It.IsAny<AICompletionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(completionContext.DisableTools ? [] : entries);

            var userAccessor = new Mock<IUserAccessor>();
            userAccessor.SetupGet(accessor => accessor.User).Returns(User);

            var claudeOptions = Mock.Of<IOptionsSnapshot<ClaudeOptions>>(snapshot => snapshot.Value == new ClaudeOptions
            {
                ApiKey = "test-key",
                BaseUrl = "https://anthropic.test",
                DefaultModel = "claude-test",
                EnablePromptCaching = _enablePromptCaching,
            });

            using var httpClient = new HttpClient(_handler);
            var clientService = new ClaudeClientService(claudeOptions, NullLogger<ClaudeClientService>.Instance, httpClient);

            var orchestrator = new ClaudeOrchestrator(
                toolRegistry.Object,
                Materializer.Object,
                userAccessor.Object,
                clientService,
                claudeOptions,
                Options.Create(new DefaultAIOptions()),
                NullLoggerFactory.Instance,
                NullLogger<ClaudeOrchestrator>.Instance);

            await using var serviceProvider = new ServiceCollection().BuildServiceProvider();

            var context = new OrchestrationContext
            {
                UserMessage = "hello",
                CompletionContext = completionContext,
                ServiceProvider = serviceProvider,
            };

            await foreach (var _ in orchestrator.ExecuteStreamingAsync(context, TestContext.Current.CancellationToken))
            {
            }

            Body = JsonNode.Parse(Assert.Single(_handler.Bodies));
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(StreamingResponse, Encoding.UTF8, "text/event-stream"),
            };
        }
    }
}
