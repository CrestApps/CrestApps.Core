using CrestApps.Core.AI;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.Tests.Core.Realtime;

public sealed class RealtimeRagGuidanceHandlerTests
{
    [Fact]
    public async Task BuiltAsync_Realtime_WithInScopeDataSource_InjectsStrictGuidance()
    {
        var profile = new AIProfile();
        profile.Put(new AIDataSourceRagMetadata { IsInScope = true });
        var context = BuiltContext(profile, ExecutionMode: OrchestrationExecutionMode.Realtime, dataSourceId: "ds-1");
        var template = new RecordingTemplateService();

        await new RealtimeRagGuidanceHandler(template).BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(AITemplateIds.RagToolSearchStrict, template.LastId);
        Assert.Contains("GUIDANCE:" + AITemplateIds.RagToolSearchStrict, context.OrchestrationContext.SystemMessageBuilder.ToString());
    }

    [Fact]
    public async Task BuiltAsync_Realtime_WithoutInScope_InjectsRelaxedGuidance()
    {
        var context = BuiltContext(new AIProfile(), ExecutionMode: OrchestrationExecutionMode.Realtime, dataSourceId: "ds-1");
        var template = new RecordingTemplateService();

        await new RealtimeRagGuidanceHandler(template).BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(AITemplateIds.RagToolSearchRelaxed, template.LastId);
        Assert.Contains("GUIDANCE:" + AITemplateIds.RagToolSearchRelaxed, context.OrchestrationContext.SystemMessageBuilder.ToString());
    }

    [Fact]
    public async Task BuiltAsync_ChatMode_IsNoOp()
    {
        var context = BuiltContext(new AIProfile(), ExecutionMode: OrchestrationExecutionMode.Chat, dataSourceId: "ds-1");
        var template = new RecordingTemplateService();

        await new RealtimeRagGuidanceHandler(template).BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Null(template.LastId);
        Assert.Equal(0, context.OrchestrationContext.SystemMessageBuilder.Length);
    }

    [Fact]
    public async Task BuiltAsync_Realtime_WithoutDataSource_IsNoOp()
    {
        var context = BuiltContext(new AIProfile(), ExecutionMode: OrchestrationExecutionMode.Realtime, dataSourceId: null);
        var template = new RecordingTemplateService();

        await new RealtimeRagGuidanceHandler(template).BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Null(template.LastId);
        Assert.Equal(0, context.OrchestrationContext.SystemMessageBuilder.Length);
    }

    [Fact]
    public async Task BuiltAsync_Realtime_WithToolsDisabled_IsNoOp()
    {
        var context = BuiltContext(new AIProfile(), ExecutionMode: OrchestrationExecutionMode.Realtime, dataSourceId: "ds-1", disableTools: true);
        var template = new RecordingTemplateService();

        await new RealtimeRagGuidanceHandler(template).BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Null(template.LastId);
        Assert.Equal(0, context.OrchestrationContext.SystemMessageBuilder.Length);
    }

    private static OrchestrationContextBuiltContext BuiltContext(
        AIProfile profile,
        OrchestrationExecutionMode ExecutionMode,
        string dataSourceId,
        bool disableTools = false)
    {
        var context = new OrchestrationContext
        {
            ExecutionMode = ExecutionMode,
            DisableTools = disableTools,
            CompletionContext = new AICompletionContext
            {
                DataSourceId = dataSourceId,
                DisableTools = disableTools,
            },
        };

        return new OrchestrationContextBuiltContext(profile, context);
    }

    private sealed class RecordingTemplateService : ITemplateService
    {
        public string LastId { get; private set; }

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            LastId = id;

            return Task.FromResult("GUIDANCE:" + id);
        }

        public Task<IReadOnlyList<CrestApps.Core.Templates.Models.Template>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CrestApps.Core.Templates.Models.Template>>([]);

        public Task<CrestApps.Core.Templates.Models.Template> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<CrestApps.Core.Templates.Models.Template>(null);

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
