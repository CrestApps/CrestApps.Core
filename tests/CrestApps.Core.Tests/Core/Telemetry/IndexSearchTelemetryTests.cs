using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Moq;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class IndexSearchTelemetryTests
{
    [Fact]
    public async Task SearchWithOutcomeAsync_WhenTheProviderThrows_RecordsAnErrorForThatProvider()
    {
        using var collector = new TelemetryCollector();
        var provider = "provider-" + Guid.NewGuid().ToString("N");

        var contentManager = new Mock<IDataSourceContentManager>();
        contentManager
            .Setup(manager => manager.SearchAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The index is unreachable."));

        var outcome = await contentManager.Object.SearchWithOutcomeAsync(CreateProfile(provider), [0.1f], "data-source", 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(outcome.Succeeded);

        var measurement = Assert.Single(collector.GetMeasurements("crestapps.core.index.operation.duration", CrestAppsTelemetry.ProviderTag, provider));

        Assert.Equal("search", measurement.Tags[CrestAppsTelemetry.OperationTag]);
        Assert.Equal(CrestAppsTelemetry.OutcomeError, measurement.Tags[CrestAppsTelemetry.OutcomeTag]);
    }

    [Fact]
    public async Task SearchWithOutcomeAsync_WhenTheProviderAnswers_RecordsASuccess()
    {
        using var collector = new TelemetryCollector();
        var provider = "provider-" + Guid.NewGuid().ToString("N");

        var contentManager = new Mock<IDataSourceContentManager>();
        contentManager
            .Setup(manager => manager.SearchAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var outcome = await contentManager.Object.SearchWithOutcomeAsync(CreateProfile(provider), [0.1f], "data-source", 5, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);

        var measurement = Assert.Single(collector.GetMeasurements("crestapps.core.index.operation.duration", CrestAppsTelemetry.ProviderTag, provider));

        Assert.Equal(CrestAppsTelemetry.OutcomeOk, measurement.Tags[CrestAppsTelemetry.OutcomeTag]);
    }

    private static IIndexProfileInfo CreateProfile(string provider)
    {
        var profile = new Mock<IIndexProfileInfo>();
        profile.SetupGet(info => info.ProviderName).Returns(provider);

        return profile.Object;
    }
}
