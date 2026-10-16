using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Models;

public sealed class AIChatAssistantDisplayTests
{
    [Fact]
    public void ToChatAssistantDisplay_NoPersona_ReturnsDefault()
    {
        ResolvedAIPersona persona = null;

        var display = persona.ToChatAssistantDisplay();

        Assert.Same(AIChatAssistantDisplay.Default, display);
        Assert.Null(display.AssistantName);
        Assert.Null(display.AvatarUrl);
    }

    [Fact]
    public void ToChatAssistantDisplay_Persona_MapsNameAndAvatar()
    {
        var display = CreatePersona().ToChatAssistantDisplay();

        Assert.Equal("Ava", display.AssistantName);
        Assert.Equal("/media/personas/ava.png", display.AvatarUrl);
    }

    [Fact]
    public void ToChatAssistantDisplay_BlankPersonaValues_AreNull()
    {
        var persona = new ResolvedAIPersona
        {
            Id = "plain",
            AssistantName = "  ",
            AvatarUrl = "",
        };

        var display = persona.ToChatAssistantDisplay();

        Assert.Null(display.AssistantName);
        Assert.Null(display.AvatarUrl);
    }

    [Fact]
    public void ToChatAssistantDisplay_TrimsValues()
    {
        var persona = new ResolvedAIPersona
        {
            Id = "padded",
            AssistantName = " Ava ",
            AvatarUrl = " https://example.com/ava.png ",
        };

        var display = persona.ToChatAssistantDisplay();

        Assert.Equal("Ava", display.AssistantName);
        Assert.Equal("https://example.com/ava.png", display.AvatarUrl);
    }

    [Fact]
    public void GetAssistantLabel_WithoutAssistantName_ReturnsDefaultLabel()
    {
        Assert.Equal("Assistant", AIChatAssistantDisplay.Default.GetAssistantLabel("Assistant"));
    }

    [Fact]
    public void GetAssistantLabel_WithAssistantName_ReturnsName()
    {
        var display = CreatePersona().ToChatAssistantDisplay();

        Assert.Equal("Ava", display.GetAssistantLabel("Assistant"));
    }

    [Fact]
    public async Task ResolveChatAssistantDisplayAsync_LinkedPersona_MapsResolvedPersona()
    {
        var profile = new AIProfile();
        var resolver = new Mock<IAIPersonaResolver>();
        resolver
            .Setup(r => r.ResolveAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePersona());

        var display = await resolver.Object.ResolveChatAssistantDisplayAsync(profile, TestContext.Current.CancellationToken);

        Assert.Equal("Ava", display.AssistantName);
        Assert.Equal("/media/personas/ava.png", display.AvatarUrl);
    }

    [Fact]
    public async Task ResolveChatAssistantDisplayAsync_NoPersona_ReturnsDefault()
    {
        var interaction = new ChatInteraction();
        var resolver = new Mock<IAIPersonaResolver>();
        resolver
            .Setup(r => r.ResolveAsync(interaction, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedAIPersona)null);

        var display = await resolver.Object.ResolveChatAssistantDisplayAsync(interaction, TestContext.Current.CancellationToken);

        Assert.Same(AIChatAssistantDisplay.Default, display);
    }

    [Fact]
    public async Task ResolveChatAssistantDisplayAsync_NullResource_ReturnsDefaultWithoutResolving()
    {
        var resolver = new Mock<IAIPersonaResolver>(MockBehavior.Strict);

        var display = await resolver.Object.ResolveChatAssistantDisplayAsync(null, TestContext.Current.CancellationToken);

        Assert.Same(AIChatAssistantDisplay.Default, display);
    }

    [Fact]
    public async Task ResolveChatAssistantDisplayAsync_ServiceProviderWithoutPersonas_ReturnsDefault()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var profile = new AIProfile();
        profile.Put(new AIPersonaMetadata { PersonaId = "ava" });

        var display = await services.ResolveChatAssistantDisplayAsync(profile, TestContext.Current.CancellationToken);

        Assert.Same(AIChatAssistantDisplay.Default, display);
    }

    [Fact]
    public async Task ResolveChatAssistantDisplayAsync_ServiceProviderWithPersonas_MapsResolvedPersona()
    {
        var profile = new AIProfile();
        var resolver = new Mock<IAIPersonaResolver>();
        resolver
            .Setup(r => r.ResolveAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePersona());
        var services = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();

        var display = await services.ResolveChatAssistantDisplayAsync(profile, TestContext.Current.CancellationToken);

        Assert.Equal("Ava", display.AssistantName);
        Assert.Equal("/media/personas/ava.png", display.AvatarUrl);
    }

    private static ResolvedAIPersona CreatePersona()
        => new()
        {
            Id = "ava",
            DisplayText = "Ava",
            Instructions = "You are Ava.",
            AssistantName = "Ava",
            AvatarUrl = "/media/personas/ava.png",
        };
}
