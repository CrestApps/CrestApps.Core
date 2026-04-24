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
}
