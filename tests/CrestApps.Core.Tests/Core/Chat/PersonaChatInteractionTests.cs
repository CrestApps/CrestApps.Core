using System.Text.Json;
using CrestApps.Core.AI.Chat.Handlers;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;
using Moq;

namespace CrestApps.Core.Tests.Core.Chat;

public sealed class PersonaChatInteractionTests
{
    [Fact]
    public async Task UpdatingAsync_WithPersonaId_LinksThePersona()
    {
        var handler = new PersonaChatInteractionSettingsHandler();
        using var document = JsonDocument.Parse("""{"personaId":" friendly-assistant "}""");
        var interaction = new ChatInteraction();

        await handler.UpdatingAsync(interaction, document.RootElement, TestContext.Current.CancellationToken);

        Assert.True(interaction.TryGet<AIPersonaMetadata>(out var metadata));
        Assert.Equal("friendly-assistant", metadata.PersonaId);
    }

    [Fact]
    public async Task UpdatingAsync_WithEmptyPersonaId_UnlinksThePersona()
    {
        var handler = new PersonaChatInteractionSettingsHandler();
        using var document = JsonDocument.Parse("""{"personaId":""}""");
        var interaction = new ChatInteraction();
        interaction.Put(new AIPersonaMetadata { PersonaId = "friendly-assistant" });

        await handler.UpdatingAsync(interaction, document.RootElement, TestContext.Current.CancellationToken);

        Assert.Null(interaction.GetOrCreate<AIPersonaMetadata>().PersonaId);
    }

    [Fact]
    public async Task UpdatingAsync_WithoutPersonaKey_KeepsTheCurrentLink()
    {
        var handler = new PersonaChatInteractionSettingsHandler();
        using var document = JsonDocument.Parse("""{"title":"Untitled"}""");
        var interaction = new ChatInteraction();
        interaction.Put(new AIPersonaMetadata { PersonaId = "friendly-assistant" });

        await handler.UpdatingAsync(interaction, document.RootElement, TestContext.Current.CancellationToken);

        Assert.Equal("friendly-assistant", interaction.GetOrCreate<AIPersonaMetadata>().PersonaId);
    }

    [Fact]
    public async Task BuiltAsync_InteractionWithPersona_PlacesPersonaInstructionsFirst()
    {
        var persona = new ResolvedAIPersona { Id = "friendly", Instructions = "You are Ava." };
        var personaResolver = new Mock<IAIPersonaResolver>();
        personaResolver
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<ExtensibleEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        var interactionHandler = new ChatInteractionCompletionContextBuilderHandler(Mock.Of<ITemplateService>());
        var personaHandler = new AIPersonaCompletionContextBuilderHandler(personaResolver.Object);
        var interaction = new ChatInteraction
        {
            ItemId = "interaction-1",
            SystemMessage = "Help with orders.",
        };
        var completionContext = new AICompletionContext();

        // The interaction builds its own system message first; the persona handler leads it once it is built.
        await interactionHandler.BuildingAsync(new AICompletionContextBuildingContext(interaction, completionContext));
        await personaHandler.BuiltAsync(new AICompletionContextBuiltContext(interaction, completionContext));

        Assert.Equal("You are Ava.\n\nHelp with orders.", completionContext.SystemMessage);
        Assert.Same(persona, completionContext.AdditionalProperties[AICompletionContextKeys.Persona]);
    }
}
