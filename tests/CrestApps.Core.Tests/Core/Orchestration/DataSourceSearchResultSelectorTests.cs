using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing.Models;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DataSourceSearchResultSelectorTests
{
    [Fact]
    public void SelectTopResults_ExcludesAssetsAndErrorPages()
    {
        var results = new[]
        {
            new DataSourceSearchResult
            {
                ReferenceId = "https://www.example.com/locations.kml",
                ReferenceType = "Web",
                Title = "Locations",
                Content = "coordinates",
                Score = 0.90f,
            },
            new DataSourceSearchResult
            {
                ReferenceId = "https://www.example.com/404/",
                ReferenceType = "Web",
                Title = "404 | Example",
                Content = "404 - Page Not Found",
                Score = 0.80f,
            },
            new DataSourceSearchResult
            {
                ReferenceId = "https://www.example.com/about/",
                ReferenceType = "Web",
                Title = "About",
                Content = "Since 1941, the theater has served the community.",
                Score = 0.41f,
            },
            new DataSourceSearchResult
            {
                ReferenceId = "https://www.example.com/history/",
                ReferenceType = "Web",
                Title = "History",
                Content = "The venue later expanded its programming.",
                Score = 0.40f,
            },
        };

        var selected = DataSourceSearchResultSelector.SelectTopResults(results, 2, 0.35f);

        Assert.Equal(2, selected.Count);
        Assert.All(selected, result => Assert.DoesNotContain(".kml", result.ReferenceId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.All(selected, result => Assert.DoesNotContain("404", result.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, result => result.Content.Contains("1941", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 9)]
    [InlineData(5, 15)]
    [InlineData(10, 20)]
    public void GetCandidateCount_ExpandsAndCapsRequestedTopN(int topN, int expected)
    {
        var candidateCount = DataSourceSearchResultSelector.GetCandidateCount(topN);

        Assert.Equal(expected, candidateCount);
    }
}
