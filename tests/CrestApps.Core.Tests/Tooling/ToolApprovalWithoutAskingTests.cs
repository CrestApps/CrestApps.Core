using System.Diagnostics.Metrics;
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
using Microsoft.Extensions.Logging;
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
    public void AToolThatRequiresApprovalButIsNotAFunction_IsLeftOut_EvenWhereAPersonCanBeAsked()
    {
        var tool = new HostedTool();
        var entry = new ToolRegistryEntry { Name = tool.Name, RequiresApproval = true };

        // No approval can be asked for a tool the function invocation does not run, so it is never offered.
        Assert.Null(DefaultToolMaterializer.WrapForApproval(tool, entry, ToolMaterializationOptions.Default));
        Assert.Null(DefaultToolMaterializer.WrapForApproval(tool, entry, new ToolMaterializationOptions { SupportsToolApproval = true }));

        // Without an approval to ask for, it is offered as before.
        Assert.Same(tool, DefaultToolMaterializer.WrapForApproval(tool, new ToolRegistryEntry { Name = tool.Name }, new ToolMaterializationOptions { SupportsToolApproval = true }));
    }

    [Fact]
    public async Task AProfileSetToRunWithoutAsking_RunsAToolThatRequiresApprovalByItsKind_WhereNobodyCanBeAsked()
    {
        using var host = new ToolHost { RunToolsWithoutAsking = true };

        var tools = await host.ConfigureAsync(supportsApproval: false, isChatInteraction: false, DeleteTool.TheName);

        var tool = Assert.IsType<AutoApprovedAIFunction>(Assert.Single(tools));
        await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal(1, host.Get<DeleteTool>().CallCount);
    }

    [Fact]
    public async Task AProfileSetToRunWithoutAsking_DoesNotPause_WhereAPersonCanBeAsked()
    {
        using var host = new ToolHost { RunToolsWithoutAsking = true };

        var tools = await host.ConfigureAsync(supportsApproval: true, isChatInteraction: false, DeleteTool.TheName);

        Assert.IsType<AutoApprovedAIFunction>(Assert.Single(tools));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AProfileSetToRunWithoutAsking_NeverLowersAnExplicitRequireApproval(bool supportsApproval)
    {
        using var host = new ToolHost { RunToolsWithoutAsking = true };

        // The tool's author registered it with .RequireApproval(): that is its floor.
        var tools = await host.ConfigureAsync(supportsApproval, isChatInteraction: false, AskingTool.TheName);
        var tool = Assert.Single(tools);

        if (supportsApproval)
        {
            Assert.IsType<ApprovalRequiredAIFunction>(tool);
        }
        else
        {
            Assert.IsType<ApprovalUnavailableAIFunction>(tool);
            await ((AIFunction)tool).InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, host.Get<AskingTool>().CallCount);
    }

    [Fact]
    public async Task ACallThatRunsWithoutAsking_IsLoggedAtInformation_AndCounted()
    {
        var logger = new CapturingLogger();
        using var host = new ToolHost { RunToolsWithoutAsking = true, Logger = logger };

        var tool = Assert.IsType<AutoApprovedAIFunction>(Assert.Single(
            await host.ConfigureAsync(supportsApproval: false, isChatInteraction: false, DeleteTool.TheName)));

        var measurements = new List<KeyValuePair<string, object>[]>();
        var inThisCall = new AsyncLocal<bool>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument == AITelemetry.ToolAutoApprovals)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            // Other tests run at the same time; only this test's call is collected.
            if (inThisCall.Value)
            {
                measurements.Add(tags.ToArray());
            }
        });
        listener.Start();

        inThisCall.Value = true;
        await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);
        inThisCall.Value = false;

        // One Information line for the call: offering the tool logs nothing.
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(DeleteTool.TheName, entry.Message);

        var tags = Assert.Single(measurements);
        Assert.Contains(tags, tag => tag.Key == AITelemetry.ReasonTag && Equals(tag.Value, "policy"));
        Assert.Contains(tags, tag => tag.Key == AITelemetry.ToolKindTag && Equals(tag.Value, nameof(AIToolKind.Destructive)));
        Assert.Contains(tags, tag => tag.Key == AITelemetry.AgentTag && Equals(tag.Value, "false"));
        Assert.Equal(1, host.Get<DeleteTool>().CallCount);
    }

    [Fact]
    public void ARegisteredTool_HasAnApprovalFloor_OnlyWhenItsRegistrationRequiresApprovalExplicitly()
    {
        using var host = new ToolHost();
        var definitions = host.Get<IOptions<AIToolDefinitionOptions>>().Value.Tools;

        Assert.True(definitions[AskingTool.TheName].RequiresApprovalExplicit);
        Assert.Null(definitions[DeleteTool.TheName].RequiresApprovalExplicit);
        Assert.False(definitions[OptedOutDeleteTool.TheName].RequiresApprovalExplicit);
    }

    [Fact]
    public async Task AProfileSetToRunWithoutAsking_StillOffersOnlyTheToolsTheCallerMayUse()
    {
        using var host = new ToolHost
        {
            RunToolsWithoutAsking = true,
            AccessEvaluator = new DenyAllToolAccessEvaluator(),
        };

        // Only the approval is skipped: a caller that may not use the tool still does not get it.
        var tools = await host.ConfigureAsync(supportsApproval: false, isChatInteraction: true, DeleteTool.TheName);

        Assert.Empty(tools);
        Assert.Equal(0, host.Get<DeleteTool>().CallCount);
    }

    [Fact]
    public void WrapForApproval_WithAnAllowAllPolicy_AuditsKindBasedTools_AndKeepsFloors()
    {
        var function = new ListTool();
        var hosted = new HostedTool();
        var policy = CreateAllowAllPolicy();
        var options = new ToolMaterializationOptions { ApprovalPolicy = policy, SupportsToolApproval = true };
        var byKind = new ToolRegistryEntry { Name = function.Name, RequiresApproval = true, Kind = AIToolKind.External };

        // Approval from the kind only: runs without asking, wrapped so the call is audited.
        Assert.IsType<AutoApprovedAIFunction>(DefaultToolMaterializer.WrapForApproval(function, byKind, options));

        // An explicit .RequireApproval(), or a consequential page tool: the floor stays.
        var floored = new ToolRegistryEntry { Name = function.Name, RequiresApproval = true, Kind = AIToolKind.External, ApprovalFloor = ToolApprovalMode.EveryCall };
        Assert.IsType<ApprovalRequiredAIFunction>(DefaultToolMaterializer.WrapForApproval(function, floored, options));

        // A kind the policy does not cover still asks.
        var otherKind = new ToolRegistryEntry { Name = function.Name, RequiresApproval = true, Kind = AIToolKind.Unspecified };
        Assert.IsType<ApprovalRequiredAIFunction>(DefaultToolMaterializer.WrapForApproval(function, otherKind, options));

        // A tool that is not a function cannot be audited, so it is still left out.
        Assert.Null(DefaultToolMaterializer.WrapForApproval(hosted, new ToolRegistryEntry { Name = hosted.Name, RequiresApproval = true, Kind = AIToolKind.External }, options));
    }

    [Fact]
    public void ToolApprovalPolicy_ForAgent_TheStricterSideWins()
    {
        var allowAll = CreateAllowAllPolicy();
        var settings = new AIToolApprovalSettings();
        settings.Kinds[nameof(AIToolKind.External)] = new ToolApprovalRule { Mode = ToolApprovalMode.Never };
        var externalOnly = ToolApprovalPolicy.FromSettings(settings);

        Assert.Null(ToolApprovalPolicy.ForAgent(null, allowAll));
        Assert.Null(ToolApprovalPolicy.ForAgent(allowAll, null));
        Assert.Equal([AIToolKind.External], ToolApprovalPolicy.ForAgent(allowAll, externalOnly).KindsRunWithoutAsking);
        Assert.Equal([AIToolKind.External], ToolApprovalPolicy.ForAgent(externalOnly, allowAll).KindsRunWithoutAsking);
    }

    [Fact]
    public void ToolApprovalPolicy_FromSettings_OnlyNeverRunsWithoutAsking()
    {
        var settings = new AIToolApprovalSettings();
        settings.Kinds[nameof(AIToolKind.Destructive)] = new ToolApprovalRule { Mode = ToolApprovalMode.OncePerSession };
        settings.Kinds[nameof(AIToolKind.External)] = new ToolApprovalRule { Mode = ToolApprovalMode.EveryCall };
        settings.Kinds["NotAKind"] = new ToolApprovalRule { Mode = ToolApprovalMode.Never };

        // OncePerSession asks every call until session grants exist, and unknown keys are ignored.
        Assert.Null(ToolApprovalPolicy.FromSettings(settings));
        Assert.Null(ToolApprovalPolicy.FromSettings(null));
    }

    [Fact]
    public void TheEditorAndThePipeline_ReadKindKeysTheSameWay_InAnyCasing()
    {
        // A recipe or hand-edited JSON with lowercase keys, in a dictionary without the case-insensitive comparer.
        var settings = new AIToolApprovalSettings
        {
            Kinds = new Dictionary<string, ToolApprovalRule>
            {
                ["destructive"] = new() { Mode = ToolApprovalMode.Never },
                ["external"] = new() { Mode = ToolApprovalMode.Never },
                ["security"] = new() { Mode = ToolApprovalMode.Never },
            },
        };

        Assert.True(settings.RunsAllWithoutAsking());
        Assert.NotNull(ToolApprovalPolicy.FromSettings(settings));

        // Saving Ask removes those rules too, whatever their casing.
        settings.SetRunAllWithoutAsking(false);

        Assert.Empty(settings.Kinds);
        Assert.False(settings.RunsAllWithoutAsking());
        Assert.Null(ToolApprovalPolicy.FromSettings(settings));
    }

    private static ToolApprovalPolicy CreateAllowAllPolicy()
    {
        var settings = new AIToolApprovalSettings();
        settings.SetRunAllWithoutAsking(true);

        return ToolApprovalPolicy.FromSettings(settings);
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

        /// <summary>
        /// Gets or sets a value indicating whether the profile was set to run its tools without asking.
        /// </summary>
        public bool RunToolsWithoutAsking { get; set; }

        /// <summary>
        /// Gets or sets the evaluator that says which tools the caller of a Chat Interaction may use.
        /// </summary>
        public IAIToolAccessEvaluator AccessEvaluator { get; set; } = new AllowAllToolAccessEvaluator();

        /// <summary>
        /// Gets or sets the materializer's logger, which the calls that run without asking are logged to.
        /// </summary>
        public ILogger<DefaultToolMaterializer> Logger { get; set; } = NullLogger<DefaultToolMaterializer>.Instance;

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

            if (RunToolsWithoutAsking)
            {
                completionContext.SetToolApprovalPolicy(CreateAllowAllPolicy());
            }

            var materializer = new DefaultToolMaterializer(
                AccessEvaluator,
                toolDefinitions,
                _services,
                Logger);
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

    private sealed class CapturingLogger : ILogger<DefaultToolMaterializer>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class DenyAllToolAccessEvaluator : IAIToolAccessEvaluator
    {
        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            return Task.FromResult(false);
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
