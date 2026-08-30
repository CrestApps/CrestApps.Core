using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Models;

public sealed class AIDataSourceOptionsTests
{
    [Theory]
    [InlineData(1, 0f)]
    [InlineData(2, 0.2f)]
    [InlineData(3, 0.4f)]
    [InlineData(4, 0.6f)]
    [InlineData(5, 0.8f)]
    public void GetMinimumScore_MapsStrictnessToCalibratedThreshold(int strictness, float expectedScore)
    {
        var options = new AIDataSourceOptions();

        var result = options.GetMinimumScore(strictness);

        Assert.Equal(expectedScore, result);
    }
}
