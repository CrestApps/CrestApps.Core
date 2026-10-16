using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.ViewModels;

namespace CrestApps.Core.Tests.Framework.Blazor;

public sealed class BlazorPersonaViewModelTests
{
    [Fact]
    public void TemplateFromTemplate_ShouldReadPersonaFields()
    {
        var template = new AIProfileTemplate
        {
            Name = "FriendlyGuide",
            Source = AITemplateSources.Persona,
        };

        template.Put(new PersonaTemplateMetadata
        {
            Instructions = "You are {{ Name }}.",
            AssistantName = "Guide",
            Language = "en-US",
            VoiceName = "alloy",
            VoiceDeploymentName = "realtime-model",
            AvatarUrl = "https://example.com/guide.png",
        });

        var model = AITemplateViewModel.FromTemplate(template);

        Assert.Equal("You are {{ Name }}.", model.PersonaInstructions);
        Assert.Equal("Guide", model.PersonaAssistantName);
        Assert.Equal("en-US", model.PersonaLanguage);
        Assert.Equal("alloy", model.PersonaVoiceName);
        Assert.Equal("realtime-model", model.PersonaVoiceDeploymentName);
        Assert.Equal("https://example.com/guide.png", model.PersonaAvatarUrl);
    }

    [Fact]
    public void TemplateApplyTo_WhenPersonaHasNoVoice_ShouldClearVoiceDeployment()
    {
        var model = new AITemplateViewModel
        {
            Name = "FriendlyGuide",
            Source = AITemplateSources.Persona,
            PersonaInstructions = "  Be kind.  ",
            PersonaAssistantName = " ",
            PersonaLanguage = " fr-FR ",
            PersonaVoiceDeploymentName = "tts-model",
            PersonaVoiceName = "",
        };

        var template = new AIProfileTemplate();

        model.ApplyTo(template);

        Assert.True(template.TryGet<PersonaTemplateMetadata>(out var metadata));
        Assert.Equal("Be kind.", metadata.Instructions);
        Assert.Null(metadata.AssistantName);
        Assert.Equal("fr-FR", metadata.Language);
        Assert.Null(metadata.VoiceName);
        Assert.Null(metadata.VoiceDeploymentName);
        Assert.Null(metadata.AvatarUrl);
    }

    [Fact]
    public void TemplateApplyTo_WhenPersonaHasVoice_ShouldKeepVoiceDeployment()
    {
        var model = new AITemplateViewModel
        {
            Source = AITemplateSources.Persona,
            PersonaInstructions = "Be kind.",
            PersonaVoiceDeploymentName = "tts-model",
            PersonaVoiceName = "en-US-AvaNeural",
        };

        var template = new AIProfileTemplate();

        model.ApplyTo(template);

        Assert.True(template.TryGet<PersonaTemplateMetadata>(out var metadata));
        Assert.Equal("en-US-AvaNeural", metadata.VoiceName);
        Assert.Equal("tts-model", metadata.VoiceDeploymentName);
    }

    [Fact]
    public void TemplateRoundTrip_ForProfileSource_ShouldKeepPersonaLinkAndParameters()
    {
        var template = new AIProfileTemplate
        {
            Source = AITemplateSources.Profile,
        };

        template.Put(new AIPersonaMetadata
        {
            PersonaId = "FriendlyGuide",
            Parameters = new Dictionary<string, object> { ["Name"] = "Guide" },
        });

        var model = AITemplateViewModel.FromTemplate(template);

        Assert.Equal("FriendlyGuide", model.PersonaId);

        model.PersonaId = "CalmExpert";
        model.ApplyTo(template);

        Assert.True(template.TryGet<AIPersonaMetadata>(out var metadata));
        Assert.Equal("CalmExpert", metadata.PersonaId);
        Assert.Equal("Guide", metadata.Parameters["Name"]?.ToString());
    }

    [Fact]
    public void TemplateApplyTo_ForProfileSourceWithoutPersona_ShouldNotAddPersonaLink()
    {
        var model = new AITemplateViewModel
        {
            Source = AITemplateSources.Profile,
            PersonaId = " ",
        };

        var template = new AIProfileTemplate();

        model.ApplyTo(template);

        Assert.False(template.TryGet<AIPersonaMetadata>(out _));
    }

    [Fact]
    public void TemplateApplyTo_WhenSourceIsNotProfile_ShouldRemovePersonaLink()
    {
        var template = new AIProfileTemplate
        {
            Source = AITemplateSources.Profile,
        };

        template.Put(new AIPersonaMetadata { PersonaId = "FriendlyGuide" });

        var model = new AITemplateViewModel
        {
            Source = AITemplateSources.SystemPrompt,
            SystemMessage = "Hello",
        };

        model.ApplyTo(template);

        Assert.False(template.TryGet<AIPersonaMetadata>(out _));
    }

    [Fact]
    public void ProfileRoundTrip_ShouldKeepPersonaLinkAndParameters()
    {
        var profile = new AIProfile
        {
            Name = "Support",
        };

        profile.Put(new AIPersonaMetadata
        {
            PersonaId = "FriendlyGuide",
            Parameters = new Dictionary<string, object> { ["Name"] = "Guide" },
        });

        var model = AIProfileViewModel.FromProfile(profile);

        Assert.Equal("FriendlyGuide", model.PersonaId);

        var created = new AIProfile();

        model.ApplyTo(created);

        Assert.True(created.TryGet<AIPersonaMetadata>(out var metadata));
        Assert.Equal("FriendlyGuide", metadata.PersonaId);
        Assert.Equal("Guide", metadata.Parameters["Name"]?.ToString());
    }

    [Fact]
    public void ProfileApplyTo_WhenPersonaCleared_ShouldStoreNullPersonaId()
    {
        var profile = new AIProfile();

        profile.Put(new AIPersonaMetadata { PersonaId = "FriendlyGuide" });

        var model = AIProfileViewModel.FromProfile(profile);

        model.PersonaId = string.Empty;
        model.ApplyTo(profile);

        Assert.True(profile.TryGet<AIPersonaMetadata>(out var metadata));
        Assert.Null(metadata.PersonaId);
    }

    [Fact]
    public void PersonaSelectList_ShouldSortByCategoryThenDisplayTextAndKeepMissingSelection()
    {
        var personas = new[]
        {
            CreatePersona("Zed", "Zed persona", "Support", voiceName: "alloy"),
            CreatePersona("Amy", "Amy persona", "Support"),
            CreatePersona("Bob", "Bob persona", null),
        };

        var items = PersonaSelectList.Build(personas, "Deleted");

        Assert.Equal(["Bob", "Amy", "Zed", "Deleted"], items.Select(item => item.PersonaId));
        Assert.Equal("alloy", items.Single(item => item.PersonaId == "Zed").VoiceName);
        Assert.True(items.Single(item => item.PersonaId == "Deleted").IsMissing);
        Assert.DoesNotContain(items, item => item.IsMissing && item.PersonaId != "Deleted");
    }

    [Fact]
    public void PersonaSelectList_WhenSelectionExists_ShouldNotAddMissingOption()
    {
        var items = PersonaSelectList.Build([CreatePersona("Amy", "Amy persona", null)], "amy");

        Assert.Single(items);
        Assert.False(items[0].IsMissing);
    }

    private static AIProfileTemplate CreatePersona(string name, string displayText, string category, string voiceName = null)
    {
        var template = new AIProfileTemplate
        {
            Name = name,
            DisplayText = displayText,
            Category = category,
            Source = AITemplateSources.Persona,
        };

        template.Put(new PersonaTemplateMetadata
        {
            Instructions = "Be kind.",
            VoiceName = voiceName,
        });

        return template;
    }
}
