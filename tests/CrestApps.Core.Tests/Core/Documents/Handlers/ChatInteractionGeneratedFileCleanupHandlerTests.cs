using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Models;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Handlers;

public sealed class ChatInteractionGeneratedFileCleanupHandlerTests
{
    [Fact]
    public async Task HistoryClearedAsync_DeletesGeneratedFilesReferencedByClearedMessages()
    {
        var cleanupService = new Mock<IConversationDocumentCleanupService>();
        IEnumerable<string> capturedIds = null;
        cleanupService
            .Setup(service => service.CleanupGeneratedDocumentsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((ids, _) => capturedIds = ids.ToList())
            .Returns(Task.CompletedTask);

        var prompts = new List<ChatInteractionPrompt>
        {
            new()
            {
                ItemId = "prompt-1",
                References = new Dictionary<string, AICompletionReference>
                {
                    ["[doc:1]"] = new() { ReferenceId = "gen-1", IsGenerated = true },
                    ["[cite:1]"] = new() { ReferenceId = "uploaded-1", IsGenerated = false },
                },
            },
            new()
            {
                ItemId = "prompt-2",
                References = new Dictionary<string, AICompletionReference>
                {
                    ["[doc:1]"] = new() { ReferenceId = "gen-2", IsGenerated = true },
                },
            },
            new()
            {
                ItemId = "prompt-3",
                References = null,
            },
        };

        var handler = new ChatInteractionGeneratedFileCleanupHandler(cleanupService.Object);

        await handler.HistoryClearedAsync(
            new ChatInteraction { ItemId = "interaction-1" },
            prompts,
            TestContext.Current.CancellationToken);

        Assert.NotNull(capturedIds);
        Assert.Equal(["gen-1", "gen-2"], capturedIds.OrderBy(id => id));
        cleanupService.Verify(
            service => service.CleanupGeneratedDocumentsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HistoryClearedAsync_WhenNoPrompts_DoesNotCallCleanup()
    {
        var cleanupService = new Mock<IConversationDocumentCleanupService>(MockBehavior.Strict);

        var handler = new ChatInteractionGeneratedFileCleanupHandler(cleanupService.Object);

        await handler.HistoryClearedAsync(
            new ChatInteraction { ItemId = "interaction-1" },
            [],
            TestContext.Current.CancellationToken);

        cleanupService.VerifyNoOtherCalls();
    }
}
