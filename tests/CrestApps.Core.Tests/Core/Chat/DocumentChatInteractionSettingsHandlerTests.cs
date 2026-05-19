using System.Text.Json;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Chat;

public sealed class DocumentChatInteractionSettingsHandlerTests
{
    [Fact]
    public async Task UpdatingAsync_WithRetrievalMode_PersistsRetrievalMode()
    {
        using var document = JsonDocument.Parse("""{"documentRetrievalMode":"Hierarchical"}""");
        var interaction = new ChatInteraction();
        var handler = new DocumentChatInteractionSettingsHandler();

        await handler.UpdatingAsync(interaction, document.RootElement, TestContext.Current.CancellationToken);

        Assert.Equal(DocumentRetrievalMode.Hierarchical, interaction.GetOrCreate<DocumentsMetadata>().RetrievalMode);
    }

    [Fact]
    public async Task UpdatingAsync_WithBlankRetrievalMode_ClearsRetrievalMode()
    {
        using var document = JsonDocument.Parse("""{"documentRetrievalMode":""}""");
        var interaction = new ChatInteraction();
        interaction.Put(new DocumentsMetadata { RetrievalMode = DocumentRetrievalMode.Chunk });
        var handler = new DocumentChatInteractionSettingsHandler();

        await handler.UpdatingAsync(interaction, document.RootElement, TestContext.Current.CancellationToken);

        Assert.Null(interaction.GetOrCreate<DocumentsMetadata>().RetrievalMode);
    }
}
