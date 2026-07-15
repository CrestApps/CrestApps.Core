using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DefaultToolRegistryDependencyTests
{
    [Fact]
    public async Task GetAllAsync_Dependencies_UseDepthFirstPreOrderBeforeLaterRoots()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "root", "dependency-a", "dependency-b");
        AddDefinition(options, "dependency-a", "nested");
        AddDefinition(options, "dependency-b");
        AddDefinition(options, "nested");
        var context = new AICompletionContext();
        var registry = CreateRegistry(
            options,
            [
                new RecordingToolRegistryProvider(
                [
                    CreateEntry("root"),
                    CreateEntry("unrelated"),
                    CreateEntry("dependency-b"),
                    CreateEntry("nested"),
                    CreateEntry("dependency-a"),
                ]),
            ]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["root", "dependency-a", "nested", "dependency-b", "unrelated"],
            result.Select(entry => entry.Name));
        Assert.Equal(
            ["dependency-a", "nested", "dependency-b"],
            GetDependencyNames(context));
    }

    [Fact]
    public async Task GetAllAsync_DuplicateDependencyNames_ExpandsEveryDistinctEntryInEncounterOrder()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "root", "shared");
        AddDefinition(options, "shared");
        var firstShared = CreateEntry("shared", "shared-first");
        var secondShared = CreateEntry("shared", "shared-second");
        var registry = CreateRegistry(
            options,
            [
                new RecordingToolRegistryProvider([CreateEntry("root", "root"), firstShared]),
                new RecordingToolRegistryProvider([secondShared]),
            ]);
        var context = new AICompletionContext();

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["root", "shared-first", "shared-second"], result.Select(entry => entry.Id));
        Assert.Equal(["shared"], GetDependencyNames(context));
    }

    [Fact]
    public async Task GetAllAsync_ReentrantDuplicateNameGroup_PreservesNestedDepthFirstOrder()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "root", "shared");
        AddDefinition(options, "shared", "shared", "leaf");
        AddDefinition(options, "leaf");
        var context = new AICompletionContext();
        var registry = CreateRegistry(
            options,
            [
                new RecordingToolRegistryProvider(
                [
                    CreateEntry("root"),
                    CreateEntry("shared", "shared-1"),
                    CreateEntry("shared", "shared-2"),
                    CreateEntry("leaf"),
                ]),
            ]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["root", "shared-1", "shared-2", "leaf"],
            result.Select(entry => entry.Id));
        Assert.Equal(["shared", "leaf"], GetDependencyNames(context));
    }

    [Fact]
    public async Task GetAllAsync_DuplicateDependencyIds_UsesFirstProviderEntry()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "root", "shared");
        AddDefinition(options, "shared");
        var firstShared = CreateEntry("shared", "shared-id", "First");
        var duplicateShared = CreateEntry("shared", "SHARED-ID", "Duplicate");
        var registry = CreateRegistry(
            options,
            [
                new RecordingToolRegistryProvider([CreateEntry("root")]),
                new RecordingToolRegistryProvider([firstShared]),
                new RecordingToolRegistryProvider([duplicateShared]),
            ]);

        var result = await registry.GetAllAsync(
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Same(firstShared, result[1]);
    }

    [Fact]
    public async Task GetAllAsync_DiamondDependencies_ResolveSharedEntryOnceWithStableSideEffects()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "root", "left", "right");
        AddDefinition(options, "left", "shared");
        AddDefinition(options, "right", "shared");
        AddDefinition(options, "shared");
        var context = new AICompletionContext();
        var registry = CreateRegistry(
            options,
            [
                new RecordingToolRegistryProvider(
                [
                    CreateEntry("root"),
                    CreateEntry("right"),
                    CreateEntry("shared"),
                    CreateEntry("left"),
                ]),
            ]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["root", "left", "shared", "right"], result.Select(entry => entry.Name));
        Assert.Equal(["left", "shared", "right"], GetDependencyNames(context));
    }

    [Fact]
    public async Task GetAllAsync_MultipleRootsSharingDependency_ResolveSharedEntryBeforeSecondRoot()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "root-1", "shared");
        AddDefinition(options, "root-2", "shared");
        AddDefinition(options, "shared");
        var context = new AICompletionContext();
        var registry = CreateRegistry(
            options,
            [
                new RecordingToolRegistryProvider(
                [
                    CreateEntry("root-1"),
                    CreateEntry("root-2"),
                    CreateEntry("shared"),
                ]),
            ]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["root-1", "shared", "root-2"], result.Select(entry => entry.Name));
        Assert.Equal(["shared"], GetDependencyNames(context));
    }

    [Fact]
    public async Task GetAllAsync_MissingDependencies_AreIgnoredAndClearStaleSideEffect()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "root", "missing");
        var context = new AICompletionContext();
        context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames] = new[] { "stale" };
        var registry = CreateRegistry(
            options,
            [new RecordingToolRegistryProvider([CreateEntry("root")])]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["root"], result.Select(entry => entry.Name));
        Assert.False(context.AdditionalProperties.ContainsKey(AICompletionContextKeys.DependencyToolNames));
    }

    [Fact]
    public async Task GetAllAsync_SelfCycle_ResolvesOnceAndRecordsSelfDependencyName()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "self", "self");
        var context = new AICompletionContext();
        var registry = CreateRegistry(
            options,
            [new RecordingToolRegistryProvider([CreateEntry("self")])]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["self"], result.Select(entry => entry.Name));
        Assert.Equal(["self"], GetDependencyNames(context));
    }

    [Fact]
    public async Task GetAllAsync_MultiNodeCycle_ResolvesEachEntryOnceAndRecordsEncounterOrder()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "a", "b");
        AddDefinition(options, "b", "c");
        AddDefinition(options, "c", "a");
        var context = new AICompletionContext();
        var registry = CreateRegistry(
            options,
            [
                new RecordingToolRegistryProvider(
                [
                    CreateEntry("a"),
                    CreateEntry("b"),
                    CreateEntry("c"),
                ]),
            ]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b", "c"], result.Select(entry => entry.Name));
        Assert.Equal(["b", "c", "a"], GetDependencyNames(context));
    }

    [Fact]
    public async Task GetAllAsync_DependencyLookup_IsCaseInsensitiveAndPreservesConfiguredNameCasing()
    {
        var options = new AIToolDefinitionOptions();
        AddDefinition(options, "root", "SHARED");
        AddDefinition(options, "shared");
        var context = new AICompletionContext();
        var registry = CreateRegistry(
            options,
            [
                new RecordingToolRegistryProvider(
                [
                    CreateEntry("ROOT"),
                    CreateEntry("shared"),
                ]),
            ]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["ROOT", "shared"], result.Select(entry => entry.Name));
        Assert.Equal(["SHARED"], GetDependencyNames(context));
    }

    [Fact]
    public async Task GetAllAsync_NullContextAndCancellationToken_ArePropagatedToEveryProvider()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var firstProvider = new RecordingToolRegistryProvider([CreateEntry("first")]);
        var secondProvider = new RecordingToolRegistryProvider([CreateEntry("second")]);
        var registry = CreateRegistry(
            new AIToolDefinitionOptions(),
            [firstProvider, secondProvider]);

        var result = await registry.GetAllAsync(null, cancellationTokenSource.Token);

        Assert.Equal(["first", "second"], result.Select(entry => entry.Name));
        Assert.Null(firstProvider.Context);
        Assert.Null(secondProvider.Context);
        Assert.Equal(cancellationTokenSource.Token, firstProvider.CancellationToken);
        Assert.Equal(cancellationTokenSource.Token, secondProvider.CancellationToken);
    }

    [Fact]
    public async Task GetAllAsync_ProviderException_LogsWarningAndContinues()
    {
        var exception = new InvalidOperationException("Provider error");
        var failedProvider = new RecordingToolRegistryProvider(exception);
        var healthyProvider = new RecordingToolRegistryProvider([CreateEntry("healthy")]);
        var logger = new RecordingLogger();
        var registry = CreateRegistry(
            new AIToolDefinitionOptions(),
            [failedProvider, healthyProvider],
            logger);

        var result = await registry.GetAllAsync(
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(["healthy"], result.Select(entry => entry.Name));
        Assert.Equal(1, failedProvider.CallCount);
        Assert.Equal(1, healthyProvider.CallCount);
        Assert.Equal(LogLevel.Warning, logger.LogLevel);
        Assert.Same(exception, logger.Exception);
        Assert.Contains(nameof(RecordingToolRegistryProvider), logger.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAllAsync_ProviderCancellation_RethrowsWithoutLoggingOrCallingLaterProviders()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var exception = new OperationCanceledException(cancellationTokenSource.Token);
        var canceledProvider = new RecordingToolRegistryProvider(exception);
        var laterProvider = new RecordingToolRegistryProvider([CreateEntry("later")]);
        var logger = new RecordingLogger();
        var registry = CreateRegistry(
            new AIToolDefinitionOptions(),
            [canceledProvider, laterProvider],
            logger);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => registry.GetAllAsync(new AICompletionContext(), cancellationTokenSource.Token));

        Assert.Same(exception, thrown);
        Assert.Equal(1, canceledProvider.CallCount);
        Assert.Equal(0, laterProvider.CallCount);
        Assert.Null(logger.LogLevel);
    }

    [Fact]
    public async Task GetAllAsync_ProviderReturningNull_IsSkipped()
    {
        var nullProvider = new RecordingToolRegistryProvider((IReadOnlyList<ToolRegistryEntry>)null);
        var healthyProvider = new RecordingToolRegistryProvider([CreateEntry("healthy")]);
        var registry = CreateRegistry(
            new AIToolDefinitionOptions(),
            [nullProvider, healthyProvider]);

        var result = await registry.GetAllAsync(
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(["healthy"], result.Select(entry => entry.Name));
    }

    private static DefaultToolRegistry CreateRegistry(
        AIToolDefinitionOptions options,
        IToolRegistryProvider[] providers,
        ILogger<DefaultToolRegistry> logger = null)
    {
        return new DefaultToolRegistry(
            providers,
            Options.Create(options),
            new LuceneTextTokenizer(),
            logger ?? NullLogger<DefaultToolRegistry>.Instance);
    }

    private static void AddDefinition(
        AIToolDefinitionOptions options,
        string name,
        params string[] dependencies)
    {
        var definition = new AIToolDefinitionEntry(typeof(object))
        {
            Name = name,
        };

        foreach (var dependency in dependencies)
        {
            definition.AddDependency(dependency);
        }

        options.SetTool(name, definition);
    }

    private static ToolRegistryEntry CreateEntry(
        string name,
        string id = null,
        string description = null)
    {
        return new ToolRegistryEntry
        {
            Id = id ?? name,
            Name = name,
            Description = description,
        };
    }

    private static string[] GetDependencyNames(AICompletionContext context)
    {
        return Assert.IsType<string[]>(
            context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames]);
    }

    private sealed class RecordingToolRegistryProvider : IToolRegistryProvider
    {
        private readonly IReadOnlyList<ToolRegistryEntry> _entries;
        private readonly Exception _exception;

        public RecordingToolRegistryProvider(IReadOnlyList<ToolRegistryEntry> entries)
        {
            _entries = entries;
        }

        public RecordingToolRegistryProvider(Exception exception)
        {
            _entries = [];
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public AICompletionContext Context { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
            AICompletionContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Context = context;
            CancellationToken = cancellationToken;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_entries);
        }
    }

    private sealed class RecordingLogger : ILogger<DefaultToolRegistry>
    {
        public LogLevel? LogLevel { get; private set; }

        public Exception Exception { get; private set; }

        public string Message { get; private set; }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            LogLevel = logLevel;
            Exception = exception;
            Message = formatter(state, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
