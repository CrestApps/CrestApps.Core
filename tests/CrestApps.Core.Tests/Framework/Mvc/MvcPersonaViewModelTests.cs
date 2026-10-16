using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class MvcPersonaViewModelTests
{
    [Fact]
    public void ToJson_SendsTheFormFieldsAndClearsEmptyOnes()
    {
        var model = new AIPersonaViewModel
        {
            Name = " front-desk ",
            DisplayText = " Front desk ",
            Instructions = "You are Ava.",
            AssistantName = " Ava ",
            Language = "en-US",
            VoiceDeploymentName = "realtime",
            VoiceName = "marin",
            AvatarUrl = " ",
        };

        var json = model.ToJson();

        Assert.Equal("front-desk", json[nameof(AIPersona.Name)]!.GetValue<string>());
        Assert.Equal("Front desk", json[nameof(AIPersona.DisplayText)]!.GetValue<string>());
        Assert.Equal("You are Ava.", json[nameof(AIPersona.Instructions)]!.GetValue<string>());
        Assert.Equal("Ava", json[nameof(AIPersona.AssistantName)]!.GetValue<string>());
        Assert.Equal("marin", json[nameof(AIPersona.VoiceName)]!.GetValue<string>());
        Assert.Equal("realtime", json[nameof(AIPersona.VoiceDeploymentName)]!.GetValue<string>());

        // An empty value is sent rather than left out, so clearing a field on the form clears it on the persona.
        Assert.Equal(string.Empty, json[nameof(AIPersona.AvatarUrl)]!.GetValue<string>());
        Assert.Equal(string.Empty, json[nameof(AIPersona.Description)]!.GetValue<string>());
    }

    [Fact]
    public void ToJson_WithoutVoice_DropsTheVoiceDeployment()
    {
        var json = new AIPersonaViewModel
        {
            Name = "ava",
            Instructions = "You are Ava.",
            VoiceDeploymentName = "realtime",
        }.ToJson();

        Assert.Equal(string.Empty, json[nameof(AIPersona.VoiceDeploymentName)]!.GetValue<string>());
    }

    [Fact]
    public void FromPersona_MapsThePersona()
    {
        var persona = new AIPersona
        {
            ItemId = "saved-1",
            Name = "ava",
            DisplayText = "Ava",
            Instructions = "You are Ava.",
            AssistantName = "Ava",
            VoiceName = "marin",
            VoiceDeploymentName = "realtime",
            AvatarUrl = "/images/ava.png",
        };

        var saved = AIPersonaViewModel.FromPersona(persona, isFromFile: false);
        var fromFile = AIPersonaViewModel.FromPersona(persona, isFromFile: true);

        Assert.Equal("saved-1", saved.ItemId);
        Assert.Equal("ava", saved.Name);
        Assert.Equal("You are Ava.", saved.Instructions);
        Assert.Equal("marin", saved.VoiceName);
        Assert.Equal("realtime", saved.VoiceDeploymentName);
        Assert.Equal("/images/ava.png", saved.AvatarUrl);
        Assert.False(saved.IsFromFile);

        // A file-based persona has no saved identifier: saving it creates the copy that replaces the file.
        Assert.Null(fromFile.ItemId);
        Assert.True(fromFile.IsFromFile);
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
