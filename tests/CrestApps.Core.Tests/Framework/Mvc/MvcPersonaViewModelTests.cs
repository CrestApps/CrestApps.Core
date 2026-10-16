using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class MvcPersonaViewModelTests
{
    [Fact]
    public void ApplyTo_PersonaSource_StoresPersonaMetadata()
    {
        var model = new AITemplateViewModel
        {
            Name = "front-desk",
            Source = AITemplateSources.Persona,
            PersonaInstructions = "You are Ava.",
            PersonaAssistantName = " Ava ",
            PersonaLanguage = "en-US",
            PersonaVoiceDeploymentName = "realtime",
            PersonaVoiceName = "marin",
            PersonaAvatarUrl = " ",
        };
        var template = new AIProfileTemplate();

        model.ApplyTo(template);

        Assert.Equal(AITemplateSources.Persona, template.Source);
        Assert.True(template.TryGet<PersonaTemplateMetadata>(out var persona));
        Assert.Equal("You are Ava.", persona.Instructions);
        Assert.Equal("Ava", persona.AssistantName);
        Assert.Equal("en-US", persona.Language);
        Assert.Equal("marin", persona.VoiceName);
        Assert.Equal("realtime", persona.VoiceDeploymentName);
        Assert.Null(persona.AvatarUrl);
        Assert.False(template.TryGet<SystemPromptTemplateMetadata>(out _));
    }

    [Fact]
    public void ApplyTo_PersonaWithoutVoice_DropsTheVoiceDeployment()
    {
        var model = new AITemplateViewModel
        {
            Source = AITemplateSources.Persona,
            PersonaInstructions = "You are Ava.",
            PersonaVoiceDeploymentName = "realtime",
        };
        var template = new AIProfileTemplate();

        model.ApplyTo(template);

        var persona = template.GetOrCreate<PersonaTemplateMetadata>();
        Assert.Null(persona.VoiceName);
        Assert.Null(persona.VoiceDeploymentName);
    }

    [Fact]
    public void FromTemplate_PersonaSource_RoundTrips()
    {
        var template = new AIProfileTemplate { Source = AITemplateSources.Persona };
        template.Put(new PersonaTemplateMetadata
        {
            Instructions = "You are Ava.",
            AssistantName = "Ava",
            VoiceName = "marin",
            VoiceDeploymentName = "realtime",
        });

        var model = AITemplateViewModel.FromTemplate(template);

        Assert.Equal("You are Ava.", model.PersonaInstructions);
        Assert.Equal("Ava", model.PersonaAssistantName);
        Assert.Equal("marin", model.PersonaVoiceName);
        Assert.Equal("realtime", model.PersonaVoiceDeploymentName);
    }

    [Fact]
    public void ProfileTemplate_LinksAndUnlinksThePersona()
    {
        var template = new AIProfileTemplate { Source = AITemplateSources.Profile };

        new AITemplateViewModel { Source = AITemplateSources.Profile, PersonaId = "front-desk" }.ApplyTo(template);

        Assert.Equal("front-desk", AITemplateViewModel.FromTemplate(template).PersonaId);

        new AITemplateViewModel { Source = AITemplateSources.Profile, PersonaId = "" }.ApplyTo(template);

        Assert.Null(template.GetOrCreate<AIPersonaMetadata>().PersonaId);
    }

    [Fact]
    public void Profile_LinksThePersonaAndKeepsItsParameters()
    {
        var profile = new AIProfile();
        profile.Put(new AIPersonaMetadata
        {
            PersonaId = "old",
            Parameters = new Dictionary<string, object> { ["company"] = "Contoso" },
        });

        var model = AIProfileViewModel.FromProfile(profile);
        Assert.Equal("old", model.PersonaId);

        model.PersonaId = " front-desk ";
        model.ApplyTo(profile);

        var metadata = profile.GetOrCreate<AIPersonaMetadata>();
        Assert.Equal("front-desk", metadata.PersonaId);
        Assert.Equal("Contoso", metadata.Parameters["company"]?.ToString());
    }
}
