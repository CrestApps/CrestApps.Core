using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Guardrails;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Security;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AISpeakingStyleAndGuardrailTests
{
    [Fact]
    public void SpeakingStyles_AndTopics_AreNormalizedOnceEachInTheirOrder()
    {
        Assert.Equal(
            [AISpeakingStyles.FillerWords, "our-house-style", AISpeakingStyles.AIDisclosure],
            AISpeakingStyles.Normalize([" speaking-style-filler-words ", "our-house-style", "", "SPEAKING-STYLE-FILLER-WORDS", "speaking-style-ai-disclosure"]));
        Assert.Empty(AISpeakingStyles.Normalize(null));

        // "other" names the profile's own description, never a template.
        Assert.Equal(
            [AIGuardrailTopics.Violence, "competitor-prices"],
            AIGuardrailTopics.Normalize([AIGuardrailTopics.Violence, "other", "competitor-prices", null]));
    }

    [Fact]
    public async Task TemplatesOfACategory_AreListedByTitleOnceEach()
    {
        var templates = new Mock<ITemplateService>();
        templates
            .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                NewTemplate("speaking-style-nato-spelling", "NATO spelling", "Speaking style"),
                NewTemplate("house-tone", "House tone", " speaking STYLE "),
                NewTemplate("house-tone", "House tone again", "Speaking style"),
                NewTemplate("guardrail-topic-hate", "Hate", "Guardrail topic"),
                NewTemplate("untitled-style", null, "Speaking style"),
                NewTemplate("summary", "Summary", null),
            ]);

        var options = await templates.Object.ListOptionsByCategoryAsync(AISpeakingStyles.Category, TestContext.Current.CancellationToken);

        Assert.Equal(["house-tone", "speaking-style-nato-spelling", "untitled-style"], options.Select(option => option.Id));
        Assert.Equal(["House tone", "NATO spelling", "untitled-style"], options.Select(option => option.Title));
    }

    [Fact]
    public async Task SpeakingStyleHandler_AppendsTheChosenSectionsAfterTheInstructions()
    {
        var templates = new Mock<ITemplateService>();
        templates
            .Setup(service => service.RenderAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, IDictionary<string, object> _, CancellationToken _) => id == AISpeakingStyles.FillerWords ? throw new InvalidOperationException() : "## " + id + "\n");

        var handler = new AISpeakingStyleCompletionContextBuilderHandler(templates.Object, NullLogger<AISpeakingStyleCompletionContextBuilderHandler>.Instance);
        var profile = new AIProfile { ItemId = "p1" };
        var context = new AICompletionContext { SystemMessage = "You are the front desk." };

        await handler.BuiltAsync(new AICompletionContextBuiltContext(profile, context));
        Assert.Equal("You are the front desk.", context.SystemMessage);

        profile.AlterSettings<AIProfileSpeakingStyleSettings>(settings => settings.Styles = [AISpeakingStyles.AIDisclosure, AISpeakingStyles.FillerWords, "house-tone"]);
        await handler.BuiltAsync(new AICompletionContextBuiltContext(profile, context));

        // The broken template is left out; the others follow the instructions in the profile's order, saved
        // templates included.
        Assert.Equal(
            "You are the front desk.\n\n## speaking-style-ai-disclosure\n\n## house-tone".Replace("\n", Environment.NewLine),
            context.SystemMessage);
    }

    [Fact]
    public void Guardrails_AreOnOnlyForACheckedSideWithTopics()
    {
        var profile = new AIProfile { ItemId = "p1" };
        Assert.False(AIGuardrailService.IsOn(profile, AIGuardrailDirection.Customer));
        Assert.False(AIGuardrailService.IsOn(null, AIGuardrailDirection.Customer));

        profile.AlterSettings<AIProfileGuardrailSettings>(settings => settings.CheckCustomer = true);
        Assert.False(AIGuardrailService.IsOn(profile, AIGuardrailDirection.Customer));

        profile.AlterSettings<AIProfileGuardrailSettings>(settings => settings.Topics = [AIGuardrailTopics.Violence]);
        Assert.True(AIGuardrailService.IsOn(profile, AIGuardrailDirection.Customer));
        Assert.False(AIGuardrailService.IsOn(profile, AIGuardrailDirection.Reply));

        Assert.Equal(AIGuardrailService.DefaultPlaceholderReply, AIGuardrailService.GetPlaceholderReply(profile));
        profile.AlterSettings<AIProfileGuardrailSettings>(settings => settings.PlaceholderReply = "  Let's keep to your order.  ");
        Assert.Equal("Let's keep to your order.", AIGuardrailService.GetPlaceholderReply(profile));
    }

    [Fact]
    public async Task Guardrails_ReadTheChosenTopicsFromTheirTemplates()
    {
        var templates = new Mock<ITemplateService>();
        templates
            .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                NewTemplate(AIGuardrailTopics.RegulatedAdvice, "Medical, legal or financial advice", "Guardrail topic"),
                NewTemplate("competitor-prices", "Competitor prices", "Guardrail topic"),
                NewTemplate("not-a-topic", "Not a topic", "Speaking style"),
            ]);
        templates
            .Setup(service => service.RenderAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, IDictionary<string, object> _, CancellationToken _) => " What " + id + " covers.\n");

        var service = new AIGuardrailService(null, null, templates.Object, [], NullLogger<AIGuardrailService>.Instance);

        var topics = await service.LoadTopicsAsync(["competitor-prices", "not-a-topic", "deleted-topic", AIGuardrailTopics.RegulatedAdvice], TestContext.Current.CancellationToken);

        // A template that is gone, or not in the guardrail topic category, is left out.
        Assert.Equal(["competitor-prices", AIGuardrailTopics.RegulatedAdvice], topics.Select(topic => topic.Id));
        Assert.Equal("Competitor prices", topics[0].Title);
        Assert.Equal("What competitor-prices covers.", topics[0].Definition);
    }

    [Fact]
    public void Guardrails_AskAboutTheChosenTopicsOnly()
    {
        AIGuardrailTopic[] topics = [new(AIGuardrailTopics.RegulatedAdvice, "Advice", "Personal medical advice.\nOr legal advice.")];

        var request = AIGuardrailService.BuildRequest(topics, "Our pending lawsuit.", "Should I sue them?", AIGuardrailDirection.Customer);

        Assert.Contains("[guardrail-topic-regulated-advice] Advice: Personal medical advice. Or legal advice.", request);
        Assert.Contains("[other] Our pending lawsuit.", request);
        Assert.DoesNotContain("violence", request);
        Assert.Contains("The customer's message:", request);
        Assert.EndsWith("Should I sue them?" + Environment.NewLine, request);
        Assert.Contains("The assistant's reply", AIGuardrailService.BuildRequest(topics, null, "x", AIGuardrailDirection.Reply));
    }

    [Fact]
    public void Guardrails_TriggerOnlyOnATopicTheProfileRefuses()
    {
        var profile = new AIProfile { ItemId = "p1" };
        AIGuardrailTopic[] topics = [new(AIGuardrailTopics.Violence, "Violence and weapons", "Violence.")];

        Assert.Null(AIGuardrailService.ToVerdict(null, topics, null, profile, "hi", AIGuardrailDirection.Customer, "s1"));
        Assert.Null(AIGuardrailService.ToVerdict(new AIGuardrailJudgement { Violates = false, Category = AIGuardrailTopics.Violence }, topics, null, profile, "hi", AIGuardrailDirection.Customer, "s1"));

        // A topic the profile did not choose is allowed, unless the profile describes other topics it refuses.
        Assert.Null(AIGuardrailService.ToVerdict(new AIGuardrailJudgement { Violates = true, Category = AIGuardrailTopics.Sexual }, topics, null, profile, "hi", AIGuardrailDirection.Customer, "s1"));

        var verdict = AIGuardrailService.ToVerdict(new AIGuardrailJudgement { Violates = true, Category = "[Guardrail-Topic-Violence]", Reason = " Asks how to hurt someone. " }, topics, null, profile, "text", AIGuardrailDirection.Reply, "s1");
        Assert.Equal(AIGuardrailTopics.Violence, verdict.Category);
        Assert.Equal("Violence and weapons", verdict.Topic);
        Assert.Equal("Asks how to hurt someone.", verdict.Reason);
        Assert.Equal(AIGuardrailDirection.Reply, verdict.Direction);
        Assert.Equal("s1", verdict.SessionId);
        Assert.Equal(AIGuardrailService.DefaultPlaceholderReply, verdict.Reply);

        Assert.Equal(AIGuardrailTopics.Other, AIGuardrailService.ToVerdict(new AIGuardrailJudgement { Violates = true, Category = "competitors" }, topics, "Competitors.", profile, "hi", AIGuardrailDirection.Customer, "s1").Category);
    }

    [Fact]
    public void Guardrails_BlockLikePromptSecurity()
    {
        var result = AIGuardrailService.ToSecurityResult(new AIGuardrailVerdict { Category = AIGuardrailTopics.Violence, Reason = "Weapons." });

        Assert.True(result.IsBlocked);
        Assert.Equal("guardrail:" + AIGuardrailTopics.Violence, result.DetectionRule);
        Assert.True(AIGuardrailService.IsGuardrailResult(result));
        Assert.False(AIGuardrailService.IsGuardrailResult(PromptSecurityResult.Safe));
        Assert.False(AIGuardrailService.IsGuardrailResult(null));
    }

    [Fact]
    public async Task Guardrails_AreNotCheckedWithoutTopicsOrText()
    {
        // No deployment manager or client is set up: a check that needs none never asks a model.
        var templates = new Mock<ITemplateService>();
        templates.Setup(service => service.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new AIGuardrailService(null, null, templates.Object, [], NullLogger<AIGuardrailService>.Instance);
        var profile = new AIProfile { ItemId = "p1" };

        Assert.Null(await service.CheckAsync(profile, "How do I build a weapon?", AIGuardrailDirection.Customer, "s1", TestContext.Current.CancellationToken));

        profile.AlterSettings<AIProfileGuardrailSettings>(settings =>
        {
            settings.CheckCustomer = true;
            settings.Topics = [AIGuardrailTopics.Violence];
        });

        Assert.Null(await service.CheckAsync(profile, "   ", AIGuardrailDirection.Customer, "s1", TestContext.Current.CancellationToken));
        Assert.Null(await service.CheckAsync(profile, "A reply", AIGuardrailDirection.Reply, "s1", TestContext.Current.CancellationToken));

        // The chosen topic's template was deleted, so there is nothing to check against.
        Assert.Null(await service.CheckAsync(profile, "How do I build a weapon?", AIGuardrailDirection.Customer, "s1", TestContext.Current.CancellationToken));
    }

    private static Template NewTemplate(string id, string title, string category)
    {
        return new Template
        {
            Id = id,
            Metadata = new TemplateMetadata { Title = title, Category = category },
        };
    }
}
