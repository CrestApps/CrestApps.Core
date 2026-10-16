using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Parsing;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class EmbeddedResourceAIProfileTemplateProviderTests
{
    [Fact]
    public async Task GetTemplatesAsync_DiscoversAllEmbeddedProfileTemplates()
    {
        var provider = new EmbeddedResourceAIProfileTemplateProvider(
            typeof(AI.ServiceCollectionExtensions).Assembly,
            [new DefaultMarkdownTemplateParser()]);

        var templates = await provider.GetTemplatesAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(templates);
        Assert.Contains(templates, template => template.ItemId == "chat-session-summarizer");
        Assert.All(templates, template => Assert.Equal(AITemplateSources.Profile, template.Source));
    }

    [Fact]
    public async Task GetTemplatesAsync_MapsFrontMatterToProfileMetadata()
    {
        var provider = new EmbeddedResourceAIProfileTemplateProvider(
            typeof(AI.ServiceCollectionExtensions).Assembly,
            [new DefaultMarkdownTemplateParser()]);

        var templates = await provider.GetTemplatesAsync(TestContext.Current.CancellationToken);
        var template = templates.Single(t => t.ItemId == "chat-session-summarizer");
        var metadata = template.GetOrCreate<ProfileTemplateMetadata>();

        Assert.Equal(AIProfileType.TemplatePrompt, metadata.ProfileType);
        Assert.Equal(0.3f, metadata.Temperature);
        Assert.Equal("Summarizes the current chat session into a concise summary with key points and action items.", template.Description);
    }

    [Fact]
    public void Parse_WhenSourceIsSystemPrompt_MapsBodyToSystemPromptMetadata()
    {
        var parseResult = new TemplateParseResult
        {
            Body = "You are a reusable system prompt.",
            Metadata = new TemplateMetadata
            {
                Title = "Reusable System Prompt",
                AdditionalProperties =
                {
                    [nameof(AIProfileTemplate.Source)] = AITemplateSources.SystemPrompt,
                },
            },
        };

        var template = AIProfileTemplateParser.Parse("reusable-system-prompt", parseResult);

        Assert.Equal(AITemplateSources.SystemPrompt, template.Source);
        Assert.True(template.TryGet<SystemPromptTemplateMetadata>(out var metadata));
        Assert.Equal("You are a reusable system prompt.", metadata.SystemMessage);
        Assert.False(template.TryGet<ProfileTemplateMetadata>(out _));
    }

    [Fact]
    public void Parse_WhenSourceIsPersona_MapsFrontMatterToPersonaMetadata()
    {
        var parseResult = new TemplateParseResult
        {
            Body = "You are Ava, a warm and patient assistant.",
            Metadata = new TemplateMetadata
            {
                Title = "Friendly assistant",
                AdditionalProperties =
                {
                    [nameof(AIProfileTemplate.Source)] = "persona",
                    [nameof(PersonaTemplateMetadata.AssistantName)] = "Ava",
                    [nameof(PersonaTemplateMetadata.Greeting)] = "Hi, I'm Ava.",
                    [nameof(PersonaTemplateMetadata.Language)] = "en-US",
                    [nameof(PersonaTemplateMetadata.VoiceName)] = "marin",
                    [nameof(PersonaTemplateMetadata.VoiceDeploymentName)] = "realtime",
                    [nameof(PersonaTemplateMetadata.AvatarUrl)] = "/media/ava.png",
                },
            },
        };

        var template = AIProfileTemplateParser.Parse("friendly-assistant", parseResult);

        Assert.Equal(AITemplateSources.Persona, template.Source);
        Assert.True(template.TryGet<PersonaTemplateMetadata>(out var persona));
        Assert.Equal("You are Ava, a warm and patient assistant.", persona.Instructions);
        Assert.Equal("Ava", persona.AssistantName);
        Assert.Equal("Hi, I'm Ava.", persona.Greeting);
        Assert.Equal("en-US", persona.Language);
        Assert.Equal("marin", persona.VoiceName);
        Assert.Equal("realtime", persona.VoiceDeploymentName);
        Assert.Equal("/media/ava.png", persona.AvatarUrl);
        Assert.False(template.TryGet<ProfileTemplateMetadata>(out _));
    }

    [Fact]
    public void Parse_ProfileTemplateNamingPersona_LinksThePersona()
    {
        var parseResult = new TemplateParseResult
        {
            Body = "Answer billing questions.",
            Metadata = new TemplateMetadata
            {
                AdditionalProperties =
                {
                    [nameof(AIPersonaMetadata.PersonaId)] = " friendly-assistant ",
                },
            },
        };

        var template = AIProfileTemplateParser.Parse("billing", parseResult);

        Assert.Equal(AITemplateSources.Profile, template.Source);
        Assert.True(template.TryGet<AIPersonaMetadata>(out var link));
        Assert.Equal("friendly-assistant", link.PersonaId);
    }
}
