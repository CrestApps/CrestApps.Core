using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Tooling;

public sealed class AIToolKindTests
{
    [Fact]
    public void AToolWithoutAKind_BehavesAsBefore()
    {
        var options = CreateOptions(services => services.AddCoreAITool<TestTool>("plain").Selectable());

        var entry = options.Tools["plain"];

        Assert.Equal(AIToolKind.Unspecified, entry.Kind);
        Assert.False(entry.RequiresApproval);
        Assert.True(entry.Kind.IsExposedWithAllTools());
    }

    [Theory]
    [InlineData(AIToolKind.Unspecified, false, true)]
    [InlineData(AIToolKind.Read, false, true)]
    [InlineData(AIToolKind.Write, false, true)]
    [InlineData(AIToolKind.Destructive, true, false)]
    [InlineData(AIToolKind.External, true, true)]
    [InlineData(AIToolKind.Security, true, false)]
    public void AKind_SaysWhetherItNeedsApprovalAndWhetherExposingAllToolsIncludesIt(AIToolKind kind, bool requiresApproval, bool exposedWithAllTools)
    {
        Assert.Equal(requiresApproval, kind.RequiresApproval());
        Assert.Equal(exposedWithAllTools, kind.IsExposedWithAllTools());
    }

    [Fact]
    public void TheBuilderMethods_SetTheKind_AndTheKindImpliesApproval()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("list").AsRead();
            services.AddCoreAITool<TestTool>("save").AsWrite();
            services.AddCoreAITool<TestTool>("delete").AsDestructive();
            services.AddCoreAITool<TestTool>("send").AsExternal();
            services.AddCoreAITool<TestTool>("grant").AsSecurity();
            services.AddCoreAITool<TestTool>("other").WithKind(AIToolKind.Write);
        });

        Assert.Equal(AIToolKind.Read, options.Tools["list"].Kind);
        Assert.Equal(AIToolKind.Write, options.Tools["save"].Kind);
        Assert.Equal(AIToolKind.Destructive, options.Tools["delete"].Kind);
        Assert.Equal(AIToolKind.External, options.Tools["send"].Kind);
        Assert.Equal(AIToolKind.Security, options.Tools["grant"].Kind);
        Assert.Equal(AIToolKind.Write, options.Tools["other"].Kind);

        Assert.False(options.Tools["list"].RequiresApproval);
        Assert.False(options.Tools["save"].RequiresApproval);
        Assert.True(options.Tools["delete"].RequiresApproval);
        Assert.True(options.Tools["send"].RequiresApproval);
        Assert.True(options.Tools["grant"].RequiresApproval);
    }

    [Fact]
    public void RequireApproval_WinsOverTheKind_WhicheverIsSetFirst()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("write_asks").AsWrite().RequireApproval();
            services.AddCoreAITool<TestTool>("asks_then_write").RequireApproval().AsWrite();
            services.AddCoreAITool<TestTool>("delete_does_not_ask").AsDestructive().RequireApproval(false);
            services.AddCoreAITool<TestTool>("does_not_ask_then_delete").RequireApproval(false).AsDestructive();
        });

        Assert.True(options.Tools["write_asks"].RequiresApproval);
        Assert.True(options.Tools["asks_then_write"].RequiresApproval);
        Assert.False(options.Tools["delete_does_not_ask"].RequiresApproval);
        Assert.False(options.Tools["does_not_ask_then_delete"].RequiresApproval);
    }

    [Fact]
    public async Task TheKind_ReachesTheRegistryEntry_WithTheApprovalItImplies()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("delete").AsDestructive().Selectable();
            services.AddCoreAITool<TestTool>("list").AsRead().Selectable();
            services.AddCoreAITool<TestTool>("plain").Selectable();
        });
        var provider = new ProfileToolRegistryProvider(Options.Create(options));

        var entries = await provider.GetToolsAsync(
            new AICompletionContext { ToolNames = ["delete", "list", "plain"] },
            TestContext.Current.CancellationToken);

        var delete = Assert.Single(entries, entry => entry.Name == "delete");
        var list = Assert.Single(entries, entry => entry.Name == "list");
        var plain = Assert.Single(entries, entry => entry.Name == "plain");

        Assert.Equal(AIToolKind.Destructive, delete.Kind);
        Assert.True(delete.RequiresApproval);
        Assert.Equal(AIToolKind.Read, list.Kind);
        Assert.False(list.RequiresApproval);
        Assert.Equal(AIToolKind.Unspecified, plain.Kind);
        Assert.False(plain.RequiresApproval);
    }

    [Fact]
    public async Task TheKind_ReachesTheRegistryEntryOfASystemTool()
    {
        var options = CreateOptions(services => services.AddCoreAITool<TestTool>("purge").AsDestructive());
        var provider = new SystemToolRegistryProvider(Options.Create(options));

        var entry = Assert.Single(await provider.GetToolsAsync(new AICompletionContext(), TestContext.Current.CancellationToken));

        Assert.Equal(AIToolKind.Destructive, entry.Kind);
        Assert.True(entry.RequiresApproval);
    }

    private static AIToolDefinitionOptions CreateOptions(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();

        services.AddOptions();

        configureServices(services);

        using var serviceProvider = services.BuildServiceProvider();

        return serviceProvider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;
    }

    private sealed class TestTool : AITool
    {
        public override string Name => "test_tool";

        public override string Description => "Test tool";

        public override IReadOnlyDictionary<string, object> AdditionalProperties => new Dictionary<string, object>();
    }
}
