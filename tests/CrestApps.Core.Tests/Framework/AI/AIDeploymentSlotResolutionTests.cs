using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Covers slot-based deployment resolution: the single ordered chain that replaced the purpose switch, and
/// the capability filter that decides which deployments qualify for a slot.
/// </summary>
public sealed class AIDeploymentSlotResolutionTests
{
    [Fact]
    public async Task ResolveUtilityOrDefaultAsync_WhenNoUtilityIsConfiguredAnywhere_ShouldUseTheCallersChatDeployment()
    {
        // Arrange. This is the regression guard for the resolution chain. "decoy" is listed first and is
        // text capable, so a utility resolve that evaluates "first capable" on its own — rather than at the
        // very end of the whole chain — answers with it and the caller's chat deployment is never consulted.
        // Background summarization, data extraction, and query rewriting would silently run on the wrong
        // model.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("decoy", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("profile-chat", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var deployment = await manager.ResolveUtilityOrDefaultAsync(
            utilityDeploymentName: null,
            chatDeploymentName: "profile-chat");

        // Assert
        Assert.NotNull(deployment);
        Assert.Equal("profile-chat", deployment.Name);
    }

    [Fact]
    public async Task ResolveUtilityOrDefaultAsync_WhenUtilityNameIsGiven_ShouldPreferItOverTheChatDeployment()
    {
        // Arrange
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("profile-chat", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("profile-utility", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var deployment = await manager.ResolveUtilityOrDefaultAsync(
            utilityDeploymentName: "profile-utility",
            chatDeploymentName: "profile-chat");

        // Assert
        Assert.Equal("profile-utility", deployment.Name);
    }

    [Fact]
    public async Task ResolveUtilityOrDefaultAsync_WhenOnlyTheSiteUtilityDefaultIsSet_ShouldPreferItOverTheChatDeployment()
    {
        // Arrange. The site-wide utility default outranks the caller's chat deployment, which is the next
        // link in the chain.
        var settings = new DefaultAIDeploymentSettings
        {
            DefaultUtilityDeploymentName = "site-utility",
        };

        var manager = CreateManager(
            settings,
            CreateDeployment("profile-chat", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("site-utility", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var deployment = await manager.ResolveUtilityOrDefaultAsync(chatDeploymentName: "profile-chat");

        // Assert
        Assert.Equal("site-utility", deployment.Name);
    }

    [Fact]
    public async Task ResolveUtilityOrDefaultAsync_WhenOnlyTheSiteChatDefaultIsSet_ShouldUseItBeforeFirstCapable()
    {
        // Arrange
        var settings = new DefaultAIDeploymentSettings
        {
            DefaultChatDeploymentName = "site-chat",
        };

        var manager = CreateManager(
            settings,
            CreateDeployment("decoy", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("site-chat", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var deployment = await manager.ResolveUtilityOrDefaultAsync();

        // Assert
        Assert.Equal("site-chat", deployment.Name);
    }

    [Fact]
    public async Task ResolveUtilityOrDefaultAsync_WhenNothingIsConfigured_ShouldFallBackToTheFirstTextCapableDeployment()
    {
        // Arrange. "first capable" is the last link, not an absent one.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("whisper", AIDeploymentFeatureNames.SpeechToText),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var deployment = await manager.ResolveUtilityOrDefaultAsync();

        // Assert
        Assert.Equal("gpt-5", deployment.Name);
    }

    [Fact]
    public async Task ResolveUtilityOrDefaultAsync_WhenTheChatDeploymentIsRealtimeOnly_ShouldNotRouteTextWorkToIt()
    {
        // Arrange. A realtime-only deployment cannot serve a text completion, so it must not satisfy the
        // utility chain even when it is the profile's own chat deployment.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var deployment = await manager.ResolveUtilityOrDefaultAsync(chatDeploymentName: "gpt-realtime");

        // Assert
        Assert.Equal("gpt-5", deployment.Name);
    }

    [Fact]
    public async Task ResolveUtilityOrDefaultAsync_WhenTheChatDeploymentDeclaresBothRealtimeAndTextGeneration_ShouldStillNotRouteTextWorkToIt()
    {
        // Arrange. A realtime deployment answers a text completion with an HTTP 400 even when an operator has
        // also ticked text generation for it, so the text slots exclude realtime outright. This is the rule
        // that used to be re-checked defensively through AIDeployment.CanServeTextCompletion().
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.Realtime),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var deployment = await manager.ResolveUtilityOrDefaultAsync(chatDeploymentName: "gpt-realtime");

        // Assert
        Assert.Equal("gpt-5", deployment.Name);
    }

    [Fact]
    public async Task GetAllBySlotAsync_WhenDeploymentDeclaresBothRealtimeAndTextGeneration_ShouldListItOnlyUnderRealtime()
    {
        // Arrange
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.Realtime));

        // Act
        var chat = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.Chat, cancellationToken: TestContext.Current.CancellationToken);
        var utility = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.Utility, cancellationToken: TestContext.Current.CancellationToken);
        var realtime = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.Realtime, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(chat);
        Assert.Empty(utility);
        Assert.Equal(["gpt-realtime"], realtime.Select(d => d.Name));
    }

    [Fact]
    public async Task ResolveSlotAsync_WhenDeploymentDeclaresNoMetadata_ShouldQualifyForChatButNotForEmbedding()
    {
        // Arrange. textGeneration is opt-out and every other feature is opt-in; the slot filter has to keep
        // that asymmetry.
        var unconstrained = new AIDeployment
        {
            ItemId = "1",
            Name = "legacy",
            ModelName = "legacy",
        };

        var manager = CreateManager(new DefaultAIDeploymentSettings(), unconstrained);

        // Act
        var chat = await manager.ResolveSlotAsync(AIDeploymentSlotNames.Chat, cancellationToken: TestContext.Current.CancellationToken);
        var embedding = await manager.ResolveSlotAsync(AIDeploymentSlotNames.Embedding, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("legacy", chat?.Name);
        Assert.Null(embedding);
    }

    [Fact]
    public async Task GetAllBySlotAsync_ShouldExcludeDeploymentsThatDoNotDeclareTheSlotFeature()
    {
        // Arrange
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("whisper", AIDeploymentFeatureNames.SpeechToText),
            CreateDeployment("gpt-4o-audio", AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.AudioInput),
            CreateDeployment("text-embedding-3-large", AIDeploymentFeatureNames.TextEmbedding),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var chat = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.Chat, cancellationToken: TestContext.Current.CancellationToken);
        var speechToText = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.SpeechToText, cancellationToken: TestContext.Current.CancellationToken);
        var embedding = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.Embedding, cancellationToken: TestContext.Current.CancellationToken);
        var realtime = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.Realtime, cancellationToken: TestContext.Current.CancellationToken);

        // Assert. An audio-capable chat model is not a transcription endpoint, and Whisper is not a chat
        // model — the point of keeping speechToText distinct from audioInput.
        Assert.Equal(["gpt-5", "gpt-4o-audio"], chat.Select(d => d.Name));
        Assert.Equal(["whisper"], speechToText.Select(d => d.Name));
        Assert.Equal(["text-embedding-3-large"], embedding.Select(d => d.Name));
        Assert.Equal(["gpt-realtime"], realtime.Select(d => d.Name));
    }

    [Fact]
    public async Task GetConversationalDeploymentsAsync_ShouldListTextAndRealtimeModelsButNothingElse()
    {
        // Arrange. The picker asks "what can this profile talk to", which is a different question from the
        // chat slot's "what can serve a text completion". A realtime deployment answers yes to the first and
        // no to the second, so it must appear here even though the chat slot excludes it.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime),
            CreateDeployment("whisper", AIDeploymentFeatureNames.SpeechToText),
            CreateDeployment("text-embedding-3-large", AIDeploymentFeatureNames.TextEmbedding));

        // Act
        var conversational = await manager.GetConversationalDeploymentsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["gpt-5", "gpt-realtime"], conversational.Select(d => d.Name));
    }

    [Fact]
    public async Task GetConversationalDeploymentsAsync_WhenADeploymentDeclaresBothCapabilities_ShouldListItOnce()
    {
        // Arrange. The union must not double up a deployment that qualifies for both slots.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("hybrid", AIDeploymentFeatureNames.TextGeneration, AIDeploymentFeatureNames.Realtime));

        // Act
        var conversational = await manager.GetConversationalDeploymentsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["gpt-5", "hybrid"], conversational.Select(d => d.Name));
    }

    [Fact]
    public async Task ResolveSlotAsync_WhenSlotIsNotRegistered_ShouldReturnNull()
    {
        // Arrange
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var deployment = await manager.ResolveSlotAsync("not-a-slot", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(deployment);
    }


    private static AIDeployment CreateDeployment(string name, params string[] features)
    {
        var deployment = new AIDeployment
        {
            ItemId = name,
            Name = name,
            ModelName = name,
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Features = features,
        });

        return deployment;
    }

    private static DefaultAIDeploymentManager CreateManager(DefaultAIDeploymentSettings settings, params AIDeployment[] deployments)
    {
        return new DefaultAIDeploymentManager(
            new FakeDeploymentStore(deployments),
            [],
            new StaticOptionsMonitor<DefaultAIDeploymentSettings>(settings),
            Options.Create(AIDeploymentSlotOptions.CreateDefault()),
            NullLogger<DefaultAIDeploymentManager>.Instance);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<T, string> listener) => null;
    }

    private sealed class FakeDeploymentStore : IAIDeploymentStore
    {
        private readonly IReadOnlyCollection<AIDeployment> _deployments;

        public FakeDeploymentStore(IReadOnlyCollection<AIDeployment> deployments)
        {
            _deployments = deployments;
        }

        public ValueTask<AIDeployment> FindByIdAsync(string id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_deployments.FirstOrDefault(d => string.Equals(d.ItemId, id, StringComparison.Ordinal)));

        public ValueTask<AIDeployment> FindByNameAsync(string name, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_deployments.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAllAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_deployments);

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>([.. _deployments.Where(d => ids.Contains(d.ItemId, StringComparer.Ordinal))]);

        public ValueTask<IReadOnlyCollection<AIDeployment>> GetAsync(string source, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyCollection<AIDeployment>>([.. _deployments.Where(d => string.Equals(d.Source, source, StringComparison.Ordinal))]);

        public ValueTask<AIDeployment> GetAsync(string name, string source, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_deployments.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Source, source, StringComparison.Ordinal)));

        public ValueTask<PageResult<AIDeployment>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
            where TQuery : QueryContext
            => ValueTask.FromResult(new PageResult<AIDeployment>());

        public ValueTask CreateAsync(AIDeployment model, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask UpdateAsync(AIDeployment model, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<bool> DeleteAsync(AIDeployment model, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
