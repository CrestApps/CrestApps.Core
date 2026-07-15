using System.Text.Json;
using CrestApps.Core.AI.Chat.Handlers;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Chat;

public sealed class DataSourceChatInteractionSettingsHandlerTests
{
    [Fact]
    public async Task UpdatingAsync_WithDataSource_PersistsDataSourceAndDefaultRagMetadata()
    {
        var dataSourceCatalog = new Mock<IAIDataSourceStore>();
        dataSourceCatalog
            .Setup(catalog => catalog.FindByIdAsync("datasource-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDataSource { ItemId = "datasource-1" });

        var serviceProvider = new ServiceCollection()
            .AddSingleton(dataSourceCatalog.Object)
            .BuildServiceProvider();

        var handler = new DataSourceChatInteractionSettingsHandler(
            serviceProvider,
            NullLogger<DataSourceChatInteractionSettingsHandler>.Instance);

        using var document = JsonDocument.Parse("""{"dataSourceId":"datasource-1"}""");
        var interaction = new ChatInteraction();

        await handler.UpdatingAsync(interaction, document.RootElement, TestContext.Current.CancellationToken);

        Assert.True(interaction.TryGet<DataSourceMetadata>(out var dataSourceMetadata));
        Assert.Equal("datasource-1", dataSourceMetadata.DataSourceId);

        Assert.True(interaction.TryGet<AIDataSourceRagMetadata>(out var ragMetadata));
        Assert.False(ragMetadata.IsInScope);
        Assert.Null(ragMetadata.Strictness);
        Assert.Null(ragMetadata.TopNDocuments);
        Assert.Null(ragMetadata.Filter);
    }

    [Fact]
    public async Task UpdatingAsync_WithoutDataSource_ClearsDataSourceAndPersistsRagSettings()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var handler = new DataSourceChatInteractionSettingsHandler(
            serviceProvider,
            NullLogger<DataSourceChatInteractionSettingsHandler>.Instance);

        using var document = JsonDocument.Parse("""{"strictness":4,"topNDocuments":8,"isInScope":true,"filter":"category eq 'docs'"}""");
        var interaction = new ChatInteraction();
        interaction.Put(new DataSourceMetadata { DataSourceId = "datasource-1" });
        interaction.Put(new AIDataSourceRagMetadata { IsInScope = false, Strictness = 3, TopNDocuments = 5, Filter = null });

        await handler.UpdatingAsync(interaction, document.RootElement, TestContext.Current.CancellationToken);

        Assert.True(interaction.TryGet<DataSourceMetadata>(out var dataSourceMetadata));
        Assert.Null(dataSourceMetadata.DataSourceId);

        Assert.True(interaction.TryGet<AIDataSourceRagMetadata>(out var ragMetadata));
        Assert.Equal(4, ragMetadata.Strictness);
        Assert.Equal(8, ragMetadata.TopNDocuments);
        Assert.Equal("category eq 'docs'", ragMetadata.Filter);
        Assert.True(ragMetadata.IsInScope);
    }
}
