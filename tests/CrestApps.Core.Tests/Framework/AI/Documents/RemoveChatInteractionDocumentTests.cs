using System.Reflection;
using System.Security.Claims;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI.Documents;

/// <summary>
/// The chat interaction remove endpoint deletes the stored document, its chunks and its file, so it must only do that for a
/// document that belongs to the interaction the caller may manage, as the session endpoint does. The document id comes from
/// the request body, and a document is stored under its own id whatever it belongs to.
/// </summary>
public sealed class RemoveChatInteractionDocumentTests
{
    [Fact]
    public async Task Remove_ADocumentOfAnotherInteraction_IsNotFoundAndNothingIsDeleted()
    {
        var harness = new Harness();
        harness.Interaction.Documents = [];
        harness.Store(new AIDocument { ItemId = "foreign", ReferenceId = "interaction-2", ReferenceType = AIReferenceTypes.Document.ChatInteraction, StoredFilePath = "files/foreign.pdf" });

        var result = await harness.InvokeAsync("foreign");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        harness.AssertNothingWasDeleted();
    }

    [Fact]
    public async Task Remove_ADocumentOfAChatSession_IsNotFoundAndNothingIsDeleted()
    {
        var harness = new Harness();
        harness.Store(new AIDocument { ItemId = "session-doc", ReferenceId = "session-9", ReferenceType = AIReferenceTypes.Document.ChatSession, StoredFilePath = "files/session.pdf" });

        var result = await harness.InvokeAsync("session-doc");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        harness.AssertNothingWasDeleted();
    }

    [Fact]
    public async Task Remove_ADocumentOfAProfile_IsNotFoundAndNothingIsDeleted()
    {
        var harness = new Harness();
        harness.Store(new AIDocument { ItemId = "profile-doc", ReferenceId = "interaction-1", ReferenceType = AIReferenceTypes.Document.Profile, StoredFilePath = "files/profile.pdf" });

        var result = await harness.InvokeAsync("profile-doc");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        harness.AssertNothingWasDeleted();
    }

    [Fact]
    public async Task Remove_AListedDocumentWhoseStoredCopyBelongsToSomethingElse_DropsTheEntryAndKeepsThatDocument()
    {
        var harness = new Harness();
        harness.Interaction.Documents = [new ChatDocumentInfo { DocumentId = "foreign", FileName = "b.pdf" }];
        harness.Store(new AIDocument { ItemId = "foreign", ReferenceId = "interaction-2", ReferenceType = AIReferenceTypes.Document.ChatInteraction, StoredFilePath = "files/foreign.pdf" });

        var result = await harness.InvokeAsync("foreign");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Empty(harness.Interaction.Documents);
        harness.AssertNothingWasDeleted();
    }

    [Fact]
    public async Task Remove_ADocumentOfTheInteraction_DeletesItAndItsChunksAndFile()
    {
        var harness = new Harness();
        harness.Interaction.Documents = [new ChatDocumentInfo { DocumentId = "mine", FileName = "a.pdf" }];
        harness.Store(new AIDocument { ItemId = "mine", ReferenceId = "interaction-1", ReferenceType = AIReferenceTypes.Document.ChatInteraction, StoredFilePath = "files/mine.pdf" });

        var result = await harness.InvokeAsync("mine");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Empty(harness.Interaction.Documents);
        harness.DocumentStore.Verify(s => s.DeleteAsync(It.Is<AIDocument>(d => d.ItemId == "mine"), It.IsAny<CancellationToken>()), Times.Once);
        harness.ChunkStore.Verify(c => c.DeleteByDocumentIdAsync("mine"), Times.Once);
        harness.FileStore.Verify(f => f.DeleteFileAsync("files/mine.pdf"), Times.Once);
    }

    [Fact]
    public async Task Remove_ADocumentStoredForTheInteractionThatItNoLongerListsIsStillRemoved()
    {
        // An older copy of the interaction was saved over the list; its owner can still remove what is stored for it.
        var harness = new Harness();
        harness.Interaction.Documents = [];
        harness.Store(new AIDocument { ItemId = "unlisted", ReferenceId = "interaction-1", ReferenceType = AIReferenceTypes.Document.ChatInteraction, StoredFilePath = "files/unlisted.pdf" });

        var result = await harness.InvokeAsync("unlisted");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        harness.DocumentStore.Verify(s => s.DeleteAsync(It.Is<AIDocument>(d => d.ItemId == "unlisted"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_ForAnInteractionTheCallerMayNotManage_IsForbiddenAndNothingIsDeleted()
    {
        var harness = new Harness { Authorized = false };
        harness.Interaction.Documents = [new ChatDocumentInfo { DocumentId = "mine", FileName = "a.pdf" }];
        harness.Store(new AIDocument { ItemId = "mine", ReferenceId = "interaction-1", ReferenceType = AIReferenceTypes.Document.ChatInteraction });

        var result = await harness.InvokeAsync("mine");

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        harness.AssertNothingWasDeleted();
    }

    private static int StatusOf(object result)
    {
        return Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;
    }

    private sealed class Harness
    {
        public ChatInteraction Interaction { get; } = new() { ItemId = "interaction-1", Documents = [] };

        public bool Authorized { get; init; } = true;

        public Mock<IAIDocumentStore> DocumentStore { get; } = new();

        public Mock<IAIDocumentChunkStore> ChunkStore { get; } = new();

        public Mock<IDocumentFileStore> FileStore { get; } = new();

        public void Store(AIDocument document)
        {
            DocumentStore
                .Setup(s => s.FindByIdAsync(document.ItemId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AIDocument>(document));
        }

        public void AssertNothingWasDeleted()
        {
            DocumentStore.Verify(s => s.DeleteAsync(It.IsAny<AIDocument>(), It.IsAny<CancellationToken>()), Times.Never);
            ChunkStore.Verify(c => c.DeleteByDocumentIdAsync(It.IsAny<string>()), Times.Never);
            FileStore.Verify(f => f.DeleteFileAsync(It.IsAny<string>()), Times.Never);
        }

        public async Task<object> InvokeAsync(string documentId)
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().BuildServiceProvider(),
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "editor")], "Test")),
            };

            var interactions = new Mock<ICatalogManager<ChatInteraction>>();
            interactions
                .Setup(m => m.FindByIdAsync("interaction-1", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ChatInteraction>(Interaction));

            ChunkStore.Setup(c => c.GetChunksByAIDocumentIdAsync(It.IsAny<string>())).ReturnsAsync([]);

            var authorization = new Mock<IAuthorizationService>();
            authorization
                .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .ReturnsAsync(Authorized ? AuthorizationResult.Success() : AuthorizationResult.Failed());

            var endpointType = typeof(RemoveChatInteractionDocument).GetNestedType("RemoveChatInteractionDocumentEndpoint", BindingFlags.NonPublic);
            var handle = endpointType.GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Static);
            var task = (Task<IResult>)handle.Invoke(null,
            [
                new RemoveDocumentRequest { ItemId = "interaction-1", DocumentId = documentId },
                httpContext,
                interactions.Object,
                DocumentStore.Object,
                ChunkStore.Object,
                FileStore.Object,
                authorization.Object,
                Array.Empty<IAIChatDocumentEventHandler>(),
            ]);

            return await task;
        }
    }
}
