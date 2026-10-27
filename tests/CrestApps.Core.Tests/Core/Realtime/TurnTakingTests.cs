using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.Tests.Core.Realtime;

public sealed class TurnTakingTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(1d, 1)]
    [InlineData(0.7, 1)]
    [InlineData(0.5, 3)]
    [InlineData(0.2, 5)]
    [InlineData(0d, int.MaxValue)]
    public void MinimumWordsToInterrupt_FollowsTheSensitivity(double? sensitivity, int expected)
    {
        Assert.Equal(expected, TurnTaking.MinimumWordsToInterrupt(sensitivity));
    }

    [Fact]
    public void ToVadThreshold_IsLowerForMoreSensitiveProfiles()
    {
        Assert.Null(TurnTaking.ToVadThreshold(null));
        Assert.True(TurnTaking.ToVadThreshold(1) < TurnTaking.ToVadThreshold(0.5));
        Assert.True(TurnTaking.ToVadThreshold(0.5) < TurnTaking.ToVadThreshold(0));
    }

    [Fact]
    public void BackchannelPlanner_AcknowledgesEveryFewWordsAndRotatesPhrases()
    {
        var planner = new BackchannelPlanner(["Mm-hm.", "Right."], wordsBetween: 10);

        Assert.False(planner.TryNext(9, out _));
        Assert.True(planner.TryNext(10, out var first));
        Assert.False(planner.TryNext(15, out _));
        Assert.True(planner.TryNext(21, out var second));

        Assert.Equal("Mm-hm.", first);
        Assert.Equal("Right.", second);

        planner.Reset();

        Assert.True(planner.TryNext(10, out _));
    }

    [Fact]
    public void BackchannelPlanner_UsesBuiltInPhrasesWhenNoneAreConfigured()
    {
        var planner = new BackchannelPlanner([" "], wordsBetween: 1);

        Assert.True(planner.TryNext(1, out var phrase));
        Assert.False(string.IsNullOrWhiteSpace(phrase));
    }
}
