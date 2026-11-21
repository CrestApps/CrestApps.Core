using System.Security.Claims;
using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Tooling;

public sealed class AIToolAliasTests
{
    [Fact]
    public void AnAlias_ResolvesToTheTool_AndListingsShowTheToolOnce()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("listUsers")
                .WithAlias("searchForUsers")
                .WithAliases("findUsers", "listUsers")
                .Selectable();
        });

        Assert.Equal(["listUsers"], options.Tools.Keys);
        Assert.Equal(["listUsers"], options.GetSelectableTools().Keys);
        Assert.Equal(["searchForUsers", "findUsers"], options.Tools["listUsers"].Aliases);

        Assert.True(options.TryGetTool("searchForUsers", out var byAlias));
        Assert.True(options.TryGetTool("FINDUSERS", out var byOtherAlias));
        Assert.True(options.TryGetTool("listUsers", out var byName));
        Assert.Same(byName, byAlias);
        Assert.Same(byName, byOtherAlias);
        Assert.False(options.TryGetTool("unknown", out _));
        Assert.False(options.TryGetTool(null, out _));

        Assert.Equal("listUsers", options.ResolveToolName("searchForUsers"));
        Assert.Equal("listUsers", options.ResolveToolName("listUsers"));
        Assert.Equal("unknown", options.ResolveToolName("unknown"));
        Assert.True(byName.IsKnownAs("searchforusers"));
        Assert.False(byName.IsKnownAs("unknown"));
    }

    [Fact]
    public void ResolveToolNames_ReplacesAliases_AndRemovesDuplicates()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("searchForUsers");
            services.AddCoreAITool<TestTool>("getUser");
        });

        var names = options.ResolveToolNames(["searchForUsers", " ", "getUser", "listUsers", "notRegistered", null]);

        Assert.Equal(["listUsers", "getUser", "notRegistered"], names);
    }

    [Fact]
    public void ExpandToolNames_ResolvesAnAliasInTheRequestedNamesAndInADependency()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("saveUser").WithDependency("searchForUsers");
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("searchForUsers");
        });

        Assert.Equal(["saveUser", "listUsers"], options.ExpandToolNames(["saveUser"]));
        Assert.Equal(["listUsers"], options.ExpandToolNames(["searchForUsers", "listUsers"]));
    }

    [Fact]
    public void AnAliasThatIsAnotherToolsName_FailsWithAMessageThatNamesBothTools()
    {
        var aliasFirst = Assert.Throws<InvalidOperationException>(() => CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("getUser");
            services.AddCoreAITool<TestTool>("getUser");
        }));
        var nameFirst = Assert.Throws<InvalidOperationException>(() => CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("getUser");
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("GETUSER");
        }));

        Assert.Contains("'getUser'", aliasFirst.Message);
        Assert.Contains("'listUsers'", aliasFirst.Message);
        Assert.Contains("'GETUSER'", nameFirst.Message);
        Assert.Contains("'listUsers'", nameFirst.Message);
    }

    [Fact]
    public void AnAliasThatIsAnotherToolsAlias_FailsWithAMessageThatNamesBothTools()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("searchForUsers");
            services.AddCoreAITool<TestTool>("findPeople").WithAlias("searchForUsers");
        }));

        Assert.Contains("'searchForUsers'", exception.Message);
        Assert.Contains("'listUsers'", exception.Message);
        Assert.Contains("'findPeople'", exception.Message);
    }

    [Fact]
    public void RegisteringAToolAgain_ReplacesItsAliases()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("searchForUsers");
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("findUsers");
        });

        Assert.False(options.TryGetTool("searchForUsers", out _));
        Assert.True(options.TryGetTool("findUsers", out _));
    }

    [Fact]
    public void AnEmptyAlias_IsRejected()
    {
        var services = new ServiceCollection();
        var builder = services.AddCoreAITool<TestTool>("listUsers");

        Assert.Throws<ArgumentException>(() => builder.WithAlias(" "));
        Assert.Throws<ArgumentNullException>(() => builder.WithAlias(null));
    }

    [Fact]
    public async Task ANameStoredBeforeTheRename_GivesTheProfileTheToolUnderItsCurrentName()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("searchForUsers").AsRead().Selectable();
        });
        var provider = new ProfileToolRegistryProvider(Options.Create(options));

        var entries = await provider.GetToolsAsync(
            new AICompletionContext { ToolNames = ["searchForUsers", "listUsers"] },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("listUsers", entry.Name);
        Assert.Equal("listUsers", entry.Id);
        Assert.Equal(AIToolKind.Read, entry.Kind);
    }

    [Fact]
    public async Task TheToolRegistry_ResolvesADependencyDeclaredUnderAnAlias()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("saveUser").WithDependency("searchForUsers").Selectable();
            services.AddCoreAITool<TestTool>("listUsers").WithAlias("searchForUsers").Selectable();
        });
        var registry = new DefaultToolRegistry(
            [new ProfileToolRegistryProvider(Options.Create(options))],
            Options.Create(options),
            new LuceneTextTokenizer(),
            NullLogger<DefaultToolRegistry>.Instance);
        var context = new AICompletionContext { ToolNames = ["saveUser", "searchForUsers"] };

        var entries = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["saveUser", "listUsers"], entries.Select(entry => entry.Name));
        Assert.Equal(["listUsers"], Assert.IsType<string[]>(context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames]));
    }

    [Fact]
    public async Task AccessGrantedForAnEarlierName_StillLetsTheCallerUseTheRenamedTool()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestFunction>("listUsers").WithAlias("searchForUsers", grantsAccess: true).Selectable();
            services.AddCoreAITool<TestFunction>("getUser").Selectable();
        });
        var evaluator = new NamedToolAccessEvaluator("searchForUsers");
        var listUsers = new TestFunction();
        var materializer = new DefaultToolMaterializer(
            evaluator,
            Options.Create(options),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<DefaultToolMaterializer>.Instance);
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            new() { Id = "listUsers", Name = "listUsers", CreateAsync = _ => new ValueTask<AITool>(listUsers) },
            new() { Id = "getUser", Name = "getUser", CreateAsync = _ => new ValueTask<AITool>(new TestFunction()) },
        ];

        var result = await materializer.MaterializeAsync(
            entries,
            new ToolMaterializationOptions
            {
                EnforceListableAccess = true,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "editor")], "Test")),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal([listUsers], result.Tools);
        Assert.Equal(["getUser"], result.DeniedToolNames);
        Assert.Equal(["listUsers", "searchForUsers", "getUser"], evaluator.CheckedNames);
    }

    [Fact]
    public async Task AnAliasWithoutAccess_ResolvesTheName_ButDoesNotGrantTheEarlierToolsAccess()
    {
        // A read-only tool merged into one that can also cancel orders: its grants must not reach the broader tool.
        var options = CreateOptions(services => services.AddCoreAITool<TestTool>("manageOrder").WithAlias("getOrder"));
        var definition = options.Tools["manageOrder"];
        var readOnlyGrant = new NamedToolAccessEvaluator("getOrder");

        Assert.Equal("manageOrder", options.ResolveToolName("getOrder"));
        Assert.Empty(definition.AccessAliases);
        Assert.False(await readOnlyGrant.IsAuthorizedAsync(user: null, "manageOrder", definition));
        Assert.Equal(["manageOrder"], readOnlyGrant.CheckedNames);
    }

    [Fact]
    public async Task TheAccessEvaluatorExtension_ChecksTheNameFirst_AndOnlyThenTheAliasesThatGrantAccess()
    {
        var options = CreateOptions(services => services.AddCoreAITool<TestTool>("listUsers")
            .WithAlias("searchForUsers", grantsAccess: true)
            .WithAlias("findUsers"));
        var definition = options.Tools["listUsers"];
        var byName = new NamedToolAccessEvaluator("listUsers");
        var byNothing = new NamedToolAccessEvaluator();

        Assert.True(await byName.IsAuthorizedAsync(user: null, "listUsers", definition));
        Assert.Equal(["listUsers"], byName.CheckedNames);
        Assert.False(await byNothing.IsAuthorizedAsync(user: null, "listUsers", definition));
        Assert.Equal(["listUsers", "searchForUsers"], byNothing.CheckedNames);
        Assert.False(await byNothing.IsAuthorizedAsync(user: null, "other", definition: null));
    }

    [Fact]
    public async Task TheToolsService_GetsARenamedToolByItsEarlierName()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddCoreAITool<TestFunction>("listUsers").WithAlias("searchForUsers");

        await using var serviceProvider = services.BuildServiceProvider();
        var toolsService = new DefaultAIToolsService(serviceProvider, serviceProvider.GetRequiredService<IOptions<AIToolDefinitionOptions>>());

        Assert.Same(serviceProvider.GetRequiredService<TestFunction>(), await toolsService.GetByNameAsync("searchForUsers"));
        Assert.Same(serviceProvider.GetRequiredService<TestFunction>(), await toolsService.GetByNameAsync("listUsers"));
        Assert.Null(await toolsService.GetByNameAsync("unknownTool"));
    }

    private static AIToolDefinitionOptions CreateOptions(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();

        services.AddOptions();

        configureServices(services);

        using var serviceProvider = services.BuildServiceProvider();

        return serviceProvider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;
    }

    private sealed class NamedToolAccessEvaluator : IAIToolAccessEvaluator
    {
        private readonly HashSet<string> _authorizedNames;

        public NamedToolAccessEvaluator(params string[] authorizedNames)
        {
            _authorizedNames = new HashSet<string>(authorizedNames, StringComparer.OrdinalIgnoreCase);
        }

        public List<string> CheckedNames { get; } = [];

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            CheckedNames.Add(toolName);

            return Task.FromResult(_authorizedNames.Contains(toolName));
        }
    }

    private sealed class TestTool : AITool
    {
        public override string Name => "test_tool";

        public override string Description => "Test tool";

        public override IReadOnlyDictionary<string, object> AdditionalProperties => new Dictionary<string, object>();
    }

    private sealed class TestFunction : AIFunction
    {
        public override string Name => "test_function";

        public override string Description => "Test function";

        public override JsonElement JsonSchema => JsonSerializer.Deserialize<JsonElement>("{}");

        protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            return new ValueTask<object>("done");
        }
    }
}
