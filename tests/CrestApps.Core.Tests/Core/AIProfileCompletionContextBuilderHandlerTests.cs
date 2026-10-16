using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;
using Moq;

namespace CrestApps.Core.Tests.Core;

public sealed class AIProfileCompletionContextBuilderHandlerTests
{
    [Fact]
    public async Task BuildingAsync_WithPersona_PlacesPersonaInstructionsFirst()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(service => service.RenderAsync("template-1", It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync("Rendered template");

        var persona = new AIPersona
        {
            Id = "friendly",
            Instructions = "You are Ava, a warm assistant.",
        };
        var personaResolver = new Mock<IAIPersonaResolver>();
        personaResolver
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<ExtensibleEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        var handler = new AIProfileCompletionContextBuilderHandler(templateService.Object, personaResolver.Object);
        var profile = new AIProfile();
        profile.Put(new AIProfileMetadata { SystemMessage = "Answer billing questions only." });
        profile.Put(new PromptTemplateMetadata
        {
            Templates = [new PromptTemplateSelectionEntry { TemplateId = "template-1" }],
        });

        var completionContext = new AICompletionContext();

        await handler.BuildingAsync(new AICompletionContextBuildingContext(profile, completionContext));

        Assert.Equal("You are Ava, a warm assistant.\n\nRendered template\n\nAnswer billing questions only.", completionContext.SystemMessage);
        Assert.Same(persona, completionContext.AdditionalProperties[AICompletionContextKeys.Persona]);
    }

    [Fact]
    public async Task BuildingAsync_WithPersonaAndNoProfileMetadata_UsesPersonaAlone()
    {
        var personaResolver = new Mock<IAIPersonaResolver>();
        personaResolver
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<ExtensibleEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIPersona { Id = "friendly", Instructions = "You are Ava." });

        var handler = new AIProfileCompletionContextBuilderHandler(Mock.Of<ITemplateService>(), personaResolver.Object);
        var completionContext = new AICompletionContext();

        await handler.BuildingAsync(new AICompletionContextBuildingContext(new AIProfile(), completionContext));

        Assert.Equal("You are Ava.", completionContext.SystemMessage);
    }

    [Fact]
    public async Task BuildingAsync_WithoutPersona_LeavesSystemMessageUnchanged()
    {
        var handler = new AIProfileCompletionContextBuilderHandler(Mock.Of<ITemplateService>(), Mock.Of<IAIPersonaResolver>());
        var profile = new AIProfile();
        profile.Put(new AIProfileMetadata { SystemMessage = "Answer billing questions only." });
        var completionContext = new AICompletionContext();

        await handler.BuildingAsync(new AICompletionContextBuildingContext(profile, completionContext));

        Assert.Equal("Answer billing questions only.", completionContext.SystemMessage);
        Assert.False(completionContext.AdditionalProperties.ContainsKey(AICompletionContextKeys.Persona));
    }

    [Fact]
    public async Task BuildingAsync_ProfileMetadataStoredInProperties_PopulatesCompletionContext()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(service => service.RenderAsync("template-1", It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync("Rendered template");

        var handler = new AIProfileCompletionContextBuilderHandler(templateService.Object, Mock.Of<IAIPersonaResolver>());
        var profile = new AIProfile
        {
            ChatDeploymentName = "chat-deployment",
            UtilityDeploymentName = "utility-deployment",
        };

        profile.Put(new AIProfileMetadata
        {
            SystemMessage = "Base system message",
            Temperature = 0.4f,
            TopP = 0.8f,
            FrequencyPenalty = 0.2f,
            PresencePenalty = 0.1f,
            MaxTokens = 256,
            PastMessagesCount = 12,
            UseCaching = false,
        });
        profile.Put(new FunctionInvocationMetadata { Names = ["property-tool"] });
        profile.Put(new AgentInvocationMetadata { Names = ["property-agent"] });
        profile.Put(new PromptTemplateMetadata
        {
            Templates =
            [
                new PromptTemplateSelectionEntry
                {
                    TemplateId = "template-1",
                    Parameters = new Dictionary<string, object>
                    {
                        ["mode"] = "property",
                    },
                },
            ],
        });

        profile.WithSettings(new AIProfileMetadata
        {
            SystemMessage = "Settings system message",
            Temperature = 0.9f,
            UseCaching = true,
        });
        profile.WithSettings(new FunctionInvocationMetadata { Names = ["settings-tool"] });

        var completionContext = new AICompletionContext();
        var buildingContext = new AICompletionContextBuildingContext(profile, completionContext);

        await handler.BuildingAsync(buildingContext);

        Assert.Equal("chat-deployment", completionContext.ChatDeploymentName);
        Assert.Equal("utility-deployment", completionContext.UtilityDeploymentName);
        Assert.Equal("Rendered template\n\nBase system message", completionContext.SystemMessage);
        Assert.Equal(0.4f, completionContext.Temperature);
        Assert.Equal(0.8f, completionContext.TopP);
        Assert.Equal(0.2f, completionContext.FrequencyPenalty);
        Assert.Equal(0.1f, completionContext.PresencePenalty);
        Assert.Equal(256, completionContext.MaxTokens);
        Assert.Equal(12, completionContext.PastMessagesCount);
        Assert.False(completionContext.UseCaching);
        Assert.Equal(["property-tool"], completionContext.ToolNames);
        Assert.Equal(["property-agent"], completionContext.AgentNames);
    }
}
