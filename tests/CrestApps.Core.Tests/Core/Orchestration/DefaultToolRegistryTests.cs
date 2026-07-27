using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DefaultToolRegistryTests
{
    [Fact]
    public async Task GetAllAsync_NoProviders_ReturnsEmptyAndClearsDependencyNames()
    {
        var context = new AICompletionContext();
        context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames] = new[] { "stale" };
        var registry = CreateRegistry([]);

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.False(context.AdditionalProperties.ContainsKey(AICompletionContextKeys.DependencyToolNames));
    }

    [Fact]
    public async Task GetAllAsync_MultipleProviders_PreservesProviderAndEntryOrder()
    {
        var firstProvider = new RecordingToolRegistryProvider(
        [
            CreateEntry("system-1", "system-1", ToolRegistryEntrySource.System),
            CreateEntry("local-1", "local-1", ToolRegistryEntrySource.Local),
        ]);
        var secondProvider = new RecordingToolRegistryProvider(
        [
            CreateEntry("mcp-1", "mcp-1", ToolRegistryEntrySource.McpServer),
            CreateEntry("agent-1", "agent-1", ToolRegistryEntrySource.Agent),
        ]);
        var registry = CreateRegistry([firstProvider, secondProvider]);

        var result = await registry.GetAllAsync(
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["system-1", "local-1", "mcp-1", "agent-1"],
            result.Select(entry => entry.Id));
        Assert.Equal(
            [
                ToolRegistryEntrySource.System,
                ToolRegistryEntrySource.Local,
                ToolRegistryEntrySource.McpServer,
                ToolRegistryEntrySource.Agent,
            ],
            result.Select(entry => entry.Source));
    }

    [Fact]
    public async Task GetAllAsync_DuplicateIdsAcrossProviders_ReturnsFirstCaseInsensitiveMatch()
    {
        var firstEntry = CreateEntry("Shared-Tool", "shared-tool", ToolRegistryEntrySource.System);
        firstEntry.Description = "First copy";
        var duplicateEntry = CreateEntry("shared-tool", "shared-tool", ToolRegistryEntrySource.Local);
        duplicateEntry.Description = "Duplicate copy";
        var registry = CreateRegistry(
        [
            new RecordingToolRegistryProvider([firstEntry]),
            new RecordingToolRegistryProvider([duplicateEntry]),
        ]);

        var result = await registry.GetAllAsync(
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(result);
        Assert.Same(firstEntry, entry);
    }

    [Fact]
    public async Task SearchAsync_ProviderOrderAndStableScoreTies_ArePreservedAcrossSources()
    {
        var tokenizer = new RecordingTextTokenizer(TokenizeWords);
        var registry = CreateRegistry(
        [
            new RecordingToolRegistryProvider(
            [
                CreateEntry("system-match", "match", ToolRegistryEntrySource.System),
                CreateEntry("local-zero", "unrelated", ToolRegistryEntrySource.Local),
            ]),
            new RecordingToolRegistryProvider(
            [
                CreateEntry("mcp-match", "match", ToolRegistryEntrySource.McpServer),
                CreateEntry("agent-zero", "different", ToolRegistryEntrySource.Agent),
            ]),
        ],
        tokenizer: tokenizer);

        var result = await registry.SearchAsync(
            "match",
            10,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["system-match", "mcp-match", "local-zero", "agent-zero"],
            result.Select(entry => entry.Id));
        Assert.Equal(
            [
                ToolRegistryEntrySource.System,
                ToolRegistryEntrySource.McpServer,
                ToolRegistryEntrySource.Local,
                ToolRegistryEntrySource.Agent,
            ],
            result.Select(entry => entry.Source));
    }

    [Fact]
    public async Task SearchAsync_ZeroScoreEntries_AreIncludedAfterMatchingEntries()
    {
        var registry = CreateRegistry(
        [
            new RecordingToolRegistryProvider(
            [
                CreateEntry("zero-1", "alpha"),
                CreateEntry("match", "needle"),
                CreateEntry("zero-2", "beta"),
            ]),
        ],
        tokenizer: new RecordingTextTokenizer(TokenizeWords));

        var result = await registry.SearchAsync(
            "needle",
            3,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(["match", "zero-1", "zero-2"], result.Select(entry => entry.Id));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(10, 3)]
    public async Task SearchAsync_TopK_PreservesCurrentTakeBehavior(int topK, int expectedCount)
    {
        var tokenizer = new RecordingTextTokenizer(TokenizeWords);
        var registry = CreateRegistry(
        [
            new RecordingToolRegistryProvider(
            [
                CreateEntry("first", "match"),
                CreateEntry("second", "match"),
                CreateEntry("third", "match"),
            ]),
        ],
        tokenizer: tokenizer);

        var result = await registry.SearchAsync(
            "match",
            topK,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedCount, result.Count);
        Assert.Equal(
            new[] { "first", "second", "third" }.Take(expectedCount),
            result.Select(entry => entry.Id));
        Assert.Equal(4, tokenizer.Inputs.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task SearchAsync_EmptyOrWhitespaceQuery_ReturnsEmptyWithoutCallingProviders(string query)
    {
        var provider = new RecordingToolRegistryProvider([CreateEntry("tool", "tool")]);
        var context = new AICompletionContext();
        var dependencyNames = new[] { "existing" };
        context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames] = dependencyNames;
        var registry = CreateRegistry([provider]);

        var result = await registry.SearchAsync(
            query,
            5,
            context,
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Equal(0, provider.CallCount);
        Assert.Same(
            dependencyNames,
            context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames]);
    }

    [Fact]
    public async Task SearchAsync_EmptyQueryTokenization_ReturnsOriginalOrderWithoutTokenizingEntries()
    {
        var tokenizer = new RecordingTextTokenizer(_ => []);
        var registry = CreateRegistry(
        [
            new RecordingToolRegistryProvider(
            [
                CreateEntry("first", "first"),
                CreateEntry("second", "second"),
                CreateEntry("third", "third"),
            ]),
        ],
        tokenizer: tokenizer);

        var result = await registry.SearchAsync(
            "stop words only",
            2,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], result.Select(entry => entry.Id));
        Assert.Equal(["stop words only"], tokenizer.Inputs);
    }

    [Fact]
    public async Task SearchAsync_DuplicateNamesWithDistinctIds_ReturnsEveryEntry()
    {
        var registry = CreateRegistry(
        [
            new RecordingToolRegistryProvider(
            [
                CreateEntry("first-id", "duplicate"),
                CreateEntry("second-id", "duplicate"),
            ]),
        ],
        tokenizer: new RecordingTextTokenizer(TokenizeWords));

        var result = await registry.SearchAsync(
            "duplicate",
            10,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(["first-id", "second-id"], result.Select(entry => entry.Id));
    }

    [Fact]
    public async Task SearchAsync_LuceneTokenizer_MatchesCaseInsensitively()
    {
        var registry = CreateRegistry(
        [
            new RecordingToolRegistryProvider(
            [
                CreateEntry("send-slack", "SendSlackMessage", description: "Sends channel messages"),
                CreateEntry("other", "CreateTicket", description: "Creates work items"),
            ]),
        ]);

        var result = await registry.SearchAsync(
            "sEnD SLACK messages",
            1,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(result);
        Assert.Equal("send-slack", entry.Id);
    }

    [Fact]
    public async Task SearchAsync_PropagatesContextAndCancellationTokenAndTokenizesQueryOnce()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var provider = new RecordingToolRegistryProvider([CreateEntry("tool", "match")]);
        var tokenizer = new RecordingTextTokenizer(TokenizeWords);
        var context = new AICompletionContext();
        var registry = CreateRegistry([provider], tokenizer: tokenizer);

        var result = await registry.SearchAsync(
            "match",
            1,
            context,
            cancellationTokenSource.Token);

        Assert.Single(result);
        Assert.Same(context, provider.Context);
        Assert.Equal(cancellationTokenSource.Token, provider.CancellationToken);
        Assert.Equal(2, tokenizer.Inputs.Count);
        Assert.Equal("match", tokenizer.Inputs[0]);
        Assert.Equal("match ", tokenizer.Inputs[1]);
    }

    [Fact]
    public async Task SearchAsync_NullContext_IsPropagatedToProvider()
    {
        var provider = new RecordingToolRegistryProvider([CreateEntry("tool", "match")]);
        var registry = CreateRegistry(
            [provider],
            tokenizer: new RecordingTextTokenizer(TokenizeWords));

        var result = await registry.SearchAsync(
            "match",
            1,
            null,
            TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Null(provider.Context);
    }

    private static DefaultToolRegistry CreateRegistry(
        IToolRegistryProvider[] providers,
        AIToolDefinitionOptions options = null,
        ITextTokenizer tokenizer = null,
        ILogger<DefaultToolRegistry> logger = null)
    {
        return new DefaultToolRegistry(
            providers,
            Options.Create(options ?? new AIToolDefinitionOptions()),
            tokenizer ?? new LuceneTextTokenizer(),
            logger ?? NullLogger<DefaultToolRegistry>.Instance);
    }

    private static ToolRegistryEntry CreateEntry(
        string id,
        string name,
        ToolRegistryEntrySource source = ToolRegistryEntrySource.Local,
        string description = null)
    {
        return new ToolRegistryEntry
        {
            Id = id,
            Name = name,
            Description = description,
            Source = source,
        };
    }

    private static HashSet<string> TokenizeWords(string text)
    {
        return text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed class RecordingTextTokenizer : ITextTokenizer
    {
        private readonly Func<string, HashSet<string>> _tokenize;

        public RecordingTextTokenizer(Func<string, HashSet<string>> tokenize)
        {
            _tokenize = tokenize;
        }

        public List<string> Inputs { get; } = [];

        public HashSet<string> Tokenize(string text)
        {
            Inputs.Add(text);

            return _tokenize(text);
        }
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
}
