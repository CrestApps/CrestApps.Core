using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Models;

public sealed class AIDataSourceOptionsTests
{
    [Theory]
    [InlineData(1, 0f)]
    [InlineData(2, 0.1f)]
    [InlineData(3, 0.2f)]
    [InlineData(4, 0.3f)]
    [InlineData(5, 0.4f)]
    public void GetMinimumScore_MapsStrictnessToCalibratedThreshold(int strictness, float expectedScore)
    {
        var options = new AIDataSourceOptions();

        var result = options.GetMinimumScore(strictness);

        Assert.Equal(expectedScore, result, 5);
    }

    /// <summary>
    /// The narrowest level must land exactly on the ceiling, and no level may exceed it. A level above the
    /// range real cosine scores occupy is unreachable, which is indistinguishable from a broken index: the
    /// search returns nothing and reports an ordinary empty result.
    /// </summary>
    [Fact]
    public void GetMinimumScore_AtMaximumStrictness_EqualsTheCeilingAndNeverExceedsIt()
    {
        var options = new AIDataSourceOptions();

        Assert.Equal(AIDataSourceOptions.DefaultStrictestMinimumScore, options.GetMinimumScore(AIDataSourceOptions.MaxStrictness), 5);

        for (var strictness = AIDataSourceOptions.MinStrictness; strictness <= AIDataSourceOptions.MaxStrictness; strictness++)
        {
            Assert.InRange(options.GetMinimumScore(strictness), 0f, AIDataSourceOptions.DefaultStrictestMinimumScore);
        }
    }

    /// <summary>
    /// The default level is what almost every installation runs on, so it is the one that must sit inside
    /// the range a real match can reach rather than above it.
    /// </summary>
    [Fact]
    public void GetMinimumScore_AtTheDefaultLevel_IsReachableByARealCosineMatch()
    {
        var options = new AIDataSourceOptions();

        // A genuinely relevant hit from a modern embedding model against a short query lands here.
        const float PlausibleRelevantScore = 0.27f;

        Assert.True(
            options.GetMinimumScore(null) < PlausibleRelevantScore,
            $"The default floor {options.GetMinimumScore(null)} excludes a genuinely relevant result scoring {PlausibleRelevantScore}.");
    }

    [Fact]
    public void GetMinimumScore_WhenTheCeilingIsRaised_ScalesEveryLevelWithIt()
    {
        var options = new AIDataSourceOptions
        {
            StrictestMinimumScore = 1f,
        };

        Assert.Equal(0f, options.GetMinimumScore(1), 5);
        Assert.Equal(0.5f, options.GetMinimumScore(3), 5);
        Assert.Equal(1f, options.GetMinimumScore(5), 5);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(2f)]
    public void GetMinimumScore_WhenTheCeilingIsOutOfRange_StaysWithinZeroToOne(float ceiling)
    {
        var options = new AIDataSourceOptions
        {
            StrictestMinimumScore = ceiling,
        };

        for (var strictness = AIDataSourceOptions.MinStrictness; strictness <= AIDataSourceOptions.MaxStrictness; strictness++)
        {
            Assert.InRange(options.GetMinimumScore(strictness), 0f, 1f);
        }
    }
}
