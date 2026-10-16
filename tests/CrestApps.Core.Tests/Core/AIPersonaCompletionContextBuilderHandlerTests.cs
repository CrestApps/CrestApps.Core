using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;
using Moq;

namespace CrestApps.Core.Tests.Core;

public sealed class AIPersonaCompletionContextBuilderHandlerTests
{
    [Fact]
    public async Task BuiltAsync_ProfileWithPersona_PlacesPersonaInstructionsFirst()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(service => service.RenderAsync("template-1", It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync("Rendered template");

        var persona = new ResolvedAIPersona
        {
            Id = "friendly",
            Instructions = "You are Ava, a warm assistant.",
        };
        var profileHandler = new AIProfileCompletionContextBuilderHandler(templateService.Object);
        var personaHandler = new AIPersonaCompletionContextBuilderHandler(CreateResolver(persona));
        var profile = new AIProfile();
        profile.Put(new AIProfileMetadata { SystemMessage = "Answer billing questions only." });
        profile.Put(new PromptTemplateMetadata
        {
            Templates = [new PromptTemplateSelectionEntry { TemplateId = "template-1" }],
        });

        var completionContext = new AICompletionContext();

        await profileHandler.BuildingAsync(new AICompletionContextBuildingContext(profile, completionContext));
        await personaHandler.BuiltAsync(new AICompletionContextBuiltContext(profile, completionContext));

        Assert.Equal("You are Ava, a warm assistant.\n\nRendered template\n\nAnswer billing questions only.", completionContext.SystemMessage);
        Assert.Same(persona, completionContext.AdditionalProperties[AICompletionContextKeys.Persona]);
    }

    [Fact]
    public async Task BuiltAsync_ProfileWithoutSystemMessage_UsesPersonaAlone()
    {
        var handler = new AIPersonaCompletionContextBuilderHandler(CreateResolver(new ResolvedAIPersona { Id = "friendly", Instructions = "You are Ava." }));
        var completionContext = new AICompletionContext();

        await handler.BuiltAsync(new AICompletionContextBuiltContext(new AIProfile(), completionContext));

        Assert.Equal("You are Ava.", completionContext.SystemMessage);
    }

    [Fact]
    public async Task BuiltAsync_WithoutPersona_LeavesSystemMessageUnchanged()
    {
        var handler = new AIPersonaCompletionContextBuilderHandler(CreateResolver(null));
        var completionContext = new AICompletionContext
        {
            SystemMessage = "Answer billing questions only.",
        };

        await handler.BuiltAsync(new AICompletionContextBuiltContext(new AIProfile(), completionContext));

        Assert.Equal("Answer billing questions only.", completionContext.SystemMessage);
        Assert.False(completionContext.AdditionalProperties.ContainsKey(AICompletionContextKeys.Persona));
    }

    [Fact]
    public async Task BuiltAsync_ResourceThatCannotLinkAPersona_IsIgnored()
    {
        var resolver = new Mock<IAIPersonaResolver>(MockBehavior.Strict);
        var handler = new AIPersonaCompletionContextBuilderHandler(resolver.Object);
        var completionContext = new AICompletionContext
        {
            SystemMessage = "Plain request.",
        };

        await handler.BuiltAsync(new AICompletionContextBuiltContext("not an entity", completionContext));

        Assert.Equal("Plain request.", completionContext.SystemMessage);
    }

    [Fact]
    public async Task BuiltAsync_PersonaWithAssistantName_NamesTheAssistantFirst()
    {
        var handler = new AIPersonaCompletionContextBuilderHandler(CreateResolver(new ResolvedAIPersona
        {
            Id = "friendly",
            AssistantName = "Ava",
            Instructions = "Be warm and concise.",
        }));
        var completionContext = new AICompletionContext
        {
            SystemMessage = "Answer billing questions only.",
        };

        await handler.BuiltAsync(new AICompletionContextBuiltContext(new AIProfile(), completionContext));

        Assert.Equal("Your name is Ava.\nBe warm and concise.\n\nAnswer billing questions only.", completionContext.SystemMessage);
    }

    [Fact]
    public async Task BuiltAsync_PersonaWithOnlyAnAssistantName_StillNamesTheAssistant()
    {
        var handler = new AIPersonaCompletionContextBuilderHandler(CreateResolver(new ResolvedAIPersona
        {
            Id = "named",
            AssistantName = "Ava",
        }));
        var completionContext = new AICompletionContext
        {
            SystemMessage = "Answer billing questions only.",
        };

        await handler.BuiltAsync(new AICompletionContextBuiltContext(new AIProfile(), completionContext));

        Assert.Equal("Your name is Ava.\n\nAnswer billing questions only.", completionContext.SystemMessage);
    }

    private static IAIPersonaResolver CreateResolver(ResolvedAIPersona persona)
    {
        var resolver = new Mock<IAIPersonaResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ExtensibleEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        return resolver.Object;
    }
}
