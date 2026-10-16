using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.ViewModels;

namespace CrestApps.Core.Tests.Framework.Blazor;

public sealed class BlazorPersonaViewModelTests
{
    [Fact]
    public void PersonaFromPersona_ShouldReadPersonaFields()
    {
        var persona = new AIPersona
        {
            ItemId = "persona-1",
            Name = "FriendlyGuide",
            DisplayText = "Friendly guide",
            Description = "A patient guide.",
            Instructions = "You are {{ Name }}.",
            AssistantName = "Guide",
            Language = "en-US",
            VoiceName = "alloy",
            VoiceDeploymentName = "realtime-model",
            AvatarUrl = "https://example.com/guide.png",
        };

        var model = AIPersonaViewModel.FromPersona(persona, isFileBased: true);

        Assert.Equal("persona-1", model.ItemId);
        Assert.Equal("FriendlyGuide", model.Name);
        Assert.Equal("Friendly guide", model.DisplayText);
        Assert.Equal("A patient guide.", model.Description);
        Assert.Equal("You are {{ Name }}.", model.Instructions);
        Assert.Equal("Guide", model.AssistantName);
        Assert.Equal("en-US", model.Language);
        Assert.Equal("alloy", model.VoiceName);
        Assert.Equal("realtime-model", model.VoiceDeploymentName);
        Assert.Equal("https://example.com/guide.png", model.AvatarUrl);
        Assert.True(model.IsFileBased);
    }

    [Fact]
    public void PersonaToJson_WhenPersonaHasNoVoice_ShouldClearVoiceDeployment()
    {
        var model = new AIPersonaViewModel
        {
            Name = " FriendlyGuide ",
            Instructions = "  Be kind.  ",
            AssistantName = " ",
            Language = " fr-FR ",
            VoiceDeploymentName = "tts-model",
            VoiceName = "",
        };

        var json = model.ToJson();

        Assert.Equal("FriendlyGuide", json[nameof(AIPersona.Name)]?.GetValue<string>());
        Assert.Equal("Be kind.", json[nameof(AIPersona.Instructions)]?.GetValue<string>());
        Assert.Equal("fr-FR", json[nameof(AIPersona.Language)]?.GetValue<string>());

        // Cleared fields are present as null so an update clears them on the persona.
        Assert.True(json.ContainsKey(nameof(AIPersona.AssistantName)));
        Assert.Null(json[nameof(AIPersona.AssistantName)]);
        Assert.True(json.ContainsKey(nameof(AIPersona.VoiceName)));
        Assert.Null(json[nameof(AIPersona.VoiceName)]);
        Assert.True(json.ContainsKey(nameof(AIPersona.VoiceDeploymentName)));
        Assert.Null(json[nameof(AIPersona.VoiceDeploymentName)]);
        Assert.True(json.ContainsKey(nameof(AIPersona.AvatarUrl)));
        Assert.Null(json[nameof(AIPersona.AvatarUrl)]);
    }

    [Fact]
    public void PersonaToJson_WhenPersonaHasVoice_ShouldKeepVoiceDeployment()
    {
        var model = new AIPersonaViewModel
        {
            Name = "FriendlyGuide",
            Instructions = "Be kind.",
            VoiceDeploymentName = "tts-model",
            VoiceName = "en-US-AvaNeural",
        };

        var json = model.ToJson();

        Assert.Equal("en-US-AvaNeural", json[nameof(AIPersona.VoiceName)]?.GetValue<string>());
        Assert.Equal("tts-model", json[nameof(AIPersona.VoiceDeploymentName)]?.GetValue<string>());
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
        Assert.True(template.TryGet<SystemPromptTemplateMetadata>(out var systemPrompt));
        Assert.Equal("Hello", systemPrompt.SystemMessage);
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
    public void PersonaSelectList_ShouldSortByDisplayTextAndKeepMissingSelection()
    {
        var personas = new[]
        {
            CreatePersona("Zed", "Zed persona", voiceName: "alloy"),
            CreatePersona("Amy", "Amy persona"),
            CreatePersona("Bob", null),
        };

        var items = PersonaSelectList.Build(personas, "Deleted");

        Assert.Equal(["Amy", "Bob", "Zed", "Deleted"], items.Select(item => item.PersonaId));
        Assert.Equal("Bob", items.Single(item => item.PersonaId == "Bob").DisplayText);
        Assert.Equal("alloy", items.Single(item => item.PersonaId == "Zed").VoiceName);
        Assert.Null(items.Single(item => item.PersonaId == "Amy").VoiceName);
        Assert.True(items.Single(item => item.PersonaId == "Deleted").IsMissing);
        Assert.DoesNotContain(items, item => item.IsMissing && item.PersonaId != "Deleted");
    }

    [Fact]
    public void PersonaSelectList_WhenSelectionExists_ShouldNotAddMissingOption()
    {
        var items = PersonaSelectList.Build([CreatePersona("Amy", "Amy persona")], "amy");

        Assert.Single(items);
        Assert.False(items[0].IsMissing);
    }

    [Fact]
    public void PersonaSelectList_WhenNamesRepeat_ShouldKeepFirstPersona()
    {
        var items = PersonaSelectList.Build(
            [
                CreatePersona("Amy", "Saved Amy"),
                CreatePersona("amy", "File Amy"),
            ],
            null);

        var item = Assert.Single(items);
        Assert.Equal("Saved Amy", item.DisplayText);
    }

    private static AIPersona CreatePersona(string name, string displayText, string voiceName = null)
    {
        return new AIPersona
        {
            ItemId = name,
            Name = name,
            DisplayText = displayText,
            Instructions = "Be kind.",
            VoiceName = voiceName,
        };
    }
}
