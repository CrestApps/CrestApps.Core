using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class DefaultAIProfileManagerTests
{
    [Fact]
    public async Task GetAsync_FiltersProfilesByType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAIProfileStore>();
        store.Setup(catalog => catalog.GetByTypeAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AIProfile { ItemId = "agent-1", Name = "agent-profile", Type = AIProfileType.Agent },
            ]);

        var manager = new DefaultAIProfileManager(store.Object, [], TimeProvider.System, NullLogger<DefaultAIProfileManager>.Instance);

        var results = (await manager.GetAsync(AIProfileType.Agent, cancellationToken)).ToList();

        Assert.Single(results);
        Assert.Equal("agent-profile", results[0].Name);
    }

    [Fact]
    public async Task CreateAsync_AssignsDefaults_WhenCalledThroughInterface()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var timestamp = new DateTimeOffset(2026, 4, 27, 19, 0, 0, TimeSpan.Zero);
        var store = new Mock<IAIProfileStore>();
        var captured = default(AIProfile);
        store.Setup(catalog => catalog.CreateAsync(It.IsAny<AIProfile>(), It.IsAny<CancellationToken>()))
            .Callback<AIProfile, CancellationToken>((profile, _) => captured = profile)
            .Returns(ValueTask.CompletedTask);

        var manager = new DefaultAIProfileManager(store.Object, [], new StubTimeProvider(timestamp), NullLogger<DefaultAIProfileManager>.Instance);
        var profile = new AIProfile { Name = "profile-1" };

        await ((IAIProfileManager)manager).CreateAsync(profile, cancellationToken);

        Assert.NotNull(captured);
        Assert.False(string.IsNullOrEmpty(captured.ItemId));
        Assert.Equal(timestamp.DateTime, captured.CreatedUtc);
    }

    [Fact]
    public async Task ValidateAsync_FailsWhenNameIsMissing_WhenCalledThroughInterface()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var manager = new DefaultAIProfileManager(
            Mock.Of<IAIProfileStore>(),
            [],
            TimeProvider.System,
            NullLogger<DefaultAIProfileManager>.Instance);

        var result = await ((IAIProfileManager)manager).ValidateAsync(new AIProfile(), cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.MemberNames.Contains(nameof(AIProfile.Name)));
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
