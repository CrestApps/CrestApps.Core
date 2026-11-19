using System.Security.Claims;
using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

#pragma warning disable MEAI001 // ApprovalRequiredAIFunction is a preview API of Microsoft.Extensions.AI.
namespace CrestApps.Core.Tests.Tooling;

/// <summary>
/// Covers a tool that requires a person's approval in a request that cannot ask for one: the tool never runs, and
/// the model reads why, so it can tell the user. The tools are registered the way a host registers them and reach
/// the model through the profile tool registry, the tool materializer and the function invocation handler.
/// </summary>
public sealed class ToolApprovalWithoutAskingTests
{
    [Fact]
    public async Task AChatThatCannotAsk_DoesNotRunAToolThatRequiresApproval_AndTheModelIsToldWhy()
    {
        using var host = new ToolHost();

        // An AI session without ToolApprovalSupported: the text-only chat stream, a realtime conversation, a
        // workflow, a channel such as SMS or voice, or a background task.
        var tools = await host.ConfigureAsync(supportsApproval: false, isChatInteraction: false, AskingTool.TheName);

        var tool = Assert.IsType<AIFunction>(Assert.Single(tools), exactMatch: false);
        Assert.Equal(AskingTool.TheName, tool.Name);
        Assert.Equal(host.Get<AskingTool>().JsonSchema.GetRawText(), tool.JsonSchema.GetRawText());

        var result = await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal(0, host.Get<AskingTool>().CallCount);
        Assert.Equal(ApprovalUnavailableAIFunction.GetMessage(AskingTool.TheName), result?.ToString());
    }

    [Fact]
    public async Task AChatInteraction_DoesNotRunAToolThatRequiresApproval()
    {
        using var host = new ToolHost();

        var tools = await host.ConfigureAsync(supportsApproval: false, isChatInteraction: true, AskingTool.TheName);

        var tool = Assert.IsType<ApprovalUnavailableAIFunction>(Assert.Single(tools));
        await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal(0, host.Get<AskingTool>().CallCount);
    }

    [Fact]
    public async Task AChatThatCanAsk_StillPausesForTheApproval()
    {
        using var host = new ToolHost();

        var tools = await host.ConfigureAsync(supportsApproval: true, isChatInteraction: false, AskingTool.TheName);

        var tool = Assert.IsType<ApprovalRequiredAIFunction>(Assert.Single(tools));
        Assert.Equal(AskingTool.TheName, tool.Name);
        Assert.Equal(0, host.Get<AskingTool>().CallCount);
    }

    [Fact]
    public async Task ADestructiveTool_RequiresApprovalByItsKind_AndDoesNotRunWhereNobodyCanBeAsked()
    {
        using var host = new ToolHost();

        var tools = await host.ConfigureAsync(supportsApproval: false, isChatInteraction: false, DeleteTool.TheName);

        var tool = Assert.IsType<ApprovalUnavailableAIFunction>(Assert.Single(tools));
        var result = await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal(0, host.Get<DeleteTool>().CallCount);
        Assert.Equal(ApprovalUnavailableAIFunction.GetMessage(DeleteTool.TheName), result?.ToString());
    }

    [Fact]
    public async Task AToolThatOptsOutWithRequireApprovalFalse_StillRunsWhereNobodyCanBeAsked()
    {
        using var host = new ToolHost();

        var tools = await host.ConfigureAsync(supportsApproval: false, isChatInteraction: false, OptedOutDeleteTool.TheName, ListTool.TheName);

        Assert.Equal(2, tools.Count);
        Assert.Same(host.Get<OptedOutDeleteTool>(), tools[0]);
        Assert.Same(host.Get<ListTool>(), tools[1]);

        await ((AIFunction)tools[0]).InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);
        await ((AIFunction)tools[1]).InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal(1, host.Get<OptedOutDeleteTool>().CallCount);
        Assert.Equal(1, host.Get<ListTool>().CallCount);
    }

    [Fact]
    public void AToolThatIsNotAFunction_IsLeftOutWhereNobodyCanBeAsked()
    {
        var tool = new HostedTool();
        var entry = new ToolRegistryEntry { Name = tool.Name, RequiresApproval = true };

        Assert.Null(DefaultToolMaterializer.WrapForApproval(tool, entry, ToolMaterializationOptions.Default));
        Assert.Same(tool, DefaultToolMaterializer.WrapForApproval(tool, entry, new ToolMaterializationOptions { SupportsToolApproval = true }));
    }

    /// <summary>
    /// A host with registered tools, the profile tool registry, the tool materializer and the function invocation
    /// handler, as the default orchestrator wires them for one request.
    /// </summary>
    private sealed class ToolHost : IDisposable
    {
        private readonly ServiceProvider _services;

        public ToolHost()
        {
            var services = new ServiceCollection();

            services.AddOptions();
            services.AddCoreAITool<AskingTool>(AskingTool.TheName).RequireApproval().Selectable();
            services.AddCoreAITool<DeleteTool>(DeleteTool.TheName).AsDestructive().Selectable();
            services.AddCoreAITool<OptedOutDeleteTool>(OptedOutDeleteTool.TheName).AsDestructive().RequireApproval(false).Selectable();
            services.AddCoreAITool<ListTool>(ListTool.TheName).AsRead().Selectable();

            _services = services.BuildServiceProvider();
        }

        public T Get<T>()
        {
            return _services.GetRequiredService<T>();
        }

        public async Task<IList<AITool>> ConfigureAsync(bool supportsApproval, bool isChatInteraction, params string[] toolNames)
        {
            var toolDefinitions = _services.GetRequiredService<IOptions<AIToolDefinitionOptions>>();
            var registry = new ProfileToolRegistryProvider(toolDefinitions);
            var completionContext = new AICompletionContext { ToolNames = toolNames };
            var entries = await registry.GetToolsAsync(completionContext, TestContext.Current.CancellationToken);

            completionContext.AdditionalProperties[FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey] = entries;

            if (supportsApproval)
            {
                completionContext.AdditionalProperties[AICompletionContextKeys.ToolApprovalSupported] = true;
            }

            if (isChatInteraction)
            {
                completionContext.AdditionalProperties[AICompletionContextKeys.Interaction] = new object();
            }

            var materializer = new DefaultToolMaterializer(
                new AllowAllToolAccessEvaluator(),
                toolDefinitions,
                _services,
                NullLogger<DefaultToolMaterializer>.Instance);
            var handler = new FunctionInvocationAICompletionServiceHandler(
                materializer,
                new AnonymousUserAccessor(),
                NullLogger<FunctionInvocationAICompletionServiceHandler>.Instance);
            var context = new CompletionServiceConfigureContext(new ChatOptions(), completionContext, true);

            await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

            return context.ChatOptions.Tools ?? [];
        }

        public void Dispose()
        {
            _services.Dispose();
        }
    }

    private sealed class AllowAllToolAccessEvaluator : IAIToolAccessEvaluator
    {
        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class AnonymousUserAccessor : IUserAccessor
    {
        public ClaimsPrincipal User { get; set; } = new(new ClaimsIdentity());
    }

    private abstract class CountingTool : AIFunction
    {
        private int _callCount;

        public int CallCount => _callCount;

        public override string Description => Name;

        public override JsonElement JsonSchema { get; } = JsonElement.Parse("""{"type":"object","properties":{"id":{"type":"integer"}}}""");

        protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);

            return ValueTask.FromResult<object>($"{Name} ran.");
        }
    }

    private sealed class AskingTool : CountingTool
    {
        public const string TheName = "archive_item";

        public override string Name => TheName;
    }

    private sealed class DeleteTool : CountingTool
    {
        public const string TheName = "delete_item";

        public override string Name => TheName;
    }

    private sealed class OptedOutDeleteTool : CountingTool
    {
        public const string TheName = "delete_draft";

        public override string Name => TheName;
    }

    private sealed class ListTool : CountingTool
    {
        public const string TheName = "list_items";

        public override string Name => TheName;
    }

    private sealed class HostedTool : AITool
    {
        public override string Name => "hosted_search";
    }
}
