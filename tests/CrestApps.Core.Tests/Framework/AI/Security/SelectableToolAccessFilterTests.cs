using System.Security.Claims;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class SelectableToolAccessFilterTests
{
    [Fact]
    public async Task GetAuthorizedSelectableTools_ExcludesDeniedAndNonSelectableTools()
    {
        var options = CreateToolDefinitions(services =>
        {
            services.AddCoreAITool<RegistrationTestTool>("allowed").Selectable();
            services.AddCoreAITool<RegistrationTestTool>("denied").Selectable();
            services.AddCoreAITool<RegistrationTestTool>("system-tool"); // System tool (not selectable).
            services.AddCoreAITool<RegistrationTestTool>("hidden-tool").Hidden();
        });
        var evaluator = new FakeToolAccessEvaluator { DeniedToolNames = { "denied" } };

        var authorized = await SelectableToolAccessFilter.GetAuthorizedSelectableToolsAsync(
            options, evaluator, CreateUser(), TestContext.Current.CancellationToken);

        // Only the selectable-and-authorized tool survives; the evaluator is consulted for selectable
        // tools only (system and hidden tools never reach it).
        Assert.Equal(["allowed"], authorized.Keys);
        Assert.Equal(["allowed", "denied"], evaluator.EvaluatedToolNames.OrderBy(n => n));
    }

    [Fact]
    public async Task GetAuthorizedSelectableToolNames_ReturnsOnlyAuthorizedNames()
    {
        var options = CreateToolDefinitions(services =>
        {
            services.AddCoreAITool<RegistrationTestTool>("allowed").Selectable();
            services.AddCoreAITool<RegistrationTestTool>("denied").Selectable();
        });
        var evaluator = new FakeToolAccessEvaluator { DeniedToolNames = { "denied" } };

        var names = await SelectableToolAccessFilter.GetAuthorizedSelectableToolNamesAsync(
            options, evaluator, CreateUser(), TestContext.Current.CancellationToken);

        Assert.Contains("allowed", names);
        Assert.DoesNotContain("denied", names);
    }

    [Fact]
    public async Task GetAuthorizedSelectableTools_WithDenyAllEvaluator_ReturnsEmpty()
    {
        var options = CreateToolDefinitions(services =>
            services.AddCoreAITool<RegistrationTestTool>("tool").Selectable());
        var evaluator = new FakeToolAccessEvaluator { DenyAll = true };

        var authorized = await SelectableToolAccessFilter.GetAuthorizedSelectableToolsAsync(
            options, evaluator, CreateUser(), TestContext.Current.CancellationToken);

        Assert.Empty(authorized);
    }

    private static ClaimsPrincipal CreateUser()
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "author")], "Test"));

    private static AIToolDefinitionOptions CreateToolDefinitions(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddOptions();
        configure(services);

        using var serviceProvider = services.BuildServiceProvider();

        return serviceProvider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;
    }

    private sealed class FakeToolAccessEvaluator : IAIToolAccessEvaluator
    {
        public List<string> EvaluatedToolNames { get; } = [];

        public HashSet<string> DeniedToolNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool DenyAll { get; set; }

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            EvaluatedToolNames.Add(toolName);

            return Task.FromResult(!DenyAll && !DeniedToolNames.Contains(toolName));
        }
    }

    private sealed class RegistrationTestTool : AIFunction
    {
        public override string Name => "registration-test-tool";

        public override string Description => Name;

        public override System.Text.Json.JsonElement JsonSchema
            => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}");

        protected override ValueTask<object> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
            => new(Name);
    }
}
